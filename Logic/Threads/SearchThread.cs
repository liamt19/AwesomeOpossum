using System.Runtime.InteropServices;
using AwesomeOpossum.Logic.MCTS;
using AwesomeOpossum.Logic.NN;

namespace AwesomeOpossum.Logic.Threads
{

    public unsafe class SearchThread : IDisposable
    {
        public const int CheckupMax = 512;

        private bool _Disposed = false;

        public ulong Nodes;
        public ulong HardNodeLimit;

        public int ThreadIdx;

        public int PVIndex;

        public int RootDepth;
        public int SelDepth;
        public int CompletedDepth;

        public bool Searching;
        public bool Quit;
        public readonly bool IsMain = false;

        public readonly Position RootPosition;

        public List<RootMove> RootMoves = new List<RootMove>(20);
        public Move CurrentMove => RootMoves[PVIndex].Move;

        public BucketCache[] CachedBuckets;

        public SearchThreadPool AssocPool;
        public TranspositionTable TT;
        public Tree Tree;

        private Thread _SysThread;
        private readonly object _Mutex;
        private readonly ConditionVariable _SearchCond;
        private Barrier _InitBarrier = new Barrier(2);

        public string FriendlyName => _SysThread.Name;

        public SearchThread(int idx)
        {
            ThreadIdx = idx;
            if (ThreadIdx == 0)
            {
                IsMain = true;
            }

            _Mutex = "Mut" + ThreadIdx;
            _SearchCond = new ConditionVariable();
            Searching = true;

            //  Each thread its own position object, which lasts the entire lifetime of the thread.
            RootPosition = new Position(InitialFEN, true, this);

            _SysThread = new Thread(ThreadInit);

            //  Start the new thread, which will enter into ThreadInit --> IdleLoop
            _SysThread.Start();

            //  Wait here until the new thread signals that it is ready.
            _InitBarrier.SignalAndWait();

            WaitForThreadFinished();

            //  This isn't necessary but doesn't hurt either.
            _InitBarrier.RemoveParticipant();
        }


        /// <summary>
        /// Initializes this thread's Accumulators and history heuristic data.
        /// </summary>
        public void ThreadInit()
        {
            Quit = false;

            const int CacheSize = Bucketed768.INPUT_BUCKETS * 2;
            CachedBuckets = new BucketCache[CacheSize];
            for (int i = 0; i < CacheSize; i++)
            {
                CachedBuckets[i] = new BucketCache();
            }

            _SysThread.Name = "SearchThread " + ThreadIdx + ", ID " + Environment.CurrentManagedThreadId;
            if (IsMain)
            {
                _SysThread.Name = "(MAIN)Thread " + ThreadIdx + ", ID " + Environment.CurrentManagedThreadId;
            }

            IdleLoop();
        }



        /// <summary>
        /// Sets this thread's <see cref="Searching"/> variable to true, which will cause the thread in the IdleLoop to
        /// call the search function once it wakes up.
        /// </summary>
        public void PrepareToSearch()
        {
            Monitor.Enter(_Mutex);
            Searching = true;
            Monitor.Exit(_Mutex);

            _SearchCond.Pulse();
        }


        /// <summary>
        /// Blocks the calling thread until this SearchThread has exited its search call 
        /// and has returned to the beginning of its IdleLoop.
        /// </summary>
        public void WaitForThreadFinished()
        {
            if (_Mutex == null)
            {
                //  Asserting that _Mutex has been initialized properly
                throw new Exception("Thread " + Thread.CurrentThread.Name + " tried accessing the Mutex of " + this.ToString() + ", but Mutex was null!");
            }

            Monitor.Enter(_Mutex);

            while (Searching)
            {
                _SearchCond.Wait(_Mutex);

                if (Searching)
                {
                    ///  Spurious wakeups are possible here if <see cref="SearchThreadPool.StartSearch"/> is called
                    ///  again before this thread has returned to IdleLoop.
                    _SearchCond.Pulse();
                    Thread.Yield();
                }
            }

            Monitor.Exit(_Mutex);
        }


        /// <summary>
        /// The main loop that threads will be in while they are not currently searching.
        /// Threads enter here after they have been initialized and do not leave until their thread is terminated.
        /// </summary>
        public void IdleLoop()
        {
            //  Let the main thread know that this thread is initialized and ready to go.
            _InitBarrier.SignalAndWait();

            while (true)
            {
                Monitor.Enter(_Mutex);
                Searching = false;
                _SearchCond.Pulse();

                while (!Searching)
                {
                    //  Wait here until we are notified of a change in Searching's state.
                    _SearchCond.Wait(_Mutex);
                    if (!Searching)
                    {
                        //  This was a spurious wakeup since Searching's state has not changed.

                        //  Another thread was waiting on this signal but the OS gave it to this thread instead.
                        //  We can pulse the condition again, yield, and hope that the OS gives it to the thread that actually needs it
                        _SearchCond.Pulse();
                        Thread.Yield();
                    }

                }

                if (Quit)
                    return;

                Monitor.Exit(_Mutex);

                if (IsMain)
                {
                    MainThreadSearch();
                }
                else
                {
                    IDSearch();
                }
            }
        }



