
//#define PERMUTE_COUNT
//#define PERMUTE_DISABLED

using AwesomeOpossum.Logic.Threads;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
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

        public const int INPUT_BUCKETS = 1;
        public const int INPUT_SIZE = 768;
        public const int L1_SIZE = 1024;
        public const int L2_SIZE = 64;
        public const int L3_SIZE = 256;
        public const int OUTPUT_BUCKETS = 8;

        private const int BUCKET_DIV = ((32 + OUTPUT_BUCKETS - 1) / OUTPUT_BUCKETS);
        private const int FT_QUANT = 256;
        private const int FT_SHIFT = 10;
        private const int L1_QUANT = 64;
        public const int OUTPUT_SCALE = 400;

        private const int N_FTW = INPUT_SIZE * L1_SIZE * INPUT_BUCKETS;
        private const int N_FTB = L1_SIZE;
        private const int N_L1W = OUTPUT_BUCKETS * L1_SIZE * L2_SIZE;
        private const int N_L1B = OUTPUT_BUCKETS * L2_SIZE;
        private const int N_L2W = OUTPUT_BUCKETS * L2_SIZE * L3_SIZE;
        private const int N_L2B = OUTPUT_BUCKETS * L3_SIZE;
        private const int N_L3W = OUTPUT_BUCKETS * L3_SIZE;
        private const int N_L3B = OUTPUT_BUCKETS;

        private static readonly ValueNetContainer<short, sbyte, float> Net;
        private static readonly Vector128<ushort>* NNZLookup;

        private static long ExpectedNetworkSize => (N_FTW + N_FTB) * sizeof(short) +
                                                           (N_L1W) * sizeof(byte) +
                           (N_L1B + N_L2W + N_L2B + N_L3W + N_L3B) * sizeof(float);

        private const int L1_CHUNK_PER_32 = sizeof(int) / sizeof(sbyte);
        private const int L1_PAIR_COUNT = L1_SIZE / 2;
        private const int I16_CHUNK_SIZE = 32 / sizeof(short);
        private const int I32_CHUNK_SIZE = 32 / sizeof(int);
        private const int F32_CHUNK_SIZE = 32 / sizeof(float);
        private const int NNZ_INPUT_SIMD_WIDTH = I32_CHUNK_SIZE;
        private const int NNZ_OUTPUTS_PER_CHUNK = (NNZ_INPUT_SIMD_WIDTH > 8 ? NNZ_INPUT_SIMD_WIDTH: 8) / 8;

        private static ReadOnlySpan<int> KingBuckets =>
        [
            0, 0, 0, 0, 1, 1, 1, 1,
            0, 0, 0, 0, 1, 1, 1, 1,
            0, 0, 0, 0, 1, 1, 1, 1,
            0, 0, 0, 0, 1, 1, 1, 1,
            0, 0, 0, 0, 1, 1, 1, 1,
            0, 0, 0, 0, 1, 1, 1, 1,
            0, 0, 0, 0, 1, 1, 1, 1,
            0, 0, 0, 0, 1, 1, 1, 1,
        ];


        static ValueNetwork()
        {
            Net = new();

            NNZLookup = AlignedAllocZeroed<Vector128<ushort>>(256);
            SetupNNZ();

            Initialize(NetworkName);
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
                Net.L1Weights[0][i] = br.ReadSByte();

            for (int i = 0; i < N_L1B; i++)
                Net.L1Biases[0][i] = br.ReadSingle();

            for (int i = 0; i < N_L2W; i++)
                Net.L2Weights[0][i] = br.ReadSingle();

            for (int i = 0; i < N_L2B; i++)
                Net.L2Biases[0][i] = br.ReadSingle();

            for (int i = 0; i < N_L3W; i++)
                Net.L3Weights[0][i] = br.ReadSingle();

            for (int i = 0; i < N_L3B; i++)
                Net.L3Biases[i] = br.ReadSingle();

            sbyte[,,] tempL1 = new sbyte[OUTPUT_BUCKETS, L2_SIZE, L1_SIZE];
            float[,,] tempL2 = new float[OUTPUT_BUCKETS, L3_SIZE, L2_SIZE];

            fixed (sbyte* tl1 = tempL1)
                Unsafe.CopyBlock(tl1, Net.L1Weights[0], N_L1W * sizeof(sbyte));

            fixed (float* tl2 = tempL2) 
                Unsafe.CopyBlock(tl2, Net.L2Weights[0], N_L2W * sizeof(float));

            PermuteFT();
            PermuteL1(tempL1);

            for (int bucket = 0; bucket < OUTPUT_BUCKETS; bucket++)
            {
                for (int i = 0; i < L1_SIZE; i += 4)
                    for (int j = 0; j < L2_SIZE; ++j)
                        for (int k = 0; k < 4; ++k)
                            Net.L1Weights[bucket][i * L2_SIZE
                                                + j * 4
                                                + k] = tempL1[bucket, j, i + k];

                for (int i = 0; i < L2_SIZE; ++i)
                    for (int j = 0; j < L3_SIZE; ++j)
                        Net.L2Weights[bucket][i * L3_SIZE + j] = tempL2[bucket, j, i];
            }

            PermuteDpbusd();
        }

        public static void RefreshAccumulator(Position pos)
        {
            RefreshPerspective(pos, White);
            RefreshPerspective(pos, Black);
        }

        private static void RefreshPerspective(Position pos, int perspective)
        {
            ref Accumulator accumulator = ref pos.ValueAccumulator;
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
            ref Accumulator accumulator = ref pos.ValueAccumulator;
            RefreshAccumulator(pos);

            var us = (short*)accumulator[pos.ToMove];
            var them = (short*)accumulator[Not(pos.ToMove)];
            var l1w = Net.L1Weights[outputBucket];
            var l1b = Net.L1Biases[outputBucket];
            var l2w = Net.L2Weights[outputBucket];
            var l2b = Net.L2Biases[outputBucket];
            var l3w = Net.L3Weights[outputBucket];
            var l3b = Net.L3Biases[outputBucket];

            float output = SIMDBindings.ValueEvaluateFn(us, them, l1w, l1b, l2w, l2b, l3w, l3b);

            return int.Clamp((int)output, ScoreTTLoss + 1, ScoreTTWin - 1);
        }


        [UnmanagedCallersOnly]
        public static float EvaluateImpl(short* us, short* them, sbyte* L1Weights, float* L1Biases,
            float* L2Weights, float* L2Biases, float* L3Weights, float L3Bias)
        {
            float L3Output = 0;
            float* L1Outputs = stackalloc float[L2_SIZE];
            float* L2Outputs = stackalloc float[L3_SIZE];

            ActivateFTSparse(us, them, L1Weights, L1Biases, L1Outputs);
            ActivateL2(L1Outputs, L2Weights, L2Biases, L2Outputs);
            ActivateL3(L2Outputs, L3Weights, L3Bias, ref L3Output);

            return L3Output;
        }


        private static void ActivateFTSparse(short* us, short* them, sbyte* weights, float* biases, float* output)
        {
            var ft_zero = _mm256_setzero_epi16();
            var ft_one = _mm256_set1_epi16(FT_QUANT);

            int nnzCount = 0;
            int offset = 0;

            sbyte* ft_outputs = stackalloc sbyte[L1_SIZE];
            ushort* nnzIndices = stackalloc ushort[L1_SIZE / L1_CHUNK_PER_32];

            Vector128<ushort> baseInc = Vector128.Create((ushort)8);
            Vector128<ushort> baseVec = Vector128<ushort>.Zero;

            for (int perspective = 0; perspective < 2; perspective++)
            {
                short* acc = perspective == 0 ? us : them;

                for (int i = 0; i < L1_PAIR_COUNT; i += (I16_CHUNK_SIZE * 2))
                {
                    var input0a = _mm256_load_si256(&acc[i + 0 * I16_CHUNK_SIZE + 0]);
                    var input0b = _mm256_load_si256(&acc[i + 1 * I16_CHUNK_SIZE + 0]);

                    var input1a = _mm256_load_si256(&acc[i + 0 * I16_CHUNK_SIZE + L1_PAIR_COUNT]);
                    var input1b = _mm256_load_si256(&acc[i + 1 * I16_CHUNK_SIZE + L1_PAIR_COUNT]);

                    var clipped0a = _mm256_min_epi16(_mm256_max_epi16(input0a, ft_zero), ft_one);
                    var clipped0b = _mm256_min_epi16(_mm256_max_epi16(input0b, ft_zero), ft_one);

                    var clipped1a = _mm256_min_epi16(input1a, ft_one);
                    var clipped1b = _mm256_min_epi16(input1b, ft_one);

                    var producta = _mm256_mulhi_epi16(_mm256_slli_epi16(clipped0a, 16 - FT_SHIFT), clipped1a);
                    var productb = _mm256_mulhi_epi16(_mm256_slli_epi16(clipped0b, 16 - FT_SHIFT), clipped1b);

                    var product_one = _mm256_packus_epi16(producta, productb).AsByte();
                    _mm256_storeu_si256(&ft_outputs[offset + i], product_one.AsSByte());

                    var nnz_mask = vec_nnz_mask(product_one);

                    for (int j = 0; j < NNZ_OUTPUTS_PER_CHUNK; j++)
                    {
                        int lookup = (nnz_mask >> (j * 8)) & 0xFF;
                        var offsets = NNZLookup[lookup];
                        _mm_storeu_si128(&nnzIndices[nnzCount], _mm_add_epi16(baseVec, offsets));

                        nnzCount += int.PopCount(lookup);
                        baseVec += baseInc;
                    }

                }

                offset += L1_PAIR_COUNT;
            }

#if PERMUTE_COUNT
            EvalCalls++;
            ActivationCount += (ulong)nnzCount;
            for (int i = 0; i < L1_SIZE; i++)
                NNZCounts[i] += (ft_outputs[i] != 0) ? 1UL : 0;
#endif

            ActivateL1Sparse(ft_outputs, weights, biases, output, new Span<ushort>(nnzIndices, nnzCount));
        }


        private static void ActivateL1Sparse(sbyte* inputs, sbyte* weights, float* biases, float* output, Span<ushort> nnzIndices)
        {
            var sums = stackalloc Vector256<int>[L2_SIZE / I32_CHUNK_SIZE];

            int nnzCount = nnzIndices.Length;
            int* inputs32 = (int*)(inputs);
            for (int i = 0; i < nnzCount; i++)
            {
                var index = nnzIndices[i];
                var input32 = _mm256_set1_epi32(inputs32[index]);
                var weight = (Vector256<sbyte>*)(&weights[index * L1_CHUNK_PER_32 * L2_SIZE]);
                for (int k = 0; k < L2_SIZE / F32_CHUNK_SIZE; k++)
                {
                    sums[k] = vec_dpbusd_epi32(sums[k], input32.AsByte(), weight[k]);
                }
            }

            var zero = _mm256_set1_ps(0.0f);
            var one = Vector256<float>.One;

            var sumMul = _mm256_set1_ps((1 << FT_SHIFT) / (float)(FT_QUANT * FT_QUANT * L1_QUANT));
            for (int i = 0; i < L2_SIZE / F32_CHUNK_SIZE; ++i)
            {
                var biasVec = _mm256_loadu_ps(&biases[i * F32_CHUNK_SIZE]);
                var sumPs = _mm256_fmadd_ps(_mm256_cvtepi32_ps(sums[i]), sumMul, biasVec);
                var clipped = _mm256_min_ps(_mm256_max_ps(sumPs, zero), one);
                var squared = _mm256_mul_ps(clipped, clipped);
                _mm256_storeu_ps(&output[i * F32_CHUNK_SIZE], squared);

            }
        }


        private static void ActivateL2(float* inputs, float* weights, float* biases, float* output)
        {
            var sumVecs = stackalloc Vector256<float>[L3_SIZE / F32_CHUNK_SIZE];

            for (int i = 0; i < L3_SIZE / F32_CHUNK_SIZE; ++i)
                sumVecs[i] = _mm256_loadu_ps(&biases[i * F32_CHUNK_SIZE]);

            for (int i = 0; i < L2_SIZE; ++i)
            {
                var inputVec = _mm256_set1_ps(inputs[i]);
                var weight = (Vector256<float>*)(&weights[i * L3_SIZE]);
                for (int j = 0; j < L3_SIZE / F32_CHUNK_SIZE; ++j)
                {
                    sumVecs[j] = vec_mul_add_ps(inputVec, weight[j], sumVecs[j]);
                }
            }

            var zero = _mm256_set1_ps(0.0f);
            var one = _mm256_set1_ps(1.0f);
            for (int i = 0; i < L3_SIZE / F32_CHUNK_SIZE; ++i)
            {
                var clipped = _mm256_min_ps(_mm256_max_ps(sumVecs[i], zero), one);
                var squared = _mm256_mul_ps(clipped, clipped);
                _mm256_storeu_ps(&output[i * F32_CHUNK_SIZE], squared);
            }
        }


        private static void ActivateL3(float* inputs, float* weights, float bias, ref float output)
        {
            var sumVec = _mm256_set1_ps(0.0f);

            for (int i = 0; i < L3_SIZE / F32_CHUNK_SIZE; i++)
            {
                var weightVec = _mm256_loadu_ps(&weights[i * F32_CHUNK_SIZE]);
                var inputsVec = _mm256_loadu_ps(&inputs[i * F32_CHUNK_SIZE]);
                sumVec = vec_mul_add_ps(inputsVec, weightVec, sumVec);
            }

            output = (bias + vec_reduce_add_ps(sumVec)) * OUTPUT_SCALE;
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



        private static void SetupNNZ()
        {
            ushort[] temp = new ushort[8];
            for (int i = 0; i < 256; i++)
            {
                Array.Clear(temp);
                int j = i;
                int k = 0;
                while (j != 0)
                {
                    uint lsbIndex = uint.TrailingZeroCount((uint)j);
                    j &= j - 1;
                    temp[k] = (ushort)lsbIndex;
                    k++;
                }

                NNZLookup[i] = Vector128.Create(temp);
            }
        }



        private static void PermuteDpbusd()
        {
            const int numRegi = 4;
            const int numChunks = 16 / sizeof(short);
            Span<int> order = [0, 2, 1, 3];

            Vector128<short>[] regi = new Vector128<short>[numRegi];
            var ws = (Vector128<short>*)Net.FTWeights;
            var bs = (Vector128<short>*)Net.FTBiases;

            for (int i = 0; i < N_FTW / numChunks; i += numRegi)
            {
                for (int j = 0; j < numRegi; j++) regi[j] = ws[i + j];
                for (int j = 0; j < numRegi; j++) ws[i + j] = regi[order[j]];
            }

            for (int i = 0; i < N_FTB / numChunks; i += numRegi)
            {
                for (int j = 0; j < numRegi; j++) regi[j] = bs[i + j];
                for (int j = 0; j < numRegi; j++) bs[i + j] = regi[order[j]];
            }
        }


        private static void PermuteFT()
        {
            Span<short> ftWeights = new(Net.FTWeights, N_FTW);
            Span<short> ftBiases = new(Net.FTBiases, N_FTB);

            const int OneBucket = (INPUT_SIZE * L1_SIZE);
            short* temp = AlignedAllocZeroed<short>(OneBucket);

            for (int bucket = 0; bucket < INPUT_BUCKETS; bucket++)
            {
                Span<short> ftBucket = ftWeights[(bucket * OneBucket)..((bucket + 1) * OneBucket)];
                ftBucket.CopyTo(new Span<short>(temp, OneBucket));
                for (int i = 0; i < INPUT_SIZE; i++)
                {
                    for (int dst = 0; dst < PermuteIndices.Length; dst++)
                    {
                        int src = PermuteIndices[dst];
                        var f = i * L1_SIZE;

                        ftBucket[f + dst] = temp[f + src];
                        ftBucket[f + dst + L1_PAIR_COUNT] = temp[f + src + L1_PAIR_COUNT];
                    }
                }
            }

            ftBiases.CopyTo(new Span<short>(temp, L1_SIZE));
            for (int dst = 0; dst < PermuteIndices.Length; dst++)
            {
                int src = PermuteIndices[dst];

                ftBiases[dst] = temp[src];
                ftBiases[dst + L1_PAIR_COUNT] = temp[src + L1_PAIR_COUNT];
            }

            NativeMemory.AlignedFree(temp);
        }


        private static void PermuteL1(sbyte[,,] l1Weights)
        {
            sbyte[,,] temp = new sbyte[OUTPUT_BUCKETS, L2_SIZE, L1_SIZE];

            Array.Copy(l1Weights, temp, N_L1W);
            for (int dst = 0; dst < PermuteIndices.Length; dst++)
            {
                int src = PermuteIndices[dst];

                for (int b = 0; b < OUTPUT_BUCKETS; b++)
                {
                    for (int l2 = 0; l2 < L2_SIZE; l2++)
                    {
                        l1Weights[b, l2, dst] = temp[b, l2, src];
                        l1Weights[b, l2, dst + L1_PAIR_COUNT] = temp[b, l2, src + L1_PAIR_COUNT];
                    }
                }
            }
        }



#if PERMUTE_COUNT
        public static ulong ActivationCount = 0;
        public static ulong EvalCalls = 0;
        public static readonly ulong[] NNZCounts = new ulong[L1_SIZE];
#endif

        public static void PrintActivationStats()
        {
#if PERMUTE_COUNT
            using var f = File.Open("perm.txt", FileMode.Create);
            using StreamWriter tw = new(f);
            for (int i = 0; i < NNZCounts.Length; i++)
                tw.WriteLine($"{i} {NNZCounts[i]}");

            Log($"{ActivationCount} / {EvalCalls} = {(double)ActivationCount / EvalCalls}");

            NNZCounts
                .Select((v, i) => (i, v))
                .Where(pair => pair.i < (L1_SIZE / 2))
                .OrderByDescending(pair => pair.v)
                .Select(pair => pair.i)
                .Chunk(16)
                .ToList()
                .ForEach(chunk =>
                {
                    Console.WriteLine($"{string.Join(", ", chunk)},");
                });
#endif
        }

#if PERMUTE_DISABLED
        private static readonly int[] PermuteIndices = [.. Enumerable.Range(0, L1_PAIR_COUNT)];
#else
        private static readonly int[] PermuteIndices = BestIndices.ToArray();
#endif

        private static ReadOnlySpan<int> BestIndices =>
        [
            108, 342, 463, 401, 355, 293, 151, 369, 425, 209, 96, 207, 317, 469, 159, 175,
            460, 7, 486, 192, 243, 190, 452, 482, 126, 277, 204, 138, 330, 258, 395, 282,
            320, 137, 431, 104, 368, 379, 468, 128, 185, 448, 385, 381, 260, 239, 121, 238,
            427, 383, 359, 227, 483, 75, 27, 202, 439, 125, 95, 256, 307, 31, 92, 110,
            218, 502, 363, 109, 70, 264, 360, 326, 149, 272, 442, 90, 221, 480, 329, 2,
            473, 146, 323, 205, 97, 35, 371, 67, 22, 477, 80, 410, 176, 162, 489, 215,
            111, 411, 407, 173, 134, 311, 21, 143, 449, 61, 34, 321, 324, 8, 50, 378,
            285, 17, 312, 183, 343, 252, 120, 387, 263, 509, 222, 331, 432, 101, 361, 398,
            254, 305, 122, 436, 376, 364, 446, 200, 220, 443, 208, 212, 242, 116, 348, 153,
            68, 211, 174, 373, 129, 414, 386, 337, 224, 161, 437, 182, 281, 105, 46, 462,
            357, 322, 77, 400, 198, 408, 43, 345, 447, 296, 396, 199, 66, 14, 292, 426,
            235, 295, 214, 250, 38, 193, 273, 115, 164, 346, 493, 504, 299, 404, 157, 94,
            365, 341, 444, 327, 306, 457, 85, 347, 119, 213, 169, 354, 289, 18, 356, 340,
            127, 10, 510, 48, 286, 423, 351, 382, 241, 45, 507, 156, 178, 409, 268, 194,
            16, 406, 313, 506, 503, 11, 44, 246, 83, 316, 229, 344, 234, 187, 429, 232,
            270, 500, 424, 147, 60, 29, 100, 188, 74, 84, 240, 139, 271, 36, 13, 459,
            498, 445, 247, 253, 82, 244, 91, 349, 451, 12, 28, 338, 166, 350, 33, 228,
            4, 279, 435, 467, 78, 422, 390, 267, 236, 389, 20, 251, 284, 490, 367, 377,
            478, 496, 106, 453, 63, 49, 57, 226, 225, 328, 413, 495, 314, 494, 165, 98,
            58, 114, 65, 72, 308, 366, 201, 54, 136, 332, 197, 266, 132, 492, 399, 41,
            245, 158, 454, 315, 144, 511, 301, 278, 59, 150, 416, 297, 304, 53, 333, 394,
            1, 397, 195, 81, 302, 180, 93, 113, 393, 438, 73, 55, 434, 141, 319, 140,
            259, 15, 230, 276, 300, 89, 474, 249, 191, 171, 392, 418, 403, 487, 64, 62,
            485, 172, 130, 40, 210, 9, 223, 491, 206, 217, 99, 124, 23, 274, 309, 479,
            265, 0, 152, 472, 87, 465, 441, 145, 163, 412, 298, 112, 5, 203, 219, 107,
            353, 135, 168, 488, 310, 508, 56, 3, 155, 475, 374, 470, 497, 417, 71, 375,
            102, 288, 179, 86, 131, 384, 160, 362, 51, 26, 481, 499, 133, 216, 19, 255,
            186, 24, 402, 283, 430, 148, 262, 415, 189, 79, 339, 52, 290, 257, 177, 318,
            428, 181, 231, 370, 380, 405, 388, 287, 440, 269, 303, 335, 30, 118, 42, 466,
            47, 455, 25, 372, 458, 196, 76, 237, 433, 69, 419, 154, 352, 421, 32, 456,
            248, 334, 142, 420, 275, 184, 471, 37, 170, 280, 358, 325, 464, 39, 501, 117,
            461, 294, 391, 484, 167, 103, 261, 6, 336, 123, 450, 88, 476, 291, 233, 505,
        ];

    }
}