
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
        public const int L1_SIZE = 2048;
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
            772, 589, 766, 11, 929, 26, 955, 525, 127, 978, 0, 660, 703, 959, 794, 412,
            965, 479, 777, 410, 128, 461, 970, 495, 509, 728, 280, 874, 358, 815, 456, 759,
            781, 235, 34, 106, 299, 71, 175, 816, 822, 438, 243, 126, 245, 491, 700, 327,
            977, 727, 659, 326, 105, 46, 13, 714, 174, 678, 143, 411, 544, 375, 981, 182,
            209, 62, 69, 561, 431, 649, 644, 251, 593, 263, 534, 5, 671, 729, 226, 119,
            641, 314, 340, 346, 356, 159, 307, 283, 517, 395, 820, 661, 638, 231, 477, 364,
            483, 48, 246, 60, 351, 890, 7, 587, 702, 832, 324, 553, 947, 377, 260, 742,
            444, 478, 362, 821, 1016, 937, 913, 558, 992, 920, 349, 971, 451, 302, 634, 131,
            44, 942, 121, 646, 633, 697, 533, 831, 354, 701, 475, 801, 270, 595, 939, 710,
            696, 930, 1006, 610, 836, 244, 328, 540, 636, 684, 910, 170, 817, 303, 361, 406,
            215, 337, 752, 332, 467, 1007, 248, 65, 784, 278, 424, 417, 95, 414, 335, 648,
            565, 811, 769, 4, 858, 618, 849, 367, 599, 348, 457, 662, 647, 918, 891, 996,
            844, 3, 552, 753, 685, 492, 92, 548, 282, 319, 906, 757, 85, 516, 148, 234,
            344, 184, 353, 214, 631, 771, 925, 908, 605, 140, 665, 45, 760, 311, 242, 950,
            953, 715, 116, 899, 998, 566, 603, 178, 266, 301, 887, 940, 754, 1002, 851, 2,
            149, 236, 241, 347, 782, 926, 765, 767, 528, 255, 287, 49, 120, 654, 275, 252,
            285, 257, 917, 58, 237, 298, 783, 627, 640, 72, 177, 267, 210, 835, 460, 898,
            90, 969, 207, 390, 54, 889, 707, 505, 941, 221, 256, 79, 1023, 130, 901, 507,
            19, 824, 843, 499, 405, 812, 645, 268, 628, 357, 643, 466, 626, 847, 397, 949,
            42, 355, 265, 622, 496, 1020, 572, 770, 632, 112, 107, 14, 421, 259, 476, 658,
            575, 98, 694, 798, 755, 310, 862, 571, 463, 153, 788, 677, 440, 125, 983, 429,
            682, 420, 439, 229, 151, 482, 43, 113, 746, 369, 842, 1012, 24, 399, 758, 374,
            192, 293, 740, 494, 103, 945, 164, 77, 749, 416, 639, 802, 223, 923, 318, 16,
            455, 828, 372, 97, 206, 339, 922, 637, 567, 883, 819, 101, 570, 907, 919, 768,
            991, 840, 179, 396, 693, 711, 699, 459, 393, 531, 513, 488, 160, 581, 997, 452,
            401, 962, 607, 967, 563, 562, 122, 690, 900, 53, 621, 427, 608, 194, 144, 502,
            732, 761, 670, 995, 443, 52, 966, 990, 437, 1001, 40, 982, 383, 986, 387, 1013,
            162, 403, 630, 59, 294, 905, 582, 750, 23, 385, 744, 879, 453, 676, 373, 462,
            850, 433, 136, 691, 204, 222, 284, 813, 972, 110, 111, 741, 279, 423, 432, 87,
            588, 526, 578, 511, 736, 227, 470, 730, 623, 876, 888, 597, 529, 426, 274, 320,
            345, 579, 145, 1010, 532, 604, 522, 834, 790, 91, 47, 519, 448, 550, 725, 321,
            704, 751, 489, 15, 520, 872, 12, 37, 687, 921, 839, 118, 473, 521, 56, 653,
            408, 378, 325, 827, 38, 1021, 96, 868, 723, 866, 288, 28, 485, 27, 218, 542,
            114, 560, 258, 415, 793, 308, 296, 99, 989, 418, 586, 9, 852, 152, 642, 500,
            380, 787, 590, 31, 386, 141, 545, 481, 825, 290, 389, 490, 830, 368, 963, 70,
            78, 845, 239, 551, 960, 673, 172, 108, 975, 63, 927, 598, 892, 524, 619, 193,
            869, 1008, 799, 1017, 800, 877, 669, 712, 508, 449, 853, 220, 123, 100, 884, 580,
            692, 873, 625, 747, 860, 402, 277, 139, 863, 762, 705, 506, 881, 117, 376, 541,
            903, 667, 994, 523, 199, 446, 155, 450, 973, 530, 855, 312, 400, 323, 606, 295,
            474, 554, 41, 1, 211, 613, 584, 733, 569, 272, 557, 158, 734, 912, 20, 915,
            315, 838, 232, 202, 510, 797, 203, 133, 497, 946, 197, 928, 181, 663, 709, 706,
            50, 165, 289, 980, 664, 795, 219, 276, 585, 737, 841, 807, 94, 76, 191, 212,
            854, 208, 614, 814, 469, 498, 987, 306, 86, 447, 363, 806, 407, 515, 932, 796,
            559, 271, 600, 512, 269, 30, 82, 419, 305, 576, 167, 154, 856, 882, 73, 472,
            51, 609, 238, 583, 343, 976, 666, 865, 333, 657, 1015, 546, 708, 84, 458, 775,
            168, 465, 833, 792, 35, 247, 902, 805, 1005, 739, 228, 720, 21, 1011, 698, 568,
            253, 893, 425, 187, 104, 1022, 867, 914, 365, 549, 846, 688, 304, 924, 36, 297,
            330, 381, 1003, 895, 233, 721, 503, 171, 471, 173, 224, 611, 147, 988, 176, 861,
            300, 480, 1004, 536, 938, 1019, 716, 504, 829, 680, 81, 67, 985, 487, 803, 18,
            80, 764, 157, 612, 857, 535, 33, 484, 261, 808, 780, 655, 804, 102, 911, 823,
            717, 756, 616, 719, 735, 886, 933, 55, 786, 871, 189, 870, 198, 594, 968, 961,
            556, 1014, 281, 75, 672, 336, 957, 129, 388, 291, 810, 366, 592, 391, 543, 724,
            596, 954, 93, 142, 88, 254, 309, 436, 668, 135, 413, 57, 791, 826, 620, 205,
            8, 186, 341, 124, 434, 848, 773, 555, 262, 894, 650, 635, 334, 195, 944, 384,
            722, 454, 29, 774, 89, 329, 464, 948, 398, 404, 624, 1009, 249, 573, 979, 74,
            679, 651, 225, 875, 115, 909, 951, 683, 943, 392, 779, 674, 201, 731, 217, 359,
            896, 880, 66, 718, 264, 776, 146, 837, 240, 993, 748, 442, 564, 213, 713, 83,
            371, 150, 547, 32, 518, 22, 200, 885, 68, 409, 486, 1000, 577, 185, 370, 17,
            331, 180, 342, 338, 859, 190, 818, 964, 441, 322, 675, 286, 514, 360, 652, 161,
            156, 615, 935, 132, 6, 864, 897, 138, 916, 394, 313, 382, 109, 601, 188, 422,
            379, 743, 430, 602, 931, 25, 617, 591, 681, 958, 166, 196, 656, 539, 878, 1018,
            64, 629, 789, 745, 39, 785, 689, 999, 538, 183, 10, 809, 952, 984, 316, 273,
            468, 134, 936, 163, 61, 428, 501, 137, 230, 527, 778, 726, 292, 537, 974, 435,
            574, 738, 250, 445, 686, 317, 216, 169, 493, 956, 904, 352, 695, 763, 350, 934,
        ];

    }
}