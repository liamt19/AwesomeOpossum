
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

//#define DBG
//#define WRITE_PGN

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
            using var ostr = File.Open(fName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            using var outWriter = new BinaryWriter(ostr);

            MontyPack pack = new();

            ulong totalPositions = 0;
            ulong totalDepths = 0;

            var info = SearchInformation.DatagenStandard(pos, softNodeLimit, (int)depthLimit);
            var prelimInfo = SearchInformation.DatagenPrelim(pos, softNodeLimit, (int)depthLimit);

            for (ulong gameNum = 0; gameNum < gamesToRun; gameNum++)
            {
                GetStartPos(thread, ref pack, ref prelimInfo);

                GameResult result = GameResult.None;
                int winPlies = 0, drawPlies = 0, lossPlies = 0;

                while (result == GameResult.None)
                {
                    int nLegalMoves = SetupThread(pos, thread);
                    if (nLegalMoves == 0)
                    {
                        result = pos.ToMove == White ? GameResult.BlackWin : GameResult.WhiteWin;
                        break;
                    }

                    thread.Playout(ref info);

                    var (idx, move, scoreSig) = tree.GetBestAction(0);
                    bool isMate = scoreSig > 1.0f || scoreSig < 0.0f;
                    var score = (int)InvSigmoid(scoreSig);

#if DBG_PRINT
                    debugStreamWriter.WriteLine($"{pos.GetSFen()}\t{move} {score}\t");
#endif
                    totalDepths += (ulong)thread.AverageDepth;

                    if (isMate)
                    {
                        result = score > 0 ? GameResult.WhiteWin : GameResult.BlackWin;
                        break;
                    }

                    pos.MakeMove(move);

                    if (score >= AdjudicateScore)
                    {
                        winPlies++;
                        lossPlies = 0;
                        drawPlies = 0;
                    }
                    else if (score <= -AdjudicateScore)
                    {
                        winPlies = 0;
                        lossPlies++;
                        drawPlies = 0;
                    }
                    else
                    {
                        winPlies = 0;
                        lossPlies = 0;
                        drawPlies = 0;
                    }

                    if (winPlies >= AdjudicatePlies)
                    {
                        result = GameResult.BlackWin;
                    }
                    else if (lossPlies >= AdjudicatePlies)
                    {
                        result = GameResult.WhiteWin;
                    }
                    else if (drawPlies >= 10)
                    {
                        result = GameResult.Draw;
                    }

                    pack.Push(move, (short)score);

                    if (pack.IsAtMoveLimit())
                        result = GameResult.Draw;
                }

#if DBG_PRINT
                debugStreamWriter.WriteLine($"done, {result}");
#endif

                totalPositions += (uint)pack.MoveIndex;

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
            Span<Move> randomMoves = stackalloc Move[16];

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
                    randomMoves[i] = rMove;
                    pos.MakeMove(rMove);
                }

                if (pos.GenLegal(legalMoves) == 0)
                    continue;

                SetupThread(pos, thread);
                thread.Playout(ref prelim);
                var score = InvSigmoid(thread.Tree.GetBestAction(0).q);
                if (Math.Abs(score) >= MaxOpeningScore)
                    continue;

                for (int i = 0; i < randMoveCount; i++)
                    pack.PushUnscored(randomMoves[i]);

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
    }
}
