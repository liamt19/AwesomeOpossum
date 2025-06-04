
#pragma warning disable CS0162 // Unreachable code detected

using System.Reflection;

using AwesomeOpossum.Logic.Evaluation;
using AwesomeOpossum.Logic.Threads;

namespace AwesomeOpossum.Logic.UCI
{
    public unsafe class UCIClient
    {
        private Position pos;
        private SearchInformation info;
        private ThreadSetup setup;

        private static Dictionary<string, UCIOption> Options;

        public static bool Active = false;

        public UCIClient(Position pos)
        {
            ProcessUCIOptions();

            this.pos = pos;

            info = new SearchInformation(pos);
            info.OnIterationUpdate = Utilities.PrintIterationInfo;
            info.OnSearchFinish = PrintFinalSearchInfo;

            setup = new ThreadSetup();
        }

        /// <summary>
        /// Blocks until a command is sent in the standard input stream
        /// </summary>
        /// <param name="cmd">Set to the command, which is the first word in the input</param>
        /// <returns>The remaining words in the input, which are parameters for the command</returns>
        private static string[] ReceiveString(out string cmd)
        {
            string input = Console.ReadLine();
            if (input == null || input.Length == 0)
            {
                cmd = ":(";
                return Array.Empty<string>();
            }

            string[] splits = input.Split(" ");
            cmd = splits[0].ToLower();
            string[] param = splits.ToList().GetRange(1, splits.Length - 1).ToArray();

            return param;
        }

        /// <summary>
        /// Sends the UCI options, and begins waiting for input.
        /// </summary>
        public void Run()
        {
            Active = true;

#if DEV
            Console.WriteLine($"id name AwesomeOpossum {EngineBuildVersion} DEV");
#else
            Console.WriteLine($"id name AwesomeOpossum {EngineBuildVersion}");
#endif
            Console.WriteLine("id author Liam McGuire");
            Console.WriteLine("info string Using Bucketed768 evaluation.");

            PrintUCIOptions();
            Console.WriteLine("uciok");

            //  In case a "ucinewgame" isn't sent for the first game
            HandleNewGame(pos.Owner.AssocPool);
            InputLoop();
        }

        /// <summary>
        /// Handles commands sent by UCI's.
        /// </summary>
        private void InputLoop()
        {
            while (true)
            {
                string[] param = ReceiveString(out string cmd);

                if (cmd == "quit")
                {
                    Environment.Exit(0);
                }
                else if (cmd == "isready")
                {
                    Console.WriteLine("readyok");
                }
                else if (cmd == "ucinewgame")
                {
                    HandleNewGame(GlobalSearchPool);
                }
                else if (cmd == "position")
                {
                    pos.IsChess960 = UCI_Chess960;

                    info = new SearchInformation(pos);
                    info.OnIterationUpdate = PrintIterationInfo;
                    info.OnSearchFinish = PrintFinalSearchInfo;

                    ParsePositionCommand(param, pos, setup);
                    ValueNetwork.RefreshAccumulator(info.Position);
                }
                else if (cmd == "go")
                {
                    GlobalSearchPool.StartAllThreads();
                    HandleGo(param);
                }
                else if (cmd == "stop")
                {
                    GlobalSearchPool.StopAllThreads();
                }
                else if (cmd == "leave")
                {
                    Active = false;
                    return;
                }
                else if (cmd == "setoption")
                {
                    try
                    {
                        //  param[0] == "name"
                        string optName = param[1];
                        string optValue = "";

                        for (int i = 2; i < param.Length; i++)
                        {
                            if (param[i] == "value")
                            {
                                for (int j = i + 1; j < param.Length; j++)
                                {
                                    optValue += param[j] + " ";
                                }
                                optValue = optValue.Trim();
                                break;
                            }
                            else
                            {
                                optName += " " + param[i];
                            }
                        }

                        //  param[2] == "value"
                        HandleSetOption(optName, optValue);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"[ERROR]: Failed parsing setoption command, got '{param}' -> {e}");
                    }
                }
                else if (cmd == "tune")
                {
                    PrintSPSAParams();
                }
                else if (cmd == "eval")
                {
                    Console.WriteLine($"{ValueNetwork.Evaluate(pos)}");
                }
            }
        }




