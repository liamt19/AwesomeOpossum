
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using AwesomeOpossum.Logic.Threads;

using static AwesomeOpossum.Logic.Evaluation.Aliases;
using static AwesomeOpossum.Logic.Evaluation.FunUnrollThings;

namespace AwesomeOpossum.Logic.Evaluation
{
    public static unsafe partial class ValueNetwork
    {
        public static string NetworkName
        {
            get
            {
                try
                {
                    return Assembly.GetEntryAssembly().GetCustomAttribute<ValueFileAttribute>().ValueFile.Trim();
                }
                catch { return ""; }
            }
        }

        public const int INPUT_BUCKETS = 6;
        public const int INPUT_SIZE = 768;
        public const int L1_SIZE = 512;
        public const int OUTPUT_BUCKETS = 8;

        private const int BUCKET_DIV = ((32 + OUTPUT_BUCKETS - 1) / OUTPUT_BUCKETS);
        private const int QA = 255;
        private const int QB = 64;
        public const int OUTPUT_SCALE = 400;

        public const int N_FTW = INPUT_SIZE * L1_SIZE * INPUT_BUCKETS;
        public const int N_FTB = L1_SIZE;

        public const int N_L1W = L1_SIZE * 2 * OUTPUT_BUCKETS;
        public const int N_L1B = OUTPUT_BUCKETS;

        private static readonly ValueNetContainer<short, short> Net;
        private static long ExpectedNetworkSize => (N_FTW + N_FTB + N_L1W + N_L1B) * sizeof(short);

        private static ReadOnlySpan<int> KingBuckets =>
        [
            0, 0, 1, 1,  7,  7,  6,  6,
            2, 2, 3, 3,  9,  9,  8,  8,
            2, 2, 3, 3,  9,  9,  8,  8,
            4, 4, 4, 4, 10, 10, 10, 10,
            4, 4, 4, 4, 10, 10, 10, 10,
            5, 5, 5, 5, 11, 11, 11, 11,
            5, 5, 5, 5, 11, 11, 11, 11,
            5, 5, 5, 5, 11, 11, 11, 11,
        ];


        static ValueNetwork()
        {
            Net = new();

            Initialize(NetworkName);
        }

        public static void Initialize(string networkToLoad, bool exitIfFail = true)
        {
            Console.WriteLine("ValueNetwork " + networkToLoad);
            using Stream netStream = NNUE.TryOpenFile(networkToLoad, exitIfFail);

            BinaryReader br;

            if (Zstd.IsCompressed(netStream))
            {
                byte[] buff = new byte[ExpectedNetworkSize + 64];
                MemoryStream memStream = Zstd.Decompress(netStream, buff);
                br = new BinaryReader(memStream);
            }
            else
            {
                br = new BinaryReader(netStream);
            }

            long toRead = ExpectedNetworkSize;
            if (br.BaseStream.Position + toRead > br.BaseStream.Length)
            {
                Console.WriteLine("ValueNetwork's BinaryReader doesn't have enough data for all weights and biases to be read!");
                Console.WriteLine($"It expects to read {toRead} bytes, but the stream's position is {br.BaseStream.Position} / {br.BaseStream.Length}");
                Console.WriteLine("The file being loaded is either not a valid 768 network, or has different layer sizes than the hardcoded ones.");
                if (exitIfFail)
                {
                    Environment.Exit(-1);
                }
                else
                {
                    return;
                }
            }

            for (int i = 0; i < N_FTW; i++)
                Net.FTWeights[i] = br.ReadInt16();

            for (int i = 0; i < N_FTB; i++)
                Net.FTBiases[i] = br.ReadInt16();

            for (int i = 0; i < N_L1W; i++)
                Net.L1Weights[i] = br.ReadInt16();

            for (int i = 0; i < N_L1B; i++)
                Net.L1Biases[i] = br.ReadInt16();
        }

        public static void RefreshAccumulator(Position pos)
        {
            RefreshPerspective(pos, White);
            RefreshPerspective(pos, Black);
        }

