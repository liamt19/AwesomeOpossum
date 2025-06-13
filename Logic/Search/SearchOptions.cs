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


        //public static int SEEValuePawn = 105;
        //public static int SEEValueKnight = 300;
        //public static int SEEValueBishop = 315;
        //public static int SEEValueRook = 535;
        //public static int SEEValueQueen = 970;

        public static float ExplTau = 0.6442f;
        public static float CPuctVisitScale = 4011.9372f;
        public static float CPuctBaseRoot = 0.5173f;
        public static float CPuctBase = 0.2805f;
        public static float PSTQInc = 0.7906f;
        public static float PSTQScale = 0.2559f;
        public static float PSTNumer = 2.2811f;
        public static float PSTPow = 1.4742f;
        public static float PSTOffset = 0.1489f;
        public static float PSTSinDiv = 25.4630f;
    }
}