        //  https://gist.github.com/DOBRO/2592c6dad754ba67e6dcaec8c90165bf

        /// <summary>
        /// Process "go" command parameters and begin a search.
        /// 
        /// <para> Currently handled: </para>
        /// <br> movetime -> search for milliseconds </br>
        /// <br> depth -> search until a specific depth (in plies) </br>
        /// <br> nodes -> only look at a maximum number of nodes </br>
        /// <br> infinite -> keep looking until we get a "stop" command </br>
        /// <br> (w/b)time -> the specified player has x amount of milliseconds left</br>
        /// <br> (w/b)inc -> the specified player gains x milliseconds after they move</br>
        /// 
        /// <para> Currently ignored: </para>
        /// <br> ponder, movestogo, mate </br>
        /// 
        /// </summary>
        /// <param name="param">List of parameters sent with the "go" command.</param>
        private void HandleGo(string[] param)
        {
            if (info.SearchActive)
                return;

            ParseGo(param, ref info, setup);
            GlobalSearchPool.StartSearch(info.Position, ref info, setup);
        }


        public static void ParseGo(string[] param, ref SearchInformation info, ThreadSetup setup)
        {
            var stmChar = (info.Position.ToMove == White) ? 'w' : 'b';

            TimeManager.Reset();

            setup.UCISearchMoves = new List<Move>();


            //  Assume that we can search infinitely, and let the parameters constrain us accordingly.
            int movetime = MaxSearchTime;
            ulong nodeLimit = MaxSearchNodes;
            int depthLimit = MaxDepth;
            int playerTime = 0;
            int increment = 0;

            for (int i = 0; i < param.Length - 1; i++)
            {
                if (param[i] == "movetime" && int.TryParse(param[i + 1], out int reqMovetime))
                {
                    movetime = reqMovetime;
                }
                else if (param[i] == "depth" && int.TryParse(param[i + 1], out int reqDepth))
                {
                    depthLimit = reqDepth;
                }
                else if (param[i] == "nodes" && ulong.TryParse(param[i + 1], out ulong reqNodes))
                {
                    nodeLimit = reqNodes;
                }
                else if (param[i].StartsWith(stmChar) && param[i].EndsWith("time") && int.TryParse(param[i + 1], out int reqPlayerTime))
                {
                    playerTime = reqPlayerTime;
                }
                else if (param[i].StartsWith(stmChar) && param[i].EndsWith("inc") && int.TryParse(param[i + 1], out int reqPlayerIncrement))
                {
                    increment = reqPlayerIncrement;
                }
                else if (param[i] == "searchmoves")
                {
                    i++;

                    while (i <= param.Length - 1)
                    {
                        if (info.Position.TryFindMove(param[i], out Move m))
                        {
                            setup.UCISearchMoves.Add(m);
                        }

                        i++;
                    }
                }
            }

            info.DepthLimit = depthLimit;
            info.HardNodeLimit = nodeLimit;

            bool useSoftTM = param.Any(x => x.EndsWith("time") && x.StartsWith(stmChar)) && !param.Any(x => x == "movetime");
            if (useSoftTM)
            {
                TimeManager.UpdateTimeLimits(playerTime, increment);
            }
            else
            {
                TimeManager.SetHardLimit(movetime);
            }
        }


        private static void HandleNewGame(SearchThreadPool pool)
        {
            pool.MainThread.WaitForThreadFinished();
            pool.Clear();
        }

