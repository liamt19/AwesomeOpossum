
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
        public const int L1_SIZE = 1536;
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
        public static readonly ulong[] NNZCounts = new ulong[L1_PAIRS];
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
            435, 533, 80, 747, 27, 395, 267, 498, 637, 158, 550, 594, 412, 670, 405, 539,
            284, 174, 56, 640, 631, 140, 36, 650, 495, 376, 567, 9, 501, 326, 520, 112,
            572, 767, 350, 397, 38, 184, 672, 659, 414, 630, 449, 743, 474, 573, 148, 107,
            276, 51, 48, 359, 674, 581, 132, 216, 236, 345, 481, 347, 214, 301, 429, 300,
            513, 451, 478, 467, 703, 303, 689, 337, 186, 707, 143, 434, 299, 706, 473, 676,
            270, 352, 341, 665, 751, 205, 121, 277, 225, 576, 249, 137, 351, 273, 590, 226,
            448, 403, 162, 717, 420, 313, 437, 68, 271, 540, 161, 344, 724, 658, 134, 10,
            698, 323, 263, 240, 667, 722, 131, 571, 241, 287, 428, 371, 538, 95, 496, 99,
            489, 173, 627, 618, 92, 316, 209, 84, 96, 275, 154, 222, 55, 425, 394, 63,
            515, 52, 191, 679, 735, 1, 733, 256, 29, 516, 44, 452, 622, 546, 402, 591,
            453, 407, 47, 282, 465, 302, 442, 519, 399, 436, 245, 400, 128, 179, 268, 332,
            295, 441, 440, 484, 377, 404, 283, 763, 171, 91, 349, 293, 565, 231, 21, 558,
            201, 426, 202, 336, 22, 406, 11, 485, 76, 142, 578, 765, 742, 13, 523, 23,
            651, 686, 508, 339, 67, 475, 487, 615, 646, 738, 488, 409, 529, 741, 375, 601,
            194, 584, 737, 45, 379, 574, 72, 16, 623, 709, 711, 237, 160, 445, 643, 15,
            309, 177, 547, 331, 408, 687, 354, 183, 353, 227, 343, 25, 356, 446, 517, 129,
            691, 71, 381, 549, 461, 126, 391, 362, 233, 396, 386, 675, 510, 120, 471, 14,
            32, 657, 370, 166, 155, 159, 721, 723, 492, 6, 556, 105, 662, 215, 59, 117,
            732, 457, 753, 401, 73, 42, 248, 536, 522, 242, 645, 530, 90, 483, 740, 532,
            89, 252, 230, 348, 514, 598, 712, 223, 725, 28, 260, 626, 544, 624, 306, 433,
            123, 734, 320, 324, 40, 176, 422, 136, 666, 701, 499, 766, 411, 638, 562, 115,
            644, 217, 561, 750, 697, 470, 12, 169, 688, 491, 94, 50, 198, 103, 649, 204,
            259, 652, 167, 545, 726, 557, 378, 164, 460, 715, 127, 266, 265, 170, 196, 610,
            57, 566, 553, 439, 66, 690, 384, 617, 447, 330, 61, 568, 653, 541, 78, 456,
            152, 31, 729, 294, 604, 75, 507, 393, 250, 144, 705, 98, 702, 106, 212, 477,
            274, 385, 65, 79, 190, 506, 602, 199, 218, 87, 577, 288, 261, 4, 81, 163,
            317, 104, 133, 366, 228, 469, 575, 296, 383, 314, 64, 221, 418, 62, 625, 509,
            669, 500, 663, 739, 720, 369, 744, 305, 30, 438, 486, 203, 197, 472, 390, 17,
            88, 528, 512, 200, 219, 156, 361, 251, 497, 647, 413, 168, 600, 20, 636, 554,
            325, 588, 229, 340, 505, 633, 311, 521, 531, 149, 85, 476, 592, 684, 39, 360,
            8, 635, 178, 629, 760, 432, 632, 119, 310, 33, 334, 757, 716, 582, 279, 527,
            535, 308, 297, 247, 551, 97, 82, 338, 278, 605, 146, 761, 587, 559, 234, 648,
            0, 34, 111, 710, 762, 187, 165, 357, 668, 543, 502, 759, 548, 599, 125, 454,
            189, 83, 182, 443, 335, 108, 494, 524, 180, 613, 363, 482, 3, 346, 86, 642,
            537, 628, 70, 285, 208, 141, 318, 458, 704, 224, 213, 388, 290, 304, 392, 37,
            272, 730, 151, 367, 611, 138, 685, 552, 431, 595, 746, 754, 253, 731, 417, 525,
            585, 43, 493, 24, 745, 41, 135, 69, 58, 109, 444, 113, 660, 53, 243, 281,
            269, 244, 641, 518, 713, 130, 714, 542, 264, 455, 609, 364, 511, 427, 606, 656,
            608, 315, 195, 693, 150, 124, 708, 54, 569, 312, 258, 421, 639, 280, 389, 756,
            102, 593, 74, 583, 116, 172, 692, 110, 358, 616, 207, 60, 7, 372, 700, 122,
            479, 328, 145, 654, 655, 430, 380, 614, 175, 246, 699, 695, 387, 333, 257, 307,
            292, 374, 678, 748, 462, 589, 77, 46, 677, 621, 490, 570, 185, 423, 193, 463,
            503, 612, 342, 464, 254, 586, 752, 596, 321, 235, 232, 368, 419, 694, 118, 101,
            727, 206, 298, 755, 211, 415, 188, 239, 579, 719, 157, 619, 620, 450, 416, 683,
            365, 114, 555, 534, 526, 329, 459, 504, 210, 19, 480, 49, 597, 319, 373, 286,
            682, 18, 718, 382, 736, 607, 560, 220, 153, 673, 100, 661, 468, 580, 664, 758,
            398, 93, 749, 192, 181, 424, 728, 147, 327, 410, 466, 35, 238, 634, 671, 764,
            355, 696, 322, 289, 255, 5, 603, 563, 26, 262, 291, 680, 2, 681, 139, 564,
        ];

    }
}