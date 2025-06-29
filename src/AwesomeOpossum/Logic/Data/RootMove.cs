using System.Runtime.CompilerServices;

namespace AwesomeOpossum.Logic.Data
{
    public class RootMove : IComparable<RootMove>
    {
        public Move Move;

        public float Score;
        public float PreviousScore;
        public float AverageScore;
        public int Depth;

        public Move[] PV;
        public int PVLength;

        public RootMove(Move move, float score = -ScoreInfinite)
        {
            Move = move;
            Score = score;
            PreviousScore = score;
            AverageScore = score;
            Depth = 0;

            PV = new Move[MaxPly];
            PV[0] = move;
            PVLength = 1;
        }

        [MethodImpl(Inline)]
        public int CompareTo(RootMove other)
        {
            if (Score != other.Score)
            {
                return Score.CompareTo(other.Score);
            }
            else
            {
                return PreviousScore.CompareTo(other.PreviousScore);
            }
        }

        public override string ToString()
        {
            return Move.ToString() + ": " + Score + ", Avg: " + AverageScore;
        }
    }
}
