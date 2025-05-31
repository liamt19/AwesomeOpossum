using System.Collections.Concurrent;

namespace AwesomeOpossum.Logic.Datagen
{
    public static class ProgressBroker
    {
        private static readonly ConcurrentDictionary<int, ulong> ThreadGameTotals = new();
        private static readonly ConcurrentDictionary<int, ulong> ThreadPositionTotals = new();
        private static readonly ConcurrentDictionary<int, ulong> ThreadNodeTotals = new();
        private static readonly CancellationTokenSource TokenSource = new();

        public static void StartMonitoring()
        {
            Task.Run(() => MonitorProgress(TokenSource.Token));
        }

        public static void StopMonitoring()
        {
            TokenSource.Cancel();
        }

        private static void MonitorProgress(CancellationToken token)
        {
            Console.WriteLine("\n");
            Console.WriteLine("   games     positions          nodes");
            (int _, int top) = Console.GetCursorPosition();

            while (!token.IsCancellationRequested)
            {
                Console.SetCursorPosition(0, top);
                Console.CursorVisible = false;
                for (int y = 0; y < Console.WindowHeight - top; y++)
                    Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, top);

                ulong totalGames = 0;
                ulong totalPositions = 0;
                ulong totalNodes = 0;

                foreach (var kvp in ThreadGameTotals)
                {
                    int id = kvp.Key;
                    totalGames += kvp.Value;
                    totalPositions += ThreadPositionTotals[id];
                    totalNodes += ThreadNodeTotals[id];
                }
                Console.WriteLine($"{totalGames,8} {totalPositions,13:N0} {totalNodes,14}");

                Thread.Sleep(250);
            }
        }

        public static void ReportProgress(int threadId, ulong gameNum, ulong totalPositions, ulong totalNodes)
        {
            ThreadGameTotals[threadId] = gameNum;
            ThreadPositionTotals[threadId] = totalPositions;
            ThreadNodeTotals[threadId] = totalNodes;
        }
    }
}
