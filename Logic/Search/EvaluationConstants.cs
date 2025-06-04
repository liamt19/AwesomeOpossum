using System.Runtime.CompilerServices;

namespace AwesomeOpossum.Logic.Search
{
    public static unsafe class EvaluationConstants
    {
        public const short ScoreNone = 32760;
        public const int ScoreInfinite = 31200;
        public const int ScoreMate = 30000;
        public const int ScoreDraw = 0;

        public const int ScoreTTWin = ScoreMate - 512;
        public const int ScoreTTLoss = -ScoreTTWin;

        public const int ScoreMateMax = ScoreMate - 256;
        public const int ScoreMatedMax = -ScoreMateMax;

        public const int MaxNormalScore = ScoreTTWin - 1;

        public const int ScoreAssuredWin = 20000;
        public const int ScoreWin = 10000;

        public const float ScorePVWin = 1.1f;
        public const float ScorePVLoss = -0.1f;

        [MethodImpl(Inline)]
        public static int MakeDrawScore(ulong nodes)
        {
            return -1 + (int)(nodes & 2);
        }

        [MethodImpl(Inline)]
        public static int MakeMateScore(int ply)
        {
            return -ScoreMate + ply;
        }

        public static bool IsScoreMate(int score)
        {
            return Math.Abs(Math.Abs(score) - ScoreMate) < MaxDepth;
        }

        [MethodImpl(Inline)]
        public static bool IsWin(int score) => score >= ScoreTTWin;

        [MethodImpl(Inline)]
        public static bool IsLoss(int score) => score <= ScoreTTLoss;

        [MethodImpl(Inline)]
        public static bool IsDecisive(int score) => IsWin(score) || IsLoss(score);

        [MethodImpl(Inline)]
        public static int GetSEEValue(int pt)
        {
            return pt switch
            {
                Pawn   => SEEValuePawn,
                Knight => SEEValueKnight,
                Bishop => SEEValueBishop,
                Rook   => SEEValueRook,
                Queen  => SEEValueQueen,
                _      => 0,
            };
        }
    }
}
