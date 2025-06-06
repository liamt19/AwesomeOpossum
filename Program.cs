﻿using System.Data;
using System.Text;

using AwesomeOpossum.Logic.Datagen;
using AwesomeOpossum.Logic.MCTS;
using AwesomeOpossum.Logic.Evaluation;
using AwesomeOpossum.Logic.Threads;

namespace AwesomeOpossum
{

    public static unsafe class Program
    {
        private static Position p;
        private static SearchInformation info;

        public static void Main(string[] args)
        {
            if (args.Length != 0)
            {
                if (args[0] == "bench")
                {
                    SearchBench.Go(openBench: true);
                    Environment.Exit(0);
                }
                else if (args[0] == "compiler")
                {
                    Console.WriteLine(GetCompilerInfo());
                    Environment.Exit(0);
                }
                else if (args[0] == "datagen")
                {
                    HandleDatagenCommand(args);
                    Environment.Exit(0);
                }
            }

            InitializeAll();

            p = new Position(owner: GlobalSearchPool.MainThread);
            info = new SearchInformation(p);

            DoInputLoop();
        }

        public static void InitializeAll()
        {
            if (!System.Diagnostics.Debugger.IsAttached)
            {
                AppDomain.CurrentDomain.UnhandledException += ExceptionHandling.CurrentDomain_UnhandledException;
            }


            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) {
                if (!GlobalSearchPool.MainThread.ShouldStop())
                {
                    //  If a search is ongoing, stop it instead of closing the console.
                    GlobalSearchPool.StopAllThreads();
                    e.Cancel = true;
                }

                //  Otherwise, e.Cancel == false and the program exits normally
            };


