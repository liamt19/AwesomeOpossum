using System.Collections.Concurrent;

namespace AwesomeOpossum.Logic.Datagen;

public static class ProgressBroker
{
    private static readonly ConcurrentDictionary<int, ulong> ThreadGameTotals = new();
    private static readonly ConcurrentDictionary<int, ulong> ThreadPositionTotals = new();
    private static readonly ConcurrentDictionary<int, ulong> ThreadNodeTotals = new();
    private static readonly ConcurrentDictionary<int, ulong> ThreadHashfullTotals = new();
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
        Console.WriteLine("   games     positions          nodes        nps       fill");
        (int _, int top) = Console.GetCursorPosition();
        Stopwatch sw = Stopwatch.StartNew();

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
            ulong totalFill = 0;

            foreach (var kvp in ThreadGameTotals)
            {
                int id = kvp.Key;
                totalGames += kvp.Value;
                totalPositions += ThreadPositionTotals[id];
                totalNodes += ThreadNodeTotals[id];
                totalFill += ThreadHashfullTotals[id];
            }

            totalFill /= (ulong)Math.Max(ThreadGameTotals.Count, 1);
            var nps = totalNodes / sw.Elapsed.TotalSeconds;
            Console.WriteLine($"{totalGames,8} {totalPositions,13:N0} {totalNodes,14:N0} {nps,10:N0} {totalFill,10:N0}");

            Thread.Sleep(250);
        }
    }

    public static void ReportProgress(int threadId, ulong gameNum, ulong totalPositions, ulong totalNodes, ulong totalFill)
    {
        ThreadGameTotals[threadId] = gameNum;
        ThreadPositionTotals[threadId] = totalPositions;
        ThreadNodeTotals[threadId] = totalNodes;
        ThreadHashfullTotals[threadId] = totalFill;
    }
}
