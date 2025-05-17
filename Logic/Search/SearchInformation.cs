
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace AwesomeOpossum.Logic.Search
{
    public struct SearchInformation
    {
        /// <summary>
        /// The normal <see cref="Action"/> delegate doesn't allow passing by reference
        /// </summary>
        public delegate void ActionRef<T>(ref T info);

        /// <summary>
        /// The method to call (which must accept a <see langword="ref"/> <see cref="SearchInformation"/> parameter)
        /// during a search after a certain number of iterations has completed.
        /// <br></br>
        /// By default, this will print out the "info depth # ..." string.
        /// </summary>
        public ActionRef<SearchInformation>? OnIterationUpdate;

        /// <summary>
        /// The method to call (which must accept a <see langword="ref"/> <see cref="SearchInformation"/> parameter)
        /// when a search is finished.
        /// <br></br>
        /// By default, this will print "bestmove (move)".
        /// </summary>
        public ActionRef<SearchInformation>? OnSearchFinish;

        public Position Position;

        public int DepthLimit = Utilities.MaxDepth;
        public ulong NodeLimit = MaxSearchNodes;
        public ulong SoftNodeLimit = MaxSearchNodes;

        /// <summary>
        /// Set to true while a search is ongoing, and false otherwise.
        /// </summary>
        public bool SearchActive = false;

        public TimeManager TimeManager;

        public bool HasDepthLimit => (DepthLimit != Utilities.MaxDepth);
        public bool HasNodeLimit => (NodeLimit != MaxSearchNodes);
        public bool HasTimeLimit => (this.TimeManager.MaxSearchTime != MaxSearchTime);

        public bool IsInfinite => !HasDepthLimit && !HasTimeLimit;

        public SearchInformation(Position p, int depth = Utilities.MaxDepth, int searchTime = MaxSearchTime)
        {
            this.Position = p;
            this.DepthLimit = depth;

            this.TimeManager = new TimeManager();
            this.TimeManager.MaxSearchTime = searchTime;

            this.OnIterationUpdate = Utilities.PrintIterationInfo;
            this.OnSearchFinish = Utilities.PrintFinalSearchInfo;
        }

        public void SetMoveTime(int moveTime)
        {
            TimeManager.MaxSearchTime = moveTime;
            TimeManager.HasMoveTime = true;
        }

        public override string ToString()
        {
            return $"DepthLimit: {DepthLimit}, NodeLimit: {NodeLimit}, MaxSearchTime: {MaxSearchTime}, SearchTime: "
                 + (TimeManager == null ? "0 (NULL!)" : TimeManager.GetSearchTime());
        }
    }
}
