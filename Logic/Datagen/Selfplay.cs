﻿
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

using static AwesomeOpossum.Logic.Datagen.DatagenParameters;

using AwesomeOpossum.Logic.Threads;
using AwesomeOpossum.Logic.MCTS;
using System.Runtime.CompilerServices;

namespace AwesomeOpossum.Logic.Datagen;

public static unsafe class Selfplay
{
    private static int Seed = Environment.TickCount;
    private static readonly ThreadLocal<Random> ThreadRNG = new(() => new Random(Interlocked.Increment(ref Seed)));

    public static void RunGames(ulong gamesToRun, int threadID, ulong softNodeLimit = SoftNodeLimit, ulong depthLimit = DepthLimit, bool dfrc = false)
    {
        SearchOptions.Hash = HashSize;
        SearchOptions.UCI_Chess960 = dfrc;
        TimeManager.RemoveSoftLimit();
        TimeManager.RemoveHardLimit();

        Tree tree = new(HashSize);
        ref var rootNode = ref tree.RootNode;
        SearchThread thread = new(0) { Tree = tree, IsDatagen = true };
        Position pos = thread.RootPosition;
        ref Bitboard bb = ref pos.bb;

        string fName = $"{softNodeLimit / 1000}k_{depthLimit}d_{threadID}.bin";
        using var ostr = File.Open(fName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        using var outWriter = new BinaryWriter(ostr);

        Span<(Move move, uint visits)> distSpan = new (Move move, uint visits)[256];
        Span<SearchData> sd = stackalloc SearchData[MontyPack.MaxSize];
        ScoredMove* legalMoves = stackalloc ScoredMove[MoveListSize];

        MontyPack pack = new() { moves = sd };

        ulong totalPositions = 0;
        ulong totalNodes = 0;
        ulong totalFill = 0;
        ulong totalSearches = 0;

        var info = SearchInformation.DatagenStandard(pos, softNodeLimit, (int)depthLimit);
        var prelimInfo = SearchInformation.DatagenPrelim(pos, softNodeLimit, (int)depthLimit);

        for (ulong gameNum = 0; gameNum < gamesToRun; gameNum++)
        {
            GetStartPos(thread, ref pack, ref prelimInfo);
            //sd.Clear();   //  Hopefully this is fine to skip

            int moveNum = 0;
            NodeStateKind playoutState = NodeStateKind.Unterminated;

            while (playoutState == NodeStateKind.Unterminated)
            {
                thread.Reset();
                thread.SetStop(false);

                int nLegalMoves = pos.GenLegal(legalMoves);
                if (nLegalMoves == 0)
                {
                    playoutState = pos.PlayoutState().Kind;
                    break;
                }

                thread.Playout(ref info);

                var (idx, move, scoreSig) = tree.GetBestAction(0);
                var score = (int)InvSigmoid(scoreSig);
                var children = tree.ChildrenOf(rootNode);

                totalSearches++;
                totalNodes += (ulong)nLegalMoves;
                totalFill += tree.FillLevel;

                sd[moveNum].best_move = ConvertToMontyMoveFormatBecauseOfCourseItIsDifferent(move, pos);
                sd[moveNum].score = scoreSig;
                sd[moveNum].NumChildren = nLegalMoves;

                var dist = distSpan[..nLegalMoves];
                dist.Clear();

                //  Order (move, visit) in ascending order based on the raw value of the move
                for (int i = 0; i < nLegalMoves; i++)
                    dist[i] = (ConvertToMontyMoveFormatBecauseOfCourseItIsDifferent(children[i].Move, pos), children[i].Visits);

                SortDistribution(dist);

                for (int i = 0; i < nLegalMoves; i++)
                    sd[moveNum].visit_distribution[i] = dist[i].visits;

                pack.Push(sd[moveNum]);

                pos.MakeMove(move);
                moveNum++;

                playoutState = pos.PlayoutState().Kind;

                if (pack.IsAtMoveLimit)
                {
                    playoutState = NodeStateKind.Draw;
                    break;
                }
            }

            float result = playoutState switch
            {
                NodeStateKind.Loss => pos.ToMove == White ? 0f : 1f,
                NodeStateKind.Win => pos.ToMove == White ? 1f : 0f,
                _ => 0.5f,
            };


            totalPositions += (uint)pack.NumEntries;

            ProgressBroker.ReportProgress(threadID, gameNum + 1, totalPositions, totalNodes, totalFill / totalSearches);
            pack.AddResultsAndWrite(result, outWriter);
        }
    }

    public static void SortDistribution(Span<(Move move, uint visits)> dist) => QuickSort(dist, 0, dist.Length - 1);
    private static void QuickSort(Span<(Move move, uint visits)> dist, int low, int high)
    {
        if (low < high)
        {
            int pivotIndex = Partition(dist, low, high);
            QuickSort(dist, low, pivotIndex - 1);
            QuickSort(dist, pivotIndex + 1, high);
        }
    }

    [MethodImpl(Inline)]
    private static int Partition(Span<(Move move, uint visits)> dist, int low, int high)
    {
        var pivot = dist[high].move.GetData();
        int i = low - 1;

        for (int j = low; j < high; j++)
        {
            if (dist[j].move.GetData() < pivot)
            {
                i++;
                (dist[i], dist[j]) = (dist[j], dist[i]);
            }
        }

        (dist[i + 1], dist[high]) = (dist[high], dist[i + 1]);
        return i + 1;
    }



    private static void GetStartPos(SearchThread thread, ref MontyPack pack, ref SearchInformation prelim)
    {
        Position pos = thread.RootPosition;

        Random rand = ThreadRNG.Value;
        ScoredMove* legalMoves = stackalloc ScoredMove[MoveListSize];

        pack.Clear();

        while (true)
        {
            thread.SetStop(false);

            Retry:
            pos.LoadFromFEN(InitialFEN);

            int randMoveCount = rand.Next(8, 9 + 1);
            for (int i = 0; i < randMoveCount; i++)
            {
                int legals = pos.GenLegal(legalMoves);

                if (legals == 0)
                    goto Retry;

                Move rMove = legalMoves[rand.Next(0, legals)].Move;
                pos.MakeMove(rMove);
            }

            if (pos.GenLegal(legalMoves) == 0)
                continue;

            SetupThread(pos, thread);
            thread.Playout(ref prelim);
            var score = InvSigmoid(thread.Tree.GetBestAction(0).q);
            if (Math.Abs(score) >= MaxOpeningScore)
                continue;

            pack.startpos = MontyPosition.FromPosition(pos);

            thread.ClearTree();
            return;
        }
    }


    private static int SetupThread(Position pos, SearchThread td)
    {
        td.Reset();
        td.SetStop(false);

        ScoredMove* list = stackalloc ScoredMove[MoveListSize];
        int size = pos.GenLegal(list);

        return size;
    }

    private static Move ConvertToMontyMoveFormatBecauseOfCourseItIsDifferent(Move m, Position pos)
    {
        int f = 0;

        var (src, dst) = m.Unpack();

        if ((src ^ dst) == 16 && pos.bb.GetPieceAtIndex(src) == Pawn)
            f = 1;
        else if (m.IsEnPassant)
            f = 5;
        else if (m.IsCastle)
        {
            f = (dst > src) ? 2 : 3;
            dst = m.CastlingKingSquare();
        }

        if (pos.bb.GetPieceAtIndex(dst) != None)
            f |= 4;

        if (m.IsPromotion)
            f |= (0b0111 + m.PromotionTo);

        return new Move((ushort)((src << 10) | (dst << 4) | f));
    }
}
