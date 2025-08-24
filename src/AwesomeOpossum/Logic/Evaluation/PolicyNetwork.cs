
using AwesomeOpossum.Logic.Data;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security.Claims;

namespace AwesomeOpossum.Logic.Evaluation
{
    public static unsafe class PolicyNetwork
    {
        public static string NetworkName
        {
            get
            {
                try
                {
                    return Assembly.GetEntryAssembly().GetCustomAttribute<PolicyFileAttribute>().PolicyFile.Trim();
                }
                catch { return ""; }
            }
        }

        public const int INPUT_BUCKETS = 1;
        public const int INPUT_SIZE = 768;
        public const int L1_SIZE = 2048;
        public const int OUTPUT_SIZE = 1880;
        public const int OUTPUT_BUCKETS = 2;

        public const int SEE_THRESHOLD = -105;

        public const int QA = 256;
        public const int QB = 64;
        public static readonly int CHUNK = Vector256<short>.Count;
        public static readonly int L1_CHUNKS = L1_SIZE / CHUNK;

        public const int L1_PAIRS = L1_SIZE / 2;

        public const int N_FTW = INPUT_SIZE * L1_SIZE * INPUT_BUCKETS;
        public const int N_FTB = L1_SIZE;

        public const int N_L1W = L1_PAIRS * OUTPUT_BUCKETS * OUTPUT_SIZE;
        public const int N_L1B = OUTPUT_BUCKETS * OUTPUT_SIZE;

        private static readonly PolicyNetContainer<short, short> Net = InitializeNet(NetworkName);
        private static long ExpectedNetworkSize => (N_FTW + N_FTB + N_L1W + N_L1B) * sizeof(short);


