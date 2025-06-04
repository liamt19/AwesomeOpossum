
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace AwesomeOpossum.Logic.Search
{
    public struct SearchInformation
    {
        public delegate void ActionRef<T>(ref T info);
        public ActionRef<SearchInformation>? OnIterationUpdate;
        public ActionRef<SearchInformation>? OnSearchFinish;

        public Position Position;

        public int DepthLimit = Utilities.MaxDepth;
        public ulong HardNodeLimit = MaxSearchNodes;
        public ulong SoftNodeLimit = MaxSearchNodes;

        public bool SearchActive = false;

        public SearchInformation(Position p, int depth = Utilities.MaxDepth, int searchTime = MaxSearchTime)
        {
            this.Position = p;
            this.DepthLimit = depth;

            this.OnIterationUpdate = Utilities.PrintIterationInfo;
            this.OnSearchFinish = Utilities.PrintFinalSearchInfo;
        }

        public bool HasDepthLimit => (DepthLimit != Utilities.MaxDepth);
        public bool HasNodeLimit => (HardNodeLimit != MaxSearchNodes);
        public bool HasTimeLimit => (TimeManager.HardTimeLimit != MaxSearchTime);
        public bool IsInfinite => !HasDepthLimit && !HasTimeLimit;

        public static SearchInformation DatagenPrelim(Position pos, ulong nodeLimit, int depthLimit)
        {
            return new SearchInformation(pos)
            {
                SoftNodeLimit = nodeLimit,
                HardNodeLimit = nodeLimit,
                DepthLimit = Math.Max(12, depthLimit),
                OnIterationUpdate = null,
                OnSearchFinish = null,
            };
        }

        public static SearchInformation DatagenStandard(Position pos, ulong nodeLimit, int depthLimit)
        {
            return new SearchInformation(pos)
            {
                SoftNodeLimit = nodeLimit,
                HardNodeLimit = nodeLimit,
                DepthLimit = depthLimit,
                OnIterationUpdate = null,
                OnSearchFinish = null,
            };
        }
    }
}