        /// <summary>
        /// Called by the MainThread after it is woken up by a call to <see cref="SearchThreadPool.StartSearch"/>.
        /// The MainThread will wake up the other threads and notify them to begin searching, and then start searching itself. 
        /// Once the MainThread finishes searching, it will wait until all other threads have finished as well, and will then 
        /// send the search results as output to the UCI.
        /// </summary>
        public void MainThreadSearch()
        {
            TT.TTUpdate();  //  Age the TT

            AssocPool.StartThreads();  //  Start other threads (if any)
            this.IDSearch();              //  Make this thread begin searching

            while (!AssocPool.StopThreads && AssocPool.SharedInfo.IsInfinite) { }

            //  When the main thread is done, prevent the other threads from searching any deeper
            AssocPool.StopThreads = true;

            //  Wait for the other threads to return
            AssocPool.WaitForSearchFinished();

            //  Search is finished, now give the UCI output.
            AssocPool.SharedInfo.OnSearchFinish?.Invoke(ref AssocPool.SharedInfo);
            AssocPool.SharedInfo.TimeManager.ResetTimer();

            AssocPool.SharedInfo.SearchActive = false;

            //  If the main program thread called BlockCallerUntilFinished,
            //  then the Blocker's ParticipantCount will be 2 instead of 1.
            if (AssocPool.Blocker.ParticipantCount == 2)
            {
                //  Signal that we are here, but only wait for 1 ms if the main thread isn't already waiting
                AssocPool.Blocker.SignalAndWait(1);
            }
        }

        /// <summary>
        /// Main deepening loop for threads. This is essentially the same as the old "StartSearching" method that was used.
        /// </summary>
        public void IDSearch()
        {
            SearchStackEntry* _SearchStackBlock = stackalloc SearchStackEntry[MaxPly];
            SearchStackEntry* ss = _SearchStackBlock + 10;
            for (int i = -10; i < MaxSearchStackPly; i++)
            {
                (ss + i)->Clear();
                (ss + i)->Ply = (short)i;
                (ss + i)->PV = AlignedAllocZeroed<Move>(MaxPly);
            }

            Bucketed768.ResetCaches(this);

            //  Create a copy of the AssocPool's root SearchInformation instance.
            SearchInformation info = AssocPool.SharedInfo;

            TimeManager tm = info.TimeManager;

            HardNodeLimit = info.NodeLimit;

            //  And set it's Position to this SearchThread's unique copy.
            //  (The Position that AssocPool.SharedInfo has right now has the same FEN, but its "Owner" field might not be correct.)
            info.Position = RootPosition;

            RootMove lastBestRootMove = new RootMove(Move.Null);


            while (++RootDepth < MaxPly)
            {
                //  The main thread is not allowed to search past info.DepthLimit
                if (IsMain && RootDepth > info.DepthLimit)
                    break;

                if (AssocPool.StopThreads)
                    break;

                uint usedDepth = (uint)RootDepth;
                float? scoreMaybe = Iteration.PerformOne(info.Position, 0, ref usedDepth);

                StableSort(RootMoves, Tree);

                if (IsMain)
                {
                    info.OnDepthFinish?.Invoke(ref info);
                }

                if (AssocPool.StopThreads)
                {
                    RootMoves[0] = lastBestRootMove;

                    for (int i = -10; i < MaxSearchStackPly; i++)
                        NativeMemory.AlignedFree((ss + i)->PV);

                    return;
                }

                lastBestRootMove.Move = RootMoves[0].Move;
                lastBestRootMove.Score = RootMoves[0].Score;
                lastBestRootMove.Depth = RootMoves[0].Depth;

                for (int i = 0; i < MaxPly; i++)
                {
                    lastBestRootMove.PV[i] = RootMoves[0].PV[i];
                    if (lastBestRootMove.PV[i] == Move.Null)
                    {
                        break;
                    }
                }

                if (Nodes >= info.SoftNodeLimit)
                    break;

                if (!AssocPool.StopThreads)
                    CompletedDepth = RootDepth;
            }

            if (IsMain && RootDepth >= MaxDepth && info.HasNodeLimit && !AssocPool.StopThreads)
            {
                //  If this was a "go nodes x" command, it is possible for the main thread to hit the
                //  maximum depth before hitting the requested node count (causing an infinite wait).

                //  If this is the case, and we haven't been told to stop searching, then we need to stop now.
                AssocPool.StopThreads = true;
            }

            for (int i = -10; i < MaxSearchStackPly; i++)
            {
                NativeMemory.AlignedFree((ss + i)->PV);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                Assert(Searching == false, "The thread {ToString()} had its Dispose({disposing}) method called Searching was {Searching}!");

                //  Set quit to True, and pulse the condition to allow the thread in IdleLoop to exit.
                Quit = true;

                PrepareToSearch();
            }

            //  Destroy the underlying system thread
            _SysThread.Join();

            _Disposed = true;
        }

        /// <summary>
        /// Calls the class destructor, which will free up the memory that was allocated to this SearchThread.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);

            //  We handled the finalization ourselves, so tell the GC not to worry about it.
            GC.SuppressFinalize(this);
        }

        ~SearchThread()
        {
            Dispose(false);
        }


        public override string ToString()
        {
            return "[" + (_SysThread != null ? _SysThread.Name : "NULL?") + " (caller ID " + Environment.CurrentManagedThreadId + ")]";
        }
    }
}
