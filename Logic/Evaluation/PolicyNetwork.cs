
using AwesomeOpossum.Logic.Data;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        public const int L1_SIZE = 512;
        public const int OUTPUT_SIZE = 1880;
        public const int OUTPUT_BUCKETS = 1;

        public const int QA = 255;
        public const int QB = 64;
        public static readonly int CHUNK = Vector256<short>.Count;

        public const int N_FTW = INPUT_SIZE * L1_SIZE * INPUT_BUCKETS;
        public const int N_FTB = L1_SIZE;

        public const int N_L1W = L1_SIZE * OUTPUT_BUCKETS * OUTPUT_SIZE;
        public const int N_L1B = OUTPUT_BUCKETS * OUTPUT_SIZE;

        private static readonly PolicyNetContainer<short, short> Net;
        private static long ExpectedNetworkSize => (N_FTW + N_FTB + N_L1W + N_L1B) * sizeof(short);

        private static readonly int* OFFSETS;
        private static readonly ulong* ALL_DESTINATIONS;

        static PolicyNetwork()
        {
            Net = new();

            OFFSETS = AlignedAllocZeroed<int>(65);
            ALL_DESTINATIONS = AlignedAllocZeroed<ulong>(64);

            Initialize(NetworkName);
            SetupOffsets();
        }

        public static void Initialize(string networkToLoad, bool exitIfFail = true)
        {
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


        public static void RefreshPolicyAccumulator(Position pos)
        {
            ref Bitboard bb = ref pos.bb;

            var wAccumulation = (short*)pos.PolicyAccumulator[White];
            var bAccumulation = (short*)pos.PolicyAccumulator[Black];
            Unsafe.CopyBlock(wAccumulation, Net.FTBiases, sizeof(short) * L1_SIZE);
            Unsafe.CopyBlock(bAccumulation, Net.FTBiases, sizeof(short) * L1_SIZE);

            ulong occ = bb.Occupancy;
            while (occ != 0)
            {
                int pieceIdx = poplsb(&occ);
                int pc = bb.GetColorAtIndex(pieceIdx);
                int pt = bb.GetPieceAtIndex(pieceIdx);

                var wIdx = FeatureIndex(pc, pt, pieceIdx, White);
                var bIdx = FeatureIndex(pc, pt, pieceIdx, Black);

                PolicyUnrollThings.Add(wAccumulation, wAccumulation, &Net.FTWeights[wIdx]);
                PolicyUnrollThings.Add(bAccumulation, bAccumulation, &Net.FTWeights[bIdx]);
            }

            int N = Vector256<short>.Count;
            int SimdChunks = L1_SIZE / N;

            var zero = Vector256<short>.Zero;
            var one = Vector256.Create((short)QA);

            var wVecs = (Vector256<short>*)wAccumulation;
            var bVecs = (Vector256<short>*)bAccumulation;
            for (int i = 0; i < SimdChunks; i++)
            {
                wVecs[i] = Vector256.Clamp(wVecs[i], zero, one);
                bVecs[i] = Vector256.Clamp(bVecs[i], zero, one);
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
        public static int MoveIndex(Move m, int stm, int kingSq)
        {
            int hm = (kingSq % 8 > 3) ? 7 : 0;
            var src = Orient(m.From ^ hm, stm);
            var dst = Orient(m.To ^ hm, stm);

            if (m.IsPromotion)
            {
                int ffile = src % 8;
                int tfile = dst % 8;
                int promoId = 2 * ffile + tfile;

                int thing = 22 * (m.PromotionTo - 1);
                return OFFSETS[64] + thing + promoId;
            }
            else
            {
                ulong below = ALL_DESTINATIONS[src] & ((1UL << dst) - 1);
                return OFFSETS[src] + (int)popcount(below);
            }
        }


        public static float Evaluate(Position pos, Move m)
        {
            int ksq = pos.State->KingSquares[pos.ToMove];
            int moveIndex = MoveIndex(m, pos.ToMove, ksq);

            int output;

            if (SIMDBindings.HasBindings)
            {
                var stmData = (short*)pos.PolicyAccumulator[pos.ToMove];
                var ntmData = (short*)pos.PolicyAccumulator[Not(pos.ToMove)];
                var l1Weights = &Net.L1Weights[moveIndex * L1_SIZE];
                var l1Biases = &Net.L1Biases[moveIndex];

                output = SIMDBindings.EvaluatePolicy(stmData, ntmData, l1Weights);
            }
            else
            {
                output = DoEvaluate(pos, moveIndex);
            }

            var rv = (((float)output / QA) + Net.L1Biases[moveIndex]) / (QA * QB);
            return rv;
        }


        public static int DoEvaluate(Position pos, int moveIndex)
        {
            var sum = Vector256<int>.Zero;

            int Stride = (L1_SIZE / Vector256<short>.Count) / 2;

            var data0 = pos.PolicyAccumulator[pos.ToMove];
            var data1 = &data0[Stride];
            var weights = (Vector256<short>*)(&Net.L1Weights[moveIndex * L1_SIZE]);
            for (int i = 0; i < Stride; i++)
            {
                (var mLo, var mHi) = Vector256.Widen(data0[i] * weights[i]);
                (var cLo, var cHi) = Vector256.Widen(data1[i]);

                sum = Vector256.Add(sum, Vector256.Add(mLo * cLo, mHi * cHi));
            }

            data0 = pos.PolicyAccumulator[Not(pos.ToMove)];
            data1 = &data0[Stride];
            weights = (Vector256<short>*)(&Net.L1Weights[(moveIndex * L1_SIZE) + L1_SIZE / 2]);
            for (int i = 0; i < Stride; i++)
            {
                (var mLo, var mHi) = Vector256.Widen(data0[i] * weights[i]);
                (var cLo, var cHi) = Vector256.Widen(data1[i]);

                sum = Vector256.Add(sum, Vector256.Add(mLo * cLo, mHi * cHi));
            }

            return Vector256.Sum(sum);
        }



        private static void SetupOffsets()
        {
            for (int square = 0; square < SquareNB; square++)
            {
                int rank = GetIndexRank(square);
                int file = GetIndexFile(square);

                ulong rooks = (ulong)((0xFFUL << (rank * 8)) ^ (0x0101_0101_0101_0101UL << file));
                ulong bishops = BinaryPrimitives.ReverseEndianness(Diagonals[file + rank]) ^ Diagonals[7 + file - rank];
                ALL_DESTINATIONS[square] = rooks | bishops | KnightAttacks[square] | KingAttacks[square];
            }

            int curr = 0;
            for (int square = 0; square <= SquareNB; square++)
            {
                OFFSETS[square] = curr;
                curr += (int)popcount(ALL_DESTINATIONS[square]);
            }
        }

        private static ReadOnlySpan<ulong> Diagonals =>
        [
            0x0100000000000000, 0x0201000000000000, 0x0402010000000000, 0x0804020100000000,
            0x1008040201000000, 0x2010080402010000, 0x4020100804020100, 0x8040201008040201,
            0x0080402010080402, 0x0000804020100804, 0x0000008040201008, 0x0000000080402010,
            0x0000000000804020, 0x0000000000008040, 0x0000000000000080,
        ];

        private static ReadOnlySpan<ulong> KingAttacks =>
        [
            0x0000000000000302, 0x0000000000000705, 0x0000000000000E0A, 0x0000000000001C14,
            0x0000000000003828, 0x0000000000007050, 0x000000000000E0A0, 0x000000000000C040,
            0x0000000000030203, 0x0000000000070507, 0x00000000000E0A0E, 0x00000000001C141C, 
            0x0000000000382838, 0x0000000000705070, 0x0000000000E0A0E0, 0x0000000000C040C0, 
            0x0000000003020300, 0x0000000007050700, 0x000000000E0A0E00, 0x000000001C141C00, 
            0x0000000038283800, 0x0000000070507000, 0x00000000E0A0E000, 0x00000000C040C000, 
            0x0000000302030000, 0x0000000705070000, 0x0000000E0A0E0000, 0x0000001C141C0000, 
            0x0000003828380000, 0x0000007050700000, 0x000000E0A0E00000, 0x000000C040C00000, 
            0x0000030203000000, 0x0000070507000000, 0x00000E0A0E000000, 0x00001C141C000000, 
            0x0000382838000000, 0x0000705070000000, 0x0000E0A0E0000000, 0x0000C040C0000000, 
            0x0003020300000000, 0x0007050700000000, 0x000E0A0E00000000, 0x001C141C00000000, 
            0x0038283800000000, 0x0070507000000000, 0x00E0A0E000000000, 0x00C040C000000000, 
            0x0302030000000000, 0x0705070000000000, 0x0E0A0E0000000000, 0x1C141C0000000000, 
            0x3828380000000000, 0x7050700000000000, 0xE0A0E00000000000, 0xC040C00000000000, 
            0x0203000000000000, 0x0507000000000000, 0x0A0E000000000000, 0x141C000000000000, 
            0x2838000000000000, 0x5070000000000000, 0xA0E0000000000000, 0x40C0000000000000, 
        ];

        private static ReadOnlySpan<ulong> KnightAttacks =>
        [
            0x0000000000020400, 0x0000000000050800, 0x00000000000A1100, 0x0000000000142200,
            0x0000000000284400, 0x0000000000508800, 0x0000000000A01000, 0x0000000000402000,
            0x0000000002040004, 0x0000000005080008, 0x000000000A110011, 0x0000000014220022,
            0x0000000028440044, 0x0000000050880088, 0x00000000A0100010, 0x0000000040200020,
            0x0000000204000402, 0x0000000508000805, 0x0000000A1100110A, 0x0000001422002214,
            0x0000002844004428, 0x0000005088008850, 0x000000A0100010A0, 0x0000004020002040,
            0x0000020400040200, 0x0000050800080500, 0x00000A1100110A00, 0x0000142200221400,
            0x0000284400442800, 0x0000508800885000, 0x0000A0100010A000, 0x0000402000204000,
            0x0002040004020000, 0x0005080008050000, 0x000A1100110A0000, 0x0014220022140000,
            0x0028440044280000, 0x0050880088500000, 0x00A0100010A00000, 0x0040200020400000,
            0x0204000402000000, 0x0508000805000000, 0x0A1100110A000000, 0x1422002214000000,
            0x2844004428000000, 0x5088008850000000, 0xA0100010A0000000, 0x4020002040000000,
            0x0400040200000000, 0x0800080500000000, 0x1100110A00000000, 0x2200221400000000,
            0x4400442800000000, 0x8800885000000000, 0x100010A000000000, 0x2000204000000000,
            0x0004020000000000, 0x0008050000000000, 0x00110A0000000000, 0x0022140000000000,
            0x0044280000000000, 0x0088500000000000, 0x0010A00000000000, 0x0020400000000000,
        ];

    }

    
}
