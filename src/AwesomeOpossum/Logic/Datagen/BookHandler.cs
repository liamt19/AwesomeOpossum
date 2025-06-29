using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.Datagen
{
    public static class BookHandler
    {
        private static List<string> BookLines;

        private static List<ConcurrentQueue<string>> Queues;
        private static int NumQueues;

        private static Thread RefillThread;

        public static void Initialize(string filePath, int numThreads = 1)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("where da book at", filePath);

            BookLines = File.ReadAllLines(filePath).ToList();
            NumQueues = numThreads;
            Queues = new List<ConcurrentQueue<string>>(NumQueues);
            for (int i = 0; i < NumQueues; i++)
                Queues.Add(new ConcurrentQueue<string>());

            RefillThread = new Thread(RefillLoop) { IsBackground = true };
            RefillThread.Start();
        }

        private static void RefillLoop()
        {
            int nextLineIndex = 0;
            while (true)
            {
                for (int queueIndex = 0; queueIndex < NumQueues; queueIndex++)
                {
                    var queue = Queues[queueIndex];
                    while (queue.Count < 50)
                    {
                        string line = BookLines[nextLineIndex % BookLines.Count];
                        queue.Enqueue(line);
                        nextLineIndex++;
                    }
                }

                nextLineIndex %= BookLines.Count;
                Thread.Sleep(100);
            }
        }

        public static string GetStartpos(int i)
        {
            string line;

            while (!Queues[i].TryDequeue(out line))
                Thread.Sleep(10);

            return line;
        }
    }
}
