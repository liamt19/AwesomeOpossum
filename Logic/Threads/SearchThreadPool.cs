using AwesomeOpossum.Logic.MCTS;

namespace AwesomeOpossum.Logic.Threads
{
    /// <summary>
    /// Keeps track of a number of SearchThreads and provides methods to start and wait for them to finish.
    /// 
    /// <para></para>
    /// Some of the thread logic in this class is based on Stockfish's Thread class
    /// (StartThreads, WaitForSearchFinished, and the general concepts in StartSearch), the sources of which are here:
    /// <br></br>
    /// https://github.com/official-stockfish/Stockfish/blob/master/src/thread.cpp
    /// <br></br>
    /// https://github.com/official-stockfish/Stockfish/blob/master/src/thread.h
    /// 
    /// </summary>
    public unsafe class SearchThreadPool
    {
        /// <summary>
        /// Global ThreadPool.
        /// </summary>
        public static SearchThreadPool GlobalSearchPool;

        public int ThreadCount = SearchOptions.Threads;

        public SearchInformation SharedInfo;
        public SearchThread[] Threads;
        public SearchThread MainThread => Threads[0];

        public Tree SharedTree;

        public Barrier Blocker;

        static SearchThreadPool()
        {
            GlobalSearchPool = new SearchThreadPool(SearchOptions.Threads);
        }

        public SearchThreadPool(int threadCount)
        {
            Blocker = new Barrier(1);
            SharedTree = new Tree(Hash);
            Resize(threadCount);
        }

        /// <summary>
        /// Joins any existing threads and spawns <paramref name="newThreadCount"/> new ones.
        /// </summary>
        public void Resize(int newThreadCount)
        {
            if (Threads != null)
            {
                MainThread.WaitForThreadFinished();

                for (int i = 0; i < ThreadCount; i++)
                {
                    Threads[i]?.Dispose();
                }
            }

            this.ThreadCount = newThreadCount;
            Threads = new SearchThread[ThreadCount];


            for (int i = 0; i < ThreadCount; i++)
            {
                Threads[i] = new(i)
                {
                    AssocPool = this,
                    Tree = SharedTree
                };
            }

            MainThread.WaitForThreadFinished();
        }

        /// <summary>
        /// Prepares each thread in the SearchThreadPool for a new search, and wakes the MainSearchThread up.
        /// <br></br>
        /// This is called by the main program thread (or UCI) after the search parameters have been set in <paramref name="rootInfo"/>.
        /// <para></para>
        /// <paramref name="rootPosition"/> should be set to the position to create the <see cref="SearchThread.RootMoves"/> from, 
        /// which should be the same as <paramref name="rootInfo"/>'s Position.
        /// </summary>
        public void StartSearch(Position rootPosition, ref SearchInformation rootInfo)
        {
            StartSearch(rootPosition, ref rootInfo, new ThreadSetup(rootPosition.GetFEN()));
        }


        /// <summary>
        /// <inheritdoc cref="StartSearch(Position, ref SearchInformation)"/>
        /// <para></para>
        /// Thread positions are first set to the FEN specified by <paramref name="setup"/>, 
        /// and each move in <see cref="ThreadSetup.SetupMoves"/> (if any) is made for each position.
        /// </summary>
        public void StartSearch(Position rootPosition, ref SearchInformation rootInfo, ThreadSetup setup)
        {
            MainThread.WaitForThreadFinished();

            StartAllThreads();
            SharedInfo = rootInfo;          //  Initialize the shared SearchInformation
            SharedInfo.SearchActive = true; //  And mark this search as having started

            ScoredMove* moves = stackalloc ScoredMove[MoveListSize];
            int size = rootPosition.GenLegal(moves);

            var rootFEN = setup.StartFEN;
            if (rootFEN == InitialFEN && setup.SetupMoves.Count == 0)
            {
                rootFEN = rootPosition.GetFEN();
            }

            for (int i = 0; i < ThreadCount; i++)
            {
                var td = Threads[i];

                td.Reset();
                td.RootPosition.LoadFromFEN(rootFEN);

                foreach (var move in setup.SetupMoves)
                {
                    td.RootPosition.MakeMove(move);
                }
            }

            TimeManager.StartTimer();
            MainThread.WakeUp();
        }




        public SearchThread GetBestThread()
        {
            //  TODO
            return MainThread;
        }


        /// <summary>
        /// Unblocks each thread waiting in IdleLoop by setting their <see cref="SearchThread.Searching"/> variable to true
        /// and signaling the condition variable.
        /// </summary>
        public void AwakenHelperThreads()
        {
            //  Skip Threads[0] because it will do this to itself after this method returns.
            for (int i = 1; i < ThreadCount; i++)
            {
                Threads[i].WakeUp();
            }
        }

        public void StopAllThreads()
        {
            for (int i = 1; i < ThreadCount; i++)
                Threads[i].SetStop(true);

            MainThread.SetStop(true);
        }

        public void StartAllThreads()
        {
            for (int i = 1; i < ThreadCount; i++)
                Threads[i].SetStop(false);

            MainThread.SetStop(false);
        }


        public void WaitForSearchFinished()
        {
            //  Skip Threads[0] (the MainThread) since this method is only ever called when the MainThread is done.
            for (int i = 1; i < ThreadCount; i++)
            {
                Threads[i].WaitForThreadFinished();
            }
        }


        /// <summary>
        /// Blocks the calling thread until the MainSearchThread has finished searching.
        /// <br></br>
        /// This should only be used after calling <see cref="StartSearch"/>, and only blocks if a search is currently active.
        /// </summary>
        public void BlockCallerUntilFinished()
        {
            //  This can happen if any thread other than the main thread calls this method.
            Assert(Blocker.ParticipantCount == 1,
                $"BlockCallerUntilFinished was called, but the barrier had {Blocker.ParticipantCount} participants (should have been 1)!");

            if (!SharedInfo.SearchActive)
            {
                //  Don't block if we aren't searching.
                return;
            }

            if (Blocker.ParticipantCount != 1)
            {
                //  This should never happen, but just in case we can signal once to try and unblock Blocker.
                Blocker.SignalAndWait(1);
                return;
            }

            //  The MainSearchThread is always a participant, and the calling thread is a temporary participant.
            //  The MainSearchThread will only signal if there are 2 participants, so add the calling thread.
            Blocker.AddParticipant();

            //  The MainSearchThread will signal Blocker once it has finished, so wait here until it does so.
            Blocker.SignalAndWait();

            //  We are done waiting, so remove the calling thread as a participant (now Blocker.ParticipantCount == 1)
            Blocker.RemoveParticipant();

        }


        public void Clear()
        {

        }

        public void ResizeHashes()
        {
            for (int i = 0; i < ThreadCount; i++)
                Threads[i].Tree.Resize(Hash);
        }


        /// <summary>
        /// Returns the total amount of nodes searched by all SearchThreads in the pool.
        /// </summary>
        /// <returns></returns>
        public ulong GetNodeCount()
        {
            ulong total = 0;
            for (int i = 0; i < ThreadCount; i++)
            {
                total += Threads[i].Nodes;
            }
            return total;
        }

    }
}