            //  Console.ReadLine() has a buffer of 256 (UTF-16?) characters, which is only large enough to handle
            //  "position startpos moves ..." commands containing fewer than 404 moves.
            //  This should double the amount that Console.ReadLine() can handle.
            //  Thanks to https://github.com/eduherminio for spotting this
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, false, 2048 * 4));

            Interop.CheckAnsi();

            //  Give the VS debugger a friendly name for the main program thread
            Thread.CurrentThread.Name = "MainThread";
        }


        private static void DoInputLoop()
        {
            Log($"AwesomeOpossum version {EngineBuildVersion}\r\n");

            ForceGC();

            ThreadSetup setup = new ThreadSetup();
            while (true)
            {
                string input = Console.ReadLine();
                if (input == null || input.Length == 0)
                {
                    continue;
                }
                string[] param = input.Split(' ');

                if (input.EqualsIgnoreCase("uci"))
                {
                    new UCIClient(p).Run();
                }
                else if (input.EqualsIgnoreCase("ucinewgame"))
                {
                    p = new Position(InitialFEN, owner: GlobalSearchPool.MainThread);
                    p.Owner.AssocPool.Clear();
                }
                else if (input.EqualsIgnoreCase("listmoves"))
                {
                    PrintMoves();
                }
                else if (input.EqualsIgnoreCase("eval"))
                {
                    Log($"Value: {ValueNetwork.Evaluate(p)}");
                }
                else if (input.EqualsIgnoreCase("eval all"))
                {
                    HandleEvalAllCommand();
                }
                else if (input.EqualsIgnoreCase("policy"))
                {
                    HandlePolicyCommand();
                }
                else if (input.EqualsIgnoreCase("d"))
                {
                    Log(p.ToString());
                }
                else if (input.EqualsIgnoreCase("compiler"))
                {
                    Log(GetCompilerInfo());
                }
                else if (input.StartsWithIgnoreCase("position"))
                {
                    HandlePositionCommand(input, setup);
                }
                else if (input.StartsWithIgnoreCase("go"))
                {
                    HandleGoCommand(input, setup);
                }
                else if (input.StartsWithIgnoreCase("move "))
                {
                    p.TryMakeMove(input[5..]);
                    Log(p.ToString());
                }
                else if (input.StartsWithIgnoreCase("stop"))
                {
                    GlobalSearchPool.StopAllThreads();
                }
                else if (input.StartsWithIgnoreCase("load"))
                {
                    HandleLoadCommand(input);
                }
                else if (input.StartsWithIgnoreCase("trace"))
                {
                    HandleTraceCommand(input);
                }
                else if (input.EqualsIgnoreCase("pretty"))
                {
                    UCI_PrettyPrint = !UCI_PrettyPrint;
                }
                else if (input.EqualsIgnoreCase("wdl"))
                {
                    UCI_ShowWDL = !UCI_ShowWDL;
                }
                else if (input.StartsWithIgnoreCase("threads"))
                {
                    if (int.TryParse(param[1], out int threadCount))
                    {
                        Threads = threadCount;
                        GlobalSearchPool.Resize(Threads);
                    }
                }
                else if (input.StartsWithIgnoreCase("multipv"))
                {
                    if (int.TryParse(param[1], out int mpv))
                    {
                        MultiPV = mpv;
                    }
                }
                else if (input.StartsWithIgnoreCase("hash"))
                {
                    if (int.TryParse(param[1], out int hash))
                    {
                        Hash = hash;
                        GlobalSearchPool.ResizeHashes();

                    }
                }
                else if (input.StartsWithIgnoreCase("bench"))
                {
                    HandleBenchCommand(input);
                }
                else if (input.StartsWithIgnoreCase("datagen") || input.StartsWithIgnoreCase("selfplay"))
                {
                    string[] splits = input.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    HandleDatagenCommand(splits);
                }
                else if (input.StartsWithIgnoreCase("quit") || input.StartsWithIgnoreCase("exit"))
                {
                    break;
                }
                else
                {
                    //  You can just copy paste in a FEN string rather than typing "position fen" before it.
                    if (input.Where(x => x == '/').Count() == 7)
                    {
                        if (p.LoadFromFEN(input.Trim()))
                        {
                            Log($"Loaded fen '{p.GetFEN()}'");
                        }
                    }
                    else
                    {
                        Log($"Unknown token '{input}'");
                    }
                }
            }
        }

        private static void DoPerftDivide(int depth)
        {
            if (depth <= 0) return;

            ulong total = 0;

            //  The Position "p" has UpdateNN == true, which we don't want
            //  for purely Perft usage.
            Position pos = new Position(p.GetFEN(), false, null);
            pos.IsChess960 = p.IsChess960;

            Stopwatch sw = Stopwatch.StartNew();

            Span<ScoredMove> list = stackalloc ScoredMove[MoveListSize];
            int size = pos.GenLegal(list);

            for (int i = 0; i < size; i++)
            {
                Move m = list[i].Move;
                pos.MakeMove(m);
                ulong result = depth > 1 ? pos.Perft(depth - 1) : 1;
                pos.UnmakeMove(m);
                Log(m.ToString() + ": " + result);
                total += result;
            }
            sw.Stop();

            Log($"\r\nNodes searched: {total} in {sw.Elapsed.TotalSeconds} s ({(int)(total / sw.Elapsed.TotalSeconds):N0} nps)\r\n");
        }

        private static void HandleEvalAllCommand()
        {
            Log($"Static evaluation ({ColorToString(p.ToMove)}'s perspective): {ValueNetwork.Evaluate(p)}");
            Log($"\r\nMove evaluations ({ColorToString(p.ToMove)}'s perspective):");

            ScoredMove* list = stackalloc ScoredMove[MoveListSize];
            int size = p.GenLegal(list);
            List<(Move mv, int eval)> scoreList = new();

            for (int i = 0; i < size; i++)
            {
                Move m = list[i].Move;
                p.MakeMove(m);
                scoreList.Add((m, -ValueNetwork.Evaluate(p)));
                p.UnmakeMove(m);
            }

            var sorted = scoreList.OrderByDescending(x => x.eval).ToList();
            for (int i = 0; i < sorted.Count; i++)
                Log($"{sorted[i].mv.ToString(p)}: {sorted[i].eval}");
        }

        private static void HandlePolicyCommand()
        {
            float* rawPolicy = stackalloc float[256];
            ScoredMove* moves = stackalloc ScoredMove[256];
            uint count = (uint)p.GenLegal(moves);
            
            List<(Move mv, float policy, float raw)> scoreList = new();

            PolicyNetwork.RefreshPolicyAccumulator(p);
            float maxScore = float.MinValue;
            for (uint i = 0; i < count; i++)
            {
                rawPolicy[i] = moves[i].Score = SearchUtils.PolicyForMove(p, moves[i].Move);
                maxScore = MathF.Max(maxScore, moves[i].Score);
            }

            float total = 0.0f;
            for (uint i = 0; i < count; i++)
            {
                moves[i].Score = float.Exp(moves[i].Score - maxScore);
                total += moves[i].Score;
            }

            for (uint i = 0; i < count; i++)
                scoreList.Add((moves[i].Move, (moves[i].Score / total * 100.0f), rawPolicy[i]));

            var sorted = scoreList.OrderBy(x => x.raw).ToList();
            for (int i = 0; i < sorted.Count; i++)
                Log($"{sorted[i].mv, -6} {sorted[i].mv.ToString(p),-5}->{sorted[i].policy,9:0.000}%{sorted[i].raw,12:0.0}");
        }

        private static void PrintMoves()
        {
            ScoredMove* pseudo = stackalloc ScoredMove[MoveListSize];
            int pseudoCnt = p.GenPseudoLegal(pseudo);
            Log($"Pseudo: [{Stringify(pseudo, p, pseudoCnt)}]");

            ScoredMove* legal = stackalloc ScoredMove[MoveListSize];
            int legalCnt = p.GenLegal(legal);
            Log($"Legal: [{Stringify(legal, p, legalCnt)}]");
        }


        private static void HandleLoadCommand(string input)
        {
            if (input.Length < 5)
            {
                Log("No file provided!");
                return;
            }

            input = input[5..];

            if (File.Exists(input))
            {
                ValueNetwork.Initialize(input, exitIfFail: false);
                ValueNetwork.RefreshAccumulator(p);
            }
            else
            {
                Log($"Couldn't find the file '{input}'");
            }
        }


        private static void HandleTraceCommand(string input)
        {
            if (input.ContainsIgnoreCase("piece"))
            {
                string[] splits = input.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (splits.Length == 4)
                {
                    int pc = StringToColor(splits[2]);

                    if (pc == Color.ColorNB)
                    {
                        Log("Invalid color for \"trace piece\" command! It should be formatted like \"trace piece black knight\".");
                        return;
                    }

                    int pt = StringToPiece(splits[3]);
                    if (pt == Piece.None)
                    {
                        Log("Invalid type for \"trace piece\" command! It should be formatted like \"trace piece black knight\".");
                        return;
                    }

                    NNUE.TracePieceValues(pt, pc);
                }
                else
                {
                    Log("Invalid input for \"trace piece\" command! It should be formatted like \"trace piece black knight\".");
                }
            }
            else
            {
                NNUE.Trace(p);
            }
        }

        private static void HandleGoCommand(string input, ThreadSetup setup)
        {
            string[] param = input.Split(' ');

            if (param.Length > 2)
            {
                if (param[1].ContainsIgnoreCase("perftp"))
                {
                    if (int.TryParse(param[2], out int depth) && depth > 0)
                    {
                        Task.Run(() =>
                        {
                            Position temp = new Position(p.GetFEN(), false, owner: GlobalSearchPool.MainThread);
                            temp.PerftParallel(depth, true);
                        });
                    }

                    return;
                }
                else if (param[1].ContainsIgnoreCase("perft"))
                {
                    if (int.TryParse(param[2], out int depth))
                    {
                        Task.Run(() => DoPerftDivide(depth));
                    }

                    return;
                }
            }
            else if (input.ContainsIgnoreCase("perft"))
            {
                Log("'go' commands involving perft must have a specified depth!");
                return;
            }

            p.Owner.AssocPool.Clear();

            info = new SearchInformation(p, MaxDepth);
            TimeManager.Reset();

            UCIClient.ParseGo(param, ref info, setup);
            GlobalSearchPool.StartSearch(p, ref info, setup);
        }

        private static void HandlePositionCommand(string input, ThreadSetup setup)
        {
            ParsePositionCommand(input.Split(' ', StringSplitOptions.RemoveEmptyEntries), p, setup);
            Log($"Loaded fen '{p.GetFEN()}'");
        }


        private static void HandleDatagenCommand(string[] args)
        {
            ulong nodes = DatagenParameters.SoftNodeLimit;
            ulong depth = DatagenParameters.DepthLimit;
            ulong numGames = 1000000;
            ulong threads = 1;
            bool dfrc = false;

            args = args.Skip(1).ToArray();

            if (ulong.TryParse(args.Where(x => x.EndsWith('n')).FirstOrDefault()?[..^1], out ulong selNodeLimit)) nodes = selNodeLimit;
            if (ulong.TryParse(args.Where(x => x.EndsWith('d')).FirstOrDefault()?[..^1], out ulong selDepthLimit)) depth = selDepthLimit;
            if (ulong.TryParse(args.Where(x => x.EndsWith('g')).FirstOrDefault()?[..^1], out ulong selNumGames)) numGames = selNumGames;
            if (ulong.TryParse(args.Where(x => x.EndsWith('t')).FirstOrDefault()?[..^1], out ulong selThreads)) threads = selThreads;

            dfrc = args.Any(x => (x.EqualsIgnoreCase("frc") || x.EqualsIgnoreCase("dfrc")));

            Log($"Threads:      {threads}");
            Log($"Games/thread: {numGames:N0}");
            Log($"Total games:  {numGames * threads:N0}");
            Log($"Node limit:   {nodes:N0}");
            Log($"Depth limit:  {depth}");
            Log($"Variant:      {(dfrc ? "DFRC" : "Standard")}");
            Log($"Hit enter to begin...");
            _ = Console.ReadLine();

            ProgressBroker.StartMonitoring();
            if (threads == 1)
            {
                //  Let this run on the main thread to allow for debugging
                Selfplay.RunGames(numGames, 0, softNodeLimit: nodes, depthLimit: depth, dfrc: dfrc);
            }
            else
            {
                Parallel.For(0, (int)threads, new() { MaxDegreeOfParallelism = (int)threads }, (int i) =>
                {
                    Selfplay.RunGames(numGames, i, softNodeLimit: nodes, depthLimit: depth, dfrc: dfrc);
                });
            }
            ProgressBroker.StopMonitoring();

            Environment.Exit(0);
        }


        private static void HandleBenchCommand(string input)
        {
            if (input.ContainsIgnoreCase("perft"))
            {
                int depth;

                try
                {
                    depth = int.Parse(input.Substring(input.IndexOf("perft") + 6));
                }
                catch (Exception e)
                {
                    Log("Couldn't parse the perft depth from the input!");
                    Log(e.ToString());
                    return;
                }

                FishBench.Go(depth);
            }
            else
            {
                int depth = 12;

                try
                {
                    if (input.Length > 5 && int.TryParse(input.AsSpan(input.IndexOf("bench") + 6), out int newDepth))
                    {
                        depth = newDepth;
                    }
                }
                catch (Exception e)
                {
                    Log("Couldn't parse the bench depth from the input!");
                    Log(e.ToString());
                    return;
                }

                SearchBench.Go(depth);
            }
        }
    }

}
