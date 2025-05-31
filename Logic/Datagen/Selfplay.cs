
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

using System.Runtime.InteropServices;

using static AwesomeOpossum.Logic.Datagen.DatagenParameters;

using AwesomeOpossum.Logic.NN;
using AwesomeOpossum.Logic.Threads;
using AwesomeOpossum.Logic.MCTS;
using System.Text;

namespace AwesomeOpossum.Logic.Datagen
{
    public static unsafe class Selfplay
    {
        private static int Seed = Environment.TickCount;
        private static readonly ThreadLocal<Random> ThreadRNG = new(() => new Random(Interlocked.Increment(ref Seed)));

        public static void RunGames(ulong gamesToRun, int threadID, ulong softNodeLimit = SoftNodeLimit, ulong depthLimit = DepthLimit, bool dfrc = false)
        {
            SearchOptions.Hash = HashSize;
            SearchOptions.UCI_Chess960 = dfrc;
            TimeManager.RemoveHardLimit();

            Tree tree = new(HashSize);
            ref var rootNode = ref tree.RootNode;
            SearchThread thread = new(0) { Tree = tree, IsDatagen = true };
            Position pos = thread.RootPosition;
            ref Bitboard bb = ref pos.bb;

            string fName = $"{softNodeLimit / 1000}k_{depthLimit}d_{threadID}.bin";
            using var ostr = File.Open(fName, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var outWriter = new BinaryWriter(ostr);

            Span<SearchData> sd = stackalloc SearchData[1024];
            ScoredMove* legalMoves = stackalloc ScoredMove[MoveListSize];

            MontyPack pack = new();
            pack.moves = sd;

            ulong totalPositions = 0;
            ulong totalDepths = 0;

            var info = SearchInformation.DatagenStandard(pos, softNodeLimit, (int)depthLimit);
            var prelimInfo = SearchInformation.DatagenPrelim(pos, softNodeLimit, (int)depthLimit);

            for (ulong gameNum = 0; gameNum < gamesToRun; gameNum++)
            {
                GetStartPos(thread, ref pack, ref prelimInfo);
                sd = new SearchData[1024];
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

#if DBG_PRINT
                    debugStreamWriter.WriteLine($"{pos.GetSFen()}\t{move} {score}\t");
#endif
                    totalDepths += (ulong)thread.AverageDepth;

                    sd[moveNum].best_move = ConvertToMontyMoveFormatBecauseOfCourseItIsDifferent(move, pos);
                    sd[moveNum].score = scoreSig;
                    sd[moveNum].NumChildren = nLegalMoves;
                    if (nLegalMoves != 0)
                    {
                        var children = tree.ChildrenOf(rootNode);
                        for (int i = 0; i < rootNode.NumChildren; i++)
                            sd[moveNum].visit_distribution[i] = children[i].Visits;
                    }

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

#if DBG_PRINT
                debugStreamWriter.WriteLine($"done, {result}");
#endif

                totalPositions += (uint)pack.NumEntries;

                ProgressBroker.ReportProgress(threadID, gameNum, totalPositions, totalDepths);

                pack.AddResultsAndWrite(result, outWriter);
            }
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
}