        private void HandleSetOption(string optName, string optValue)
        {
            optName = optName.Replace(" ", string.Empty);

            try
            {
                string key = Options.Keys.First(x => x.Replace(" ", string.Empty).EqualsIgnoreCase(optName));
                UCIOption opt = Options[key];
                object prevValue = opt.FieldHandle.GetValue(null);

                if (opt.FieldHandle.FieldType == typeof(bool) && bool.TryParse(optValue, out bool newBool))
                {
                    opt.FieldHandle.SetValue(null, newBool);
                }
                else if (opt.FieldHandle.FieldType == typeof(int) && int.TryParse(optValue, out int newValue))
                {
                    if (newValue >= opt.MinValue && newValue <= opt.MaxValue)
                    {
                        opt.FieldHandle.SetValue(null, newValue);

                        if (opt.Name == nameof(Threads))
                        {
                            GlobalSearchPool.Resize(SearchOptions.Threads);
                        }

                        if (opt.Name == nameof(Hash))
                        {
                            GlobalSearchPool.ResizeHashes();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR]: Failed handling setoption command for '{optName}' -> {optValue}! {e}");
            }

        }

        private static void ProcessUCIOptions()
        {
            Options = new();

            //  Get all "public static" fields, and specifically exclude constant fields (which have field.IsLiteral == true)
            List<FieldInfo> fields = typeof(SearchOptions).GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => !x.IsLiteral).ToList();

            foreach (FieldInfo field in fields)
            {
                string fieldName = field.Name;

                //  Most options are numbers and are called "spin"
                //  If they are true/false, they are called "check"
                string fieldType = field.FieldType == typeof(bool)   ? "check" 
                                 : field.FieldType == typeof(string) ? "string"
                                 :                                     "spin";
                string defaultValue = field.GetValue(null).ToString().ToLower();

                UCIOption opt = new(fieldName, fieldType, defaultValue, field);

                Options.Add(fieldName, opt);
            }

            Options[nameof(Threads)].SetMinMax(1, 512);
            Options[nameof(MultiPV)].SetMinMax(1, 256);
            Options[nameof(Hash)].SetMinMax(1, 1048576);
            Options[nameof(MoveOverhead)].SetMinMax(0, 2000);

            foreach (var optName in Options.Keys)
            {
                var opt = Options[optName];
                if (opt.FieldHandle.FieldType != typeof(int))
                    continue;

                //  Ensure values are within [Min, Max] and Max > Min
                int currValue = int.Parse(opt.DefaultValue);
                if (currValue < opt.MinValue || currValue > opt.MaxValue || opt.MaxValue < opt.MinValue)
                {
                    Log($"Option '{optName}' has an invalid range! -> [{opt.MinValue} <= {opt.DefaultValue} <= {opt.MaxValue}]!");
                }
            }
        }


        private static void PrintUCIOptions()
        {
            List<string> whitelist =
            [
                nameof(SearchOptions.Threads),
                nameof(SearchOptions.MultiPV),
                nameof(SearchOptions.Hash),
                nameof(SearchOptions.UCI_Chess960),
                nameof(SearchOptions.UCI_ShowWDL),
                nameof(SearchOptions.UCI_PrettyPrint),
            ];

            foreach (string k in Options.Keys.Where(x => whitelist.Contains(x)))
            {
                Console.WriteLine(Options[k].ToString());
            }
        }

        private static void PrintSPSAParams()
        {
            List<string> ignore =
            [
                nameof(SearchOptions.Threads),
                nameof(SearchOptions.MultiPV),
                nameof(SearchOptions.Hash),
                nameof(SearchOptions.UCI_Chess960),
                nameof(SearchOptions.UCI_ShowWDL),
                nameof(SearchOptions.UCI_PrettyPrint),
            ];

            foreach (var optName in Options.Keys)
            {
                if (ignore.Contains(optName))
                {
                    continue;
                }

                var opt = Options[optName];
                Console.WriteLine(opt.GetSPSAFormat());
            }
        }

    }
}
