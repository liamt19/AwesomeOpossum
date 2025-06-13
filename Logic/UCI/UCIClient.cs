
#pragma warning disable CS0162 // Unreachable code detected

using System.Reflection;

using AwesomeOpossum.Logic.Evaluation;
using AwesomeOpossum.Logic.Threads;

namespace AwesomeOpossum.Logic.UCI
{
    public unsafe static class UCIClient
    {
        private static Position pos;
        private static SearchInformation info;
        private static ThreadSetup setup;

        private static Dictionary<string, UCIOption> Options;

        public static bool Active = false;

        static UCIClient()
        {
            ProcessUCIOptions();
            setup = new();
        }

        public static void Run(Position pos)
        {
            Active = true;

            UCIClient.pos = pos;

            info = new(pos)
            {
                OnIterationUpdate = PrintIterationInfo,
                OnSearchFinish = PrintFinalSearchInfo
            };

            Console.WriteLine($"id name AwesomeOpossum {EngineBuildVersion}");
            Console.WriteLine("id author Liam McGuire");

            PrintUCIOptions();
            Console.WriteLine("uciok");

            //  In case a "ucinewgame" isn't sent for the first game
            HandleNewGame(pos.Owner.AssocPool);
            InputLoop();
        }


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

        private static void InputLoop()
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

                    info = new(pos)
                    {
                        OnIterationUpdate = PrintIterationInfo,
                        OnSearchFinish = PrintFinalSearchInfo
                    };

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


        private static void HandleGo(string[] param)
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

            setup.UCISearchMoves = [];


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


        private static void HandleSetOption(string optName, string optValue)
        {
            optName = optName.Replace(" ", string.Empty);

            try
            {
                string key = Options.Keys.First(x => x.Replace(" ", string.Empty).EqualsIgnoreCase(optName));
                UCIOption opt = Options[key];
                object prevValue = opt.FieldHandle.GetValue(null);

                if (opt.IsBool && bool.TryParse(optValue, out bool newBool))
                {
                    opt.FieldHandle.SetValue(null, newBool);
                }
                else if (opt.IsInt && int.TryParse(optValue, out int newValue))
                {
                    if (newValue >= (int)opt.MinValue && newValue <= (int)opt.MaxValue)
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
                else if (opt.IsFloat && float.TryParse(optValue, out float newFloat))
                {
                    if (newFloat >= (float)opt.MinValue && newFloat <= (float)opt.MaxValue)
                    {
                        opt.FieldHandle.SetValue(null, newFloat);
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

                string fieldType = field.FieldType == typeof(bool)   ? "check"
                                 : field.FieldType == typeof(float)  ? "string"
                                 : field.FieldType == typeof(string) ? "string"
                                 :                                     "spin";

                var defaultValue = field.GetValue(null);
                if (field.FieldType == typeof(string))
                    defaultValue = defaultValue.ToString().ToLower();

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
                if (opt.HasBadRange())
                {
                    Log($"Option '{optName}' has an invalid range! -> [{opt.MinValue} <= {opt.DefaultValue} <= {opt.MaxValue}]!");
                }
            }
        }


        private static void PrintUCIOptions()
        {
            List<string> whitelist =
            [
                //nameof(SearchOptions.Threads),
                //nameof(SearchOptions.MultiPV),
                //nameof(SearchOptions.Hash),
                //nameof(SearchOptions.MoveOverhead),
                //nameof(SearchOptions.UCI_Chess960),
                //nameof(SearchOptions.UCI_ShowWDL),
                //nameof(SearchOptions.UCI_PrettyPrint),
            ];

            foreach (string k in Options.Keys.Where(x => whitelist.Contains(x) || whitelist.Count == 0))
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
                nameof(SearchOptions.MoveOverhead),
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
