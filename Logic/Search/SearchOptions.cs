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


        public static int SEEValuePawn = 105;
        public static int SEEValueKnight = 300;
        public static int SEEValueBishop = 315;
        public static int SEEValueRook = 535;
        public static int SEEValueQueen = 970;

        public static float TemperatureQInc = 0.75f;
        public static float TemperatureScale = 0.25f;
    }
}
