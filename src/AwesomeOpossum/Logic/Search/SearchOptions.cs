namespace AwesomeOpossum.Logic.Search
{
    public static class SearchOptions
    {
        public static int Threads = 1;
        public static int MultiPV = 1;
        public static int Hash = 32;
        public static int MoveOverhead = 25;

        public static bool UCI_Chess960 = false;
        public static bool UCI_ShowWDL = false;
        public static bool UCI_PrettyPrint = true;
        public static bool Minimal = false;


        public static float ExplTau = 0.6493f;
        public static float GiniBase = 0.6700f;
        public static float GiniScale = 1.5000f;
        public static float GiniMin = 2.1500f;
        public static float CPuctVisitScale = 3999.7565f;
        public static float CPuctBaseRoot = 0.5141f;
        public static float CPuctBase = 0.2793f;
        public static float PSTQInc = 0.7948f;
        public static float PSTQScale = 0.2565f;
        public static float PSTNumer = 2.2689f;
        public static float PSTPow = 1.4886f;
        public static float PSTOffset = 0.1502f;
        public static float PSTSinDiv = 25.5294f;
    }
}
