using System.Runtime.InteropServices;
using System.Text;

namespace AwesomeOpossum.Logic.Search
{
    /// <summary>
    /// Used during a search to keep track of information from earlier plies/depths
    /// </summary>
    public unsafe struct SearchStackEntry
    {
        public static SearchStackEntry NullEntry = new SearchStackEntry();

        public Move* PV;
        public short DoubleExtensions;
        public short Ply;
        public short StaticEval;
        public Move KillerMove;
        public Move CurrentMove;
        public Move Skip;
        public bool InCheck;
        public bool TTPV;
        public bool TTHit;


        public SearchStackEntry()
        {
            Clear();
        }

        /// <summary>
        /// Zeroes the fields within this Entry.
        /// </summary>
        public void Clear()
        {
            CurrentMove = Move.Null;
            Skip = Move.Null;

            Ply = 0;
            DoubleExtensions = 0;
            StaticEval = ScoreNone;

            InCheck = false;
            TTPV = false;
            TTHit = false;

            if (PV != null)
            {
                NativeMemory.AlignedFree(PV);
                PV = null;
            }

            KillerMove = Move.Null;
        }

        public static string GetMovesPlayed(SearchStackEntry* curr)
        {
            StringBuilder sb = new StringBuilder();

            //  Not using a while loop here to prevent infinite loops or some other nonsense.
            for (int i = curr->Ply; i >= 0; i--)
            {
                sb.Insert(0, curr->CurrentMove.ToString() + " ");
                curr--;
            }

            return sb.ToString();
        }
    }
}