        private static void RefreshPerspective(Position pos, int perspective)
        {
            ref Accumulator accumulator = ref *pos.State->Accumulator;
            ref Bitboard bb = ref pos.bb;

            var ourAccumulation = (short*)accumulator[perspective];
            Unsafe.CopyBlock(ourAccumulation, Net.FTBiases, sizeof(short) * L1_SIZE);

            int ourKing = pos.State->KingSquares[perspective];
            ulong occ = bb.Occupancy;
            while (occ != 0)
            {
                int pieceIdx = poplsb(&occ);

                int pt = bb.GetPieceAtIndex(pieceIdx);
                int pc = bb.GetColorAtIndex(pieceIdx);

                int idx = FeatureIndexSingle(pc, pt, pieceIdx, ourKing, perspective);
                UnrollAdd(ourAccumulation, ourAccumulation, Net.FTWeights + idx);
            }
        }


        public static int Evaluate(Position pos) => Evaluate(pos, ((int)popcount(pos.bb.Occupancy) - 2) / BUCKET_DIV);
        public static int Evaluate(Position pos, int outputBucket)
        {
            int ev = GetEvaluation(pos, outputBucket);
            return int.Clamp(ev, ScoreTTLoss + 1, ScoreTTWin - 1);
        }

        private static int GetEvaluation(Position pos, int outputBucket)
        {
            ref Accumulator accumulator = ref *pos.State->Accumulator;
            RefreshAccumulator(pos);

            Vector256<short> maxVec = Vector256.Create((short)QA);
            Vector256<short> zeroVec = Vector256<short>.Zero;
            Vector256<int> sum = Vector256<int>.Zero;

            int SimdChunks = L1_SIZE / Vector256<short>.Count;

            var ourData   = accumulator[pos.ToMove];
            var theirData = accumulator[Not(pos.ToMove)];
            var ourWeights   = (Vector256<short>*)(&Net.L1Weights[outputBucket * (L1_SIZE * 2)]);
            var theirWeights = (Vector256<short>*)(&Net.L1Weights[outputBucket * (L1_SIZE * 2) + L1_SIZE]);
            for (int i = 0; i < SimdChunks; i++)
            {
                Vector256<short> clamp = Vector256.Min(maxVec, Vector256.Max(zeroVec, ourData[i]));
                Vector256<short> mult = clamp * ourWeights[i];

                (var mLo, var mHi) = Vector256.Widen(mult);
                (var cLo, var cHi) = Vector256.Widen(clamp);

                sum = Vector256.Add(sum, Vector256.Add(mLo * cLo, mHi * cHi));
            }

            for (int i = 0; i < SimdChunks; i++)
            {
                Vector256<short> clamp = Vector256.Min(maxVec, Vector256.Max(zeroVec, theirData[i]));
                Vector256<short> mult = clamp * theirWeights[i];

                (var mLo, var mHi) = Vector256.Widen(mult);
                (var cLo, var cHi) = Vector256.Widen(clamp);

                sum = Vector256.Add(sum, Vector256.Add(mLo * cLo, mHi * cHi));
            }

            var bias = Net.L1Biases[outputBucket];
            int output = Vector256.Sum(sum);
            output = (output / QA) + bias;

            return output * OUTPUT_SCALE / (QA * QB);
        }


        [MethodImpl(Inline)]
        private static int FeatureIndexSingle(int pc, int pt, int sq, int kingSq, int perspective)
        {
            const int ColorStride = 64 * 6;
            const int PieceStride = 64;

            if (perspective == Black)
            {
                sq ^= 56;
                kingSq ^= 56;
            }

            if (kingSq % 8 > 3)
            {
                sq ^= 7;
                kingSq ^= 7;
            }

            return ((768 * KingBuckets[kingSq]) + ((pc ^ perspective) * ColorStride) + (pt * PieceStride) + (sq)) * L1_SIZE;
        }

    }
}