        public static PolicyNetContainer<short, short> InitializeNet(string networkToLoad, bool exitIfFail = true)
        {
            PolicyNetContainer<short, short> Net = new();
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
                Console.WriteLine("PolicyNetwork's BinaryReader doesn't have enough data for all weights and biases to be read!");
                Console.WriteLine($"It expects to read {toRead} bytes, but the stream's position is {br.BaseStream.Position} / {br.BaseStream.Length}");
                Console.WriteLine("The file being loaded is either not a valid 768 network, or has different layer sizes than the hardcoded ones.");
                if (exitIfFail)
                {
                    Environment.Exit(-1);
                }
                else
                {
                    return Net;
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

            br.Dispose();
            return Net;
        }


        public static void RefreshPolicyAccumulator(Position pos)
        {
            ref Bitboard bb = ref pos.bb;
            var stm = pos.ToMove;
            var ntm = Not(stm);

            var vert = (stm == Black) ? 56 : 0;
            var hori = (pos.KingSquare(stm) % 8 > 3) ? 7 : 0;
            var flip = vert ^ hori;

            var accumulation = pos.PolicyAccumulation;
            Unsafe.CopyBlock(accumulation, Net.FTBiases, sizeof(short) * L1_SIZE);

            for (int pt = Pawn; pt <= King; pt++)
            {
                ulong boys = bb.Pieces[pt] & bb.Colors[stm];
                ulong opps = bb.Pieces[pt] & bb.Colors[ntm];

                while (boys != 0)
                {
                    int sq = poplsb(&boys);
                    var idx = (64 * pt) + (sq ^ flip);
                    PolicyUnrollThings.Add(accumulation, accumulation, &Net.FTWeights[idx * L1_SIZE]);
                }

                while (opps != 0)
                {
                    int sq = poplsb(&opps);
                    var idx = 384 + (64 * pt) + (sq ^ flip);
                    PolicyUnrollThings.Add(accumulation, accumulation, &Net.FTWeights[idx * L1_SIZE]);
                }
            }

            var one = Vector256.Create((short)QA);
            var vecs = (Vector256<short>*)accumulation;
            for (int i = 0; i < L1_CHUNKS; i++)
            {
                vecs[i] = Vector256.Min(Vector256.Max(vecs[i], Vector256<short>.Zero), one);
            }
        }


        [MethodImpl(Inline)]
        private static int Orient(int sq, int perspective) => sq ^ (56 * perspective);

        [MethodImpl(Inline)]
        private static int FeatureIndex(int pc, int pt, int sq, int perspective)
        {
            return (((pc ^ perspective) * 64 * 6) + (pt * 64) + Orient(sq, perspective)) * L1_SIZE;
        }

        [MethodImpl(Inline)]
        public static int MoveIndex(Position pos, Move m)
        {
            const int MaxPromos = 22 * 4;

            int stm = pos.ToMove;
            int kingSq = pos.KingSquare(stm);

            int hm = (kingSq % 8 > 3) ? 7 : 0;
            var src = Orient(m.From ^ hm, stm);
            var dst = Orient(m.To ^ hm, stm);

            int seeBucket = pos.SEE(m, SEE_THRESHOLD) ? (MoveOffsets[64] + MaxPromos) : 0;

            int idx;
            if (m.IsPromotion)
            {
                int ffile = src % 8;
                int tfile = dst % 8;
                int promoId = 2 * ffile + tfile;

                int thing = 22 * (m.PromotionTo - 1);
                idx = MoveOffsets[64] + thing + promoId;
            }
            else
            {
                ulong below = AllDestinations[src] & ((1UL << dst) - 1);
                idx = MoveOffsets[src] + (int)popcount(below);
            }

            return idx + seeBucket;
        }


        public static float Evaluate(Position pos, Move m)
        {
            int moveIndex = MoveIndex(pos, m);

            var data = pos.PolicyAccumulation;
            var l1Weights = &Net.L1Weights[moveIndex * L1_PAIRS];
            var l1Bias = Net.L1Biases[moveIndex];

            int output = SIMDBindings.PolicyEvaluateFn(data, l1Weights);

            var rv = (((float)output / QA) + l1Bias) / (QA * QB);
            return rv;
        }


        [UnmanagedCallersOnly]
        public static int EvaluateImpl(short* data, short* l1Weights)
        {
            var sum = Vector256<int>.Zero;

            int Stride = L1_CHUNKS / 2;

            var data0 = (Vector256<short>*)&data[0];
            var data1 = (Vector256<short>*)&data[L1_SIZE / 2];
            var weights = (Vector256<short>*)l1Weights;
            for (int i = 0; i < Stride; i++)
            {
                var mullo = Vector256.Multiply(data0[i], weights[i]);
                var madd = Aliases.MultiplyAddAdjacentEpi16(mullo, data1[i]);

                sum = Vector256.Add(sum, madd);
            }

            return Vector256.Sum(sum);
        }


        private static ReadOnlySpan<int> MoveOffsets =>
        [
            0x000, 0x017, 0x02F, 0x048, 0x061, 0x07A, 0x093, 0x0AB,
            0x0C2, 0x0DA, 0x0F5, 0x112, 0x12F, 0x14C, 0x169, 0x184,
            0x19C, 0x1B5, 0x1D2, 0x1F3, 0x214, 0x235, 0x256, 0x273,
            0x28C, 0x2A5, 0x2C2, 0x2E3, 0x306, 0x329, 0x34A, 0x367,
            0x380, 0x399, 0x3B6, 0x3D7, 0x3FA, 0x41D, 0x43E, 0x45B,
            0x474, 0x48D, 0x4AA, 0x4CB, 0x4EC, 0x50D, 0x52E, 0x54B,
            0x564, 0x57C, 0x597, 0x5B4, 0x5D1, 0x5EE, 0x60B, 0x626,
            0x63E, 0x655, 0x66D, 0x686, 0x69F, 0x6B8, 0x6D1, 0x6E9,
            0x700,
        ];

        private static ReadOnlySpan<ulong> AllDestinations =>
        [
            0x81412111090707FE, 0x02824222120F0FFD, 0x04048444241F1FFB, 0x08080888493E3EF7,
            0x10101011927C7CEF, 0x2020212224F8F8DF, 0x4041424448F0F0BF, 0x8182848890E0E07F,
            0x412111090707FE07, 0x824222120F0FFD0F, 0x048444241F1FFB1F, 0x080888493E3EF73E,
            0x101011927C7CEF7C, 0x20212224F8F8DFF8, 0x41424448F0F0BFF0, 0x82848890E0E07FE0,
            0x2111090707FE0707, 0x4222120F0FFD0F0F, 0x8444241F1FFB1F1F, 0x0888493E3EF73E3E,
            0x1011927C7CEF7C7C, 0x212224F8F8DFF8F8, 0x424448F0F0BFF0F0, 0x848890E0E07FE0E0,
            0x11090707FE070709, 0x22120F0FFD0F0F12, 0x44241F1FFB1F1F24, 0x88493E3EF73E3E49,
            0x11927C7CEF7C7C92, 0x2224F8F8DFF8F824, 0x4448F0F0BFF0F048, 0x8890E0E07FE0E090,
            0x090707FE07070911, 0x120F0FFD0F0F1222, 0x241F1FFB1F1F2444, 0x493E3EF73E3E4988,
            0x927C7CEF7C7C9211, 0x24F8F8DFF8F82422, 0x48F0F0BFF0F04844, 0x90E0E07FE0E09088,
            0x0707FE0707091121, 0x0F0FFD0F0F122242, 0x1F1FFB1F1F244484, 0x3E3EF73E3E498808,
            0x7C7CEF7C7C921110, 0xF8F8DFF8F8242221, 0xF0F0BFF0F0484442, 0xE0E07FE0E0908884,
            0x07FE070709112141, 0x0FFD0F0F12224282, 0x1FFB1F1F24448404, 0x3EF73E3E49880808,
            0x7CEF7C7C92111010, 0xF8DFF8F824222120, 0xF0BFF0F048444241, 0xE07FE0E090888482,
            0xFE07070911214181, 0xFD0F0F1222428202, 0xFB1F1F2444840404, 0xF73E3E4988080808,
            0xEF7C7C9211101010, 0xDFF8F82422212020, 0xBFF0F04844424140, 0x7FE0E09088848281,
        ];

    }

    
}
