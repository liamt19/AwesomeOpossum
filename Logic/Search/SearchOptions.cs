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

        public static float ExplTau = 0.5f;
        public static float CPuctVisitScale = 4000.0f;
        public static float CPuctBaseRoot = 0.5f;
        public static float CPuctBase = 0.25f;
        public static float PSTQInc = 0.75f;
        public static float PSTQScale = 0.25f;
        public static float PSTNumer = 2.15f;
        public static float PSTPow = 1.60f;
        public static float PSTOffset = 0.15f;
        public static float PSTSinDiv = 25.0f;
    }
}
