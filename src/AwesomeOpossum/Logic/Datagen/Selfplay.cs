

using AwesomeOpossum.Logic.MCTS;
using AwesomeOpossum.Logic.Threads;
using System.Runtime.CompilerServices;
using System.Threading;
using static AwesomeOpossum.Logic.Datagen.DatagenParameters;

namespace AwesomeOpossum.Logic.Datagen;

public static class DatagenParameters
{
    public const int HashSize = 8;

    public const int RandomPlies = 4;

    public const int IterationLimit = 1000;
    public const int DepthLimit = 14;

    public const bool DFRC = true;

    public const bool UseBook = true;
    public const string BookPath = "DFRC_4852_v1.epd.fixed";
}

public static unsafe class Selfplay
{
    private static int Seed = Environment.TickCount;
    private static readonly ThreadLocal<Random> ThreadRNG = new(() => new Random(Interlocked.Increment(ref Seed)));

    public static void RunValueGames(ulong gamesToRun, int threadID)
    {
        Tree tree = new(HashSize);
        ref var rootNode = ref tree.RootNode;
        SearchThread thread = new(0) { Tree = tree, IsDatagen = true };
        Position pos = thread.RootPosition;
        pos.IsChess960 = DFRC;

        using var ostr = File.Open(Path.Combine(GetValueFolder(), OutFileName(threadID)), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        using var outWriter = new BinaryWriter(ostr);

        Span<MontyScoredMove> sd = stackalloc MontyScoredMove[MontyPack.MaxSize];
        MontyValuePack pack = new() { moves = sd };

        ulong totalPositions = 0, totalNodes = 0;
        ulong totalSearches = 0, totalFill = 0, totalIters = 0;

        var info = SearchInformation.DatagenStandard(pos, IterationLimit, DepthLimit);

        for (ulong gameNum = 0; gameNum < gamesToRun; gameNum++)
        {
            pack.Clear();
            GetStartPos(thread, threadID);
            pack.startpos = MontyPosition.FromPosition(pos);
            pack.rights = MontyCastling.FromPosition(pos);

            NodeStateKind playoutState = NodeStateKind.Unterminated;

            while (playoutState == NodeStateKind.Unterminated)
            {
                thread.Reset();
                thread.SetStop(false);

                thread.Playout(ref info);

                var (move, scoreSig) = tree.BestRootAction;

                totalSearches++;
                totalNodes += (ulong)rootNode.NumChildren;
                totalFill += tree.FillLevel;
                totalIters += thread.PlayoutIteration;

                var bm = ConvertToMontyMoveFormatBecauseOfCourseItIsDifferent(move, pos);
                pack.Push(pos.ToMove, bm, scoreSig);
                if (pack.IsAtMoveLimit)
                    break;

                pos.MakeMove(move);

                playoutState = pos.PlayoutState().Kind;
            }

            GameResult result = playoutState switch
            {
                NodeStateKind.Loss => pos.ToMove == White ? GameResult.Loss : GameResult.Win,
                NodeStateKind.Win  => pos.ToMove == White ? GameResult.Win  : GameResult.Loss,
                 _                 =>                       GameResult.Draw,
            };

            totalPositions += (uint)pack.NumEntries;

            ProgressBroker.ReportProgress(threadID, gameNum + 1, totalPositions, totalNodes, totalFill / totalSearches, totalIters / totalSearches);
            pack.Write(result, outWriter);
        }
    }


    public static void RunPolicyGames(ulong gamesToRun, int threadID)
    {
        Tree tree = new(HashSize);
        ref var rootNode = ref tree.RootNode;
        SearchThread thread = new(0) { Tree = tree, IsDatagen = true };
        Position pos = thread.RootPosition;
        pos.IsChess960 = DFRC;

        using var ostr = File.Open(Path.Combine(GetPolicyFolder(), OutFileName(threadID)), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        using var outWriter = new BinaryWriter(ostr);

        Span<(Move move, uint visits)> distSpan = new (Move move, uint visits)[256];
        Span<SearchData> sd = stackalloc SearchData[MontyPack.MaxSize];

        MontyPack pack = new() { moves = sd };

        ulong totalPositions = 0, totalNodes = 0;
        ulong totalSearches = 0, totalFill = 0, totalIters = 0;

        var info = SearchInformation.DatagenStandard(pos, IterationLimit, DepthLimit);

        for (ulong gameNum = 0; gameNum < gamesToRun; gameNum++)
        {
            pack.Clear();
            GetStartPos(thread, threadID);
            pack.startpos = MontyPosition.FromPosition(pos);
            pack.rights = MontyCastling.FromPosition(pos);

            int moveNum = 0;
            NodeStateKind playoutState = NodeStateKind.Unterminated;

            while (playoutState == NodeStateKind.Unterminated)
            {
                thread.Reset();
                thread.SetStop(false);

                thread.Playout(ref info);

                var (move, scoreSig) = tree.BestRootAction;
                var children = tree.ChildrenOf(rootNode);
                int nLegalMoves = rootNode.NumChildren;

                totalSearches++;
                totalNodes += (ulong)nLegalMoves;
                totalFill += tree.FillLevel;
                totalIters += thread.PlayoutIteration;

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
                    playoutState = NodeStateKind.Draw;
            }

            GameResult result = playoutState switch
            {
                NodeStateKind.Loss => pos.ToMove == White ? GameResult.Loss : GameResult.Win,
                NodeStateKind.Win => pos.ToMove == White ? GameResult.Win : GameResult.Loss,
                _ => GameResult.Draw,
            };

            totalPositions += (uint)pack.NumEntries;

            ProgressBroker.ReportProgress(threadID, gameNum + 1, totalPositions, totalNodes, totalFill / totalSearches, totalIters / totalSearches);
            pack.AddResultsAndWrite(result, outWriter);
        }
    }


    public static void DatagenProlog(ulong numGames, ulong threads, bool policy)
    {
#if !DATAGEN
        Log($"WARN: Not compiled with DATAGEN defined! Gini is being used.");
#endif

        Log($"Kind:         {(policy ? "Policy" : "Value")}");
        Log($"Threads:      {threads}");
        Log($"Games/thread: {numGames:N0}");
        Log($"Total games:  {numGames * threads:N0}");
        Log($"Iter limit:   {IterationLimit:N0}");
        Log($"Depth limit:  {DepthLimit}");
        Log($"Variant:      {(DFRC ? "DFRC" : "Standard")}");
        Log($"Book:         {(UseBook ? BookPath : "<None>")}");
        Log($"Hit enter to begin...");
        _ = Console.ReadLine();

        SearchOptions.Hash = HashSize;
        SearchOptions.UCI_Chess960 = DFRC;
        TimeManager.RemoveSoftLimit();
        TimeManager.RemoveHardLimit();
    }

    public static void SetupBookHandlerMaybe(ulong threads)
    {
        if (UseBook)
            BookHandler.Initialize(BookPath, (int)threads);
    }


    private static string GetValueFolder()
    {
        string v = Path.Combine("data", "value");
        try { Directory.CreateDirectory(v); } catch { }
        return v;
    }

    private static string GetPolicyFolder()
    {
        string v = Path.Combine("data", "policy");
        try { Directory.CreateDirectory(v); } catch { }
        return v;
    }

    private static string OutFileName(int tid) => $"{(DFRC ? "dfrc_" : "")}{IterationLimit}it_{DepthLimit}d_{tid}.bin";



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


    // [35, 20, 20, 8, 12, 5]
    private static ReadOnlySpan<int> PieceProbs => [35, 55, 75, 83, 95, 100];
    private static void GetStartPos(SearchThread thread, int threadID)
    {
        Position pos = thread.RootPosition;
        ref Bitboard bb = ref pos.bb;

        Random rand = ThreadRNG.Value;
        Move* legalMoves = stackalloc Move[MoveListSize];

        Span<Move> candidates = stackalloc Move[MoveListSize];

        int RandomPieceType()
        {
            var r = rand.Next(0, 100 + 1);
            for (int j = 0; j < PieceNB; j++)
                if (r <= PieceProbs[j])
                    return j;

            return 0;
        } 

        thread.SetStop(false);
        thread.ClearTree();

        while (true)
        {
            Retry:

            if (UseBook)
                pos.LoadFromFEN(BookHandler.GetStartpos(threadID));
            else if (DFRC)
                pos.SetupForDFRC(rand.Next(0, 960), rand.Next(0, 960));
            else
                pos.LoadFromFEN(InitialFEN);

            int randMoveCount = rand.Next(RandomPlies, RandomPlies + 1);
            for (int i = 0; i < randMoveCount; i++)
            {
                int legals = pos.GenLegal(legalMoves);
                if (legals == 0)
                    goto Retry;

                Move toMake = Move.Null;
                while (toMake == Move.Null)
                {
                    candidates.Clear();
                    int ci = 0;

                    int randomPt = RandomPieceType();
                    for (int j = 0; j < legals; j++)
                    {
                        var m = legalMoves[j];
                        if (bb.GetPieceAtIndex(m.From) == randomPt)
                            candidates[ci++] = m;
                    }

                    if (ci != 0)
                        toMake = candidates[rand.Next(0, ci)];
                }

                pos.MakeMove(toMake);
            }

            if (!pos.HasLegalMoves())
                continue;

            return;
        }
    }


    private static int SetupThread(Position pos, SearchThread td)
    {
        td.Reset();
        td.SetStop(false);

        Move* list = stackalloc Move[MoveListSize];
        int size = pos.GenLegal(list);

        return size;
    }

    private static Move ConvertToMontyMoveFormatBecauseOfCourseItIsDifferent(Move m, Position pos)
    {
        int f = 0;

        var (src, dst) = m.Unpack();

        if (m.IsCastle)
        {
            f = (dst > src) ? 2 : 3;
            dst = m.CastlingKingSquare();
        }
        else
        {
            if ((src ^ dst) == 16 && pos.bb.GetPieceAtIndex(src) == Pawn)
                f = 1;
            else if (m.IsEnPassant)
                f = 5;

            if (pos.bb.GetPieceAtIndex(dst) != None)
                f |= 4;

            if (m.IsPromotion)
                f |= (0b0111 + m.PromotionTo);
        }

        return new Move((ushort)((src << 10) | (dst << 4) | f));
    }
}
