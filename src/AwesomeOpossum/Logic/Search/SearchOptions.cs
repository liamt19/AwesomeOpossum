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


        public static float ExplTau = 0.659278f;
        public static float GiniBase = 0.663742f;
        public static float GiniScale = 1.544302f;
        public static float GiniMin = 2.136079f;
        public static float CPuctVisitScale = 4077.954306f;
        public static float CPuctBaseRoot = 0.520352f;
        public static float CPuctBase = 0.261141f;
        public static float FPUBase = 1.019626f;
        public static float PSTQInc = 0.801457f;
        public static float PSTQScale = 0.252099f;
        public static float PSTNumer = 2.371023f;
        public static float PSTPow = 1.342446f;
        public static float PSTOffset = 0.140031f;
        public static float PSTSinDiv = 26.110095f;

        public static float StabilityBase = 0.976387f;
        public static float StabilityMul = 0.143435f;

        public static float EffortOffset = 2.500000f;
        public static float EffortMul = 0.500000f;
        public static float EffortBase = 1.000000f;

        public static float EvalDeltaMul = 0.065000f;
        public static float EvalDeltaMin = 0.750000f;
        public static float EvalDeltaMax = 1.500000f;

    }
}
