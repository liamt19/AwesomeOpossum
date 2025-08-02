
//#define PERMUTE_COUNT
//#define PERMUTE_DISABLED

using AwesomeOpossum.Logic.Threads;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static AwesomeOpossum.Logic.Evaluation.Aliases;

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
        private const int N_L1W = OUTPUT_BUCKETS * L1_PAIRS * L2_SIZE;
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

        public const int L1_PAIRS = L1_SIZE / 2;

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

            sbyte[,,] tempL1 = new sbyte[OUTPUT_BUCKETS, L2_SIZE, L1_PAIRS];
            float[,,] tempL2 = new float[OUTPUT_BUCKETS, L3_SIZE, L2_SIZE];

            fixed (sbyte* tl1 = tempL1)
                Unsafe.CopyBlock(tl1, Net.L1Weights[0], N_L1W * sizeof(sbyte));

            fixed (float* tl2 = tempL2) 
                Unsafe.CopyBlock(tl2, Net.L2Weights[0], N_L2W * sizeof(float));

            PermuteFT();
            PermuteL1(tempL1);

            for (int bucket = 0; bucket < OUTPUT_BUCKETS; bucket++)
            {
                for (int i = 0; i < L1_PAIRS; i += 4)
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
            ref Bitboard bb = ref pos.bb;
            var stm = pos.ToMove;
            var ntm = Not(stm);

            var vert = (stm == Black) ? 56 : 0;
            var hori = (pos.KingSquare(stm) % 8 > 3) ? 7 : 0;
            var flip = vert ^ hori;

            var accumulation = pos.ValueAccumulation;
            Unsafe.CopyBlock(accumulation, Net.FTBiases, sizeof(short) * L1_SIZE);

            for (int pt = Pawn; pt <= King; pt++)
            {
                ulong boys = bb.Pieces[pt] & bb.Colors[stm];
                ulong opps = bb.Pieces[pt] & bb.Colors[ntm];

                while (boys != 0)
                {
                    int sq = poplsb(&boys);
                    var idx = (64 * pt) + (sq ^ flip);
                    ValueUnrollThings.Add(accumulation, accumulation, &Net.FTWeights[idx * L1_SIZE]);
                }

                while (opps != 0)
                {
                    int sq = poplsb(&opps);
                    var idx = 384 + (64 * pt) + (sq ^ flip);
                    ValueUnrollThings.Add(accumulation, accumulation, &Net.FTWeights[idx * L1_SIZE]);
                }

            }
        }


        public static int Evaluate(Position pos) => Evaluate(pos, ((int)popcount(pos.bb.Occupancy) - 2) / BUCKET_DIV);
        public static int Evaluate(Position pos, int outputBucket)
        {
            RefreshAccumulator(pos);

            var data = pos.ValueAccumulation;
            var l1w = Net.L1Weights[outputBucket];
            var l1b = Net.L1Biases[outputBucket];
            var l2w = Net.L2Weights[outputBucket];
            var l2b = Net.L2Biases[outputBucket];
            var l3w = Net.L3Weights[outputBucket];
            var l3b = Net.L3Biases[outputBucket];

            float output = SIMDBindings.ValueEvaluateFn(data, l1w, l1b, l2w, l2b, l3w, l3b);

            return int.Clamp((int)output, ScoreTTLoss + 1, ScoreTTWin - 1);
        }


        [UnmanagedCallersOnly]
        public static float EvaluateImpl(short* data, sbyte* L1Weights, float* L1Biases,
            float* L2Weights, float* L2Biases, float* L3Weights, float L3Bias)
        {
            float L3Output = 0;
            float* L1Outputs = stackalloc float[L2_SIZE];
            float* L2Outputs = stackalloc float[L3_SIZE];

            ActivateFTSparse(data, L1Weights, L1Biases, L1Outputs);
            ActivateL2(L1Outputs, L2Weights, L2Biases, L2Outputs);
            ActivateL3(L2Outputs, L3Weights, L3Bias, ref L3Output);

            return L3Output;
        }


        private static void ActivateFTSparse(short* data, sbyte* weights, float* biases, float* output)
        {
            var ft_zero = _mm256_setzero_epi16();
            var ft_one = _mm256_set1_epi16(FT_QUANT);

            int nnzCount = 0;

            sbyte* ft_outputs = stackalloc sbyte[L1_PAIRS];
            ushort* nnzIndices = stackalloc ushort[L1_PAIRS / 4];

            Vector128<ushort> baseInc = Vector128.Create((ushort)8);
            Vector128<ushort> baseVec = Vector128<ushort>.Zero;

            var ftPair0 = data;
            var ftPair1 = &data[L1_PAIRS];

            for (int i = 0; i < L1_PAIRS; i += (I16_CHUNK_SIZE * 2))
            {
                var input0a = _mm256_load_si256(&ftPair0[i + 0 * I16_CHUNK_SIZE]);
                var input0b = _mm256_load_si256(&ftPair0[i + 1 * I16_CHUNK_SIZE]);

                var input1a = _mm256_load_si256(&ftPair1[i + 0 * I16_CHUNK_SIZE]);
                var input1b = _mm256_load_si256(&ftPair1[i + 1 * I16_CHUNK_SIZE]);

                var clipped0a = _mm256_min_epi16(_mm256_max_epi16(input0a, ft_zero), ft_one);
                var clipped0b = _mm256_min_epi16(_mm256_max_epi16(input0b, ft_zero), ft_one);

                var clipped1a = _mm256_min_epi16(input1a, ft_one);
                var clipped1b = _mm256_min_epi16(input1b, ft_one);

                var producta = _mm256_mulhi_epi16(_mm256_slli_epi16(clipped0a, 16 - FT_SHIFT), clipped1a);
                var productb = _mm256_mulhi_epi16(_mm256_slli_epi16(clipped0b, 16 - FT_SHIFT), clipped1b);

                var product_one = _mm256_packus_epi16(producta, productb).AsByte();
                _mm256_storeu_si256(&ft_outputs[i], product_one.AsSByte());

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

#if PERMUTE_COUNT
            EvalCalls++;
            ActivationCount += (ulong)nnzCount;
            for (int i = 0; i < L1_PAIRS; i++)
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
                var weight = (Vector256<sbyte>*)(&weights[index * 4 * L2_SIZE]);
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
                        ftBucket[f + dst + L1_PAIRS] = temp[f + src + L1_PAIRS];
                    }
                }
            }

            ftBiases.CopyTo(new Span<short>(temp, L1_SIZE));
            for (int dst = 0; dst < PermuteIndices.Length; dst++)
            {
                int src = PermuteIndices[dst];

                ftBiases[dst] = temp[src];
                ftBiases[dst + L1_PAIRS] = temp[src + L1_PAIRS];
            }

            NativeMemory.AlignedFree(temp);
        }


        private static void PermuteL1(sbyte[,,] l1Weights)
        {
            sbyte[,,] temp = new sbyte[OUTPUT_BUCKETS, L2_SIZE, L1_PAIRS];

            Array.Copy(l1Weights, temp, N_L1W);
            for (int dst = 0; dst < PermuteIndices.Length; dst++)
            {
                int src = PermuteIndices[dst];

                for (int b = 0; b < OUTPUT_BUCKETS; b++)
                {
                    for (int l2 = 0; l2 < L2_SIZE; l2++)
                    {
                        l1Weights[b, l2, dst] = temp[b, l2, src];
                        //l1Weights[b, l2, dst + (L1_PAIRS / 2)] = temp[b, l2, src + (L1_PAIRS / 2)];
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
                .Where(pair => pair.i < L1_PAIRS)
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
        private static readonly int[] PermuteIndices = [.. Enumerable.Range(0, L1_PAIRS)];
#else
        private static readonly int[] PermuteIndices = BestIndices.ToArray();
#endif

        private static ReadOnlySpan<int> BestIndices =>
        [
            424, 203, 97, 191, 510, 2, 151, 45, 65, 453, 56, 72, 50, 147, 482, 131,
            477, 105, 293, 120, 243, 218, 495, 54, 324, 366, 438, 158, 494, 402, 41, 143,
            286, 27, 378, 334, 326, 231, 306, 361, 61, 249, 40, 458, 202, 79, 376, 360,
            263, 339, 371, 109, 465, 349, 394, 415, 318, 95, 499, 443, 148, 102, 89, 386,
            132, 375, 292, 184, 385, 94, 285, 407, 281, 251, 136, 164, 367, 466, 240, 198,
            382, 57, 351, 17, 62, 256, 277, 224, 391, 417, 153, 505, 379, 208, 316, 200,
            370, 188, 250, 418, 173, 463, 201, 296, 311, 117, 335, 163, 295, 60, 159, 31,
            68, 327, 308, 309, 39, 125, 239, 448, 271, 92, 139, 353, 363, 291, 84, 161,
            343, 21, 48, 478, 273, 489, 172, 227, 400, 392, 496, 71, 26, 233, 189, 156,
            511, 264, 486, 35, 475, 503, 501, 242, 380, 179, 141, 452, 23, 154, 509, 434,
            272, 96, 209, 16, 246, 252, 212, 481, 165, 398, 24, 137, 193, 355, 435, 304,
            157, 437, 81, 480, 470, 449, 384, 36, 226, 232, 298, 329, 388, 431, 19, 34,
            266, 337, 445, 214, 506, 401, 245, 149, 493, 397, 412, 474, 346, 462, 473, 274,
            305, 195, 278, 441, 152, 6, 347, 485, 464, 28, 78, 414, 228, 461, 288, 144,
            155, 113, 283, 403, 268, 389, 169, 107, 88, 383, 192, 490, 377, 178, 187, 74,
            119, 134, 411, 103, 504, 86, 430, 341, 55, 399, 469, 38, 483, 49, 69, 352,
            150, 126, 1, 37, 492, 0, 500, 258, 455, 497, 427, 146, 413, 488, 14, 230,
            442, 168, 99, 219, 22, 374, 476, 301, 310, 439, 426, 181, 138, 253, 104, 358,
            185, 359, 348, 53, 215, 450, 175, 345, 211, 255, 216, 472, 183, 90, 63, 406,
            217, 220, 205, 336, 13, 247, 170, 9, 4, 287, 123, 80, 47, 122, 498, 330,
            315, 289, 459, 282, 127, 290, 428, 166, 110, 51, 433, 130, 98, 254, 199, 3,
            350, 502, 76, 83, 362, 32, 331, 121, 207, 270, 390, 222, 419, 299, 114, 29,
            373, 196, 267, 275, 297, 323, 70, 234, 300, 129, 338, 276, 405, 93, 314, 160,
            468, 372, 368, 171, 325, 115, 420, 204, 111, 457, 284, 320, 33, 313, 446, 244,
            487, 67, 444, 223, 64, 46, 238, 364, 108, 269, 25, 423, 106, 280, 116, 440,
            221, 365, 66, 396, 42, 479, 454, 180, 484, 142, 425, 145, 421, 262, 332, 44,
            174, 73, 491, 319, 294, 235, 447, 7, 58, 507, 182, 387, 10, 356, 91, 177,
            112, 206, 229, 257, 422, 5, 340, 85, 176, 409, 432, 416, 20, 190, 261, 194,
            404, 237, 124, 52, 30, 381, 248, 15, 87, 357, 344, 302, 317, 333, 210, 82,
            140, 303, 77, 260, 279, 43, 75, 436, 456, 12, 265, 101, 241, 322, 451, 429,
            471, 307, 312, 197, 408, 467, 236, 59, 259, 133, 167, 225, 162, 8, 321, 369,
            100, 395, 328, 186, 118, 135, 460, 18, 128, 410, 354, 508, 342, 11, 393, 213,
        ];

    }
}