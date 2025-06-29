
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace AwesomeOpossum.Logic.Evaluation;

public static unsafe partial class ValueNetwork
{

    //  Here lies the results of 1.5 hours of attempts at getting a source generator to do this for me

    [UnmanagedCallersOnly]
    public static int EvaluateImpl64(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 64;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl128(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 128;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl256(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 256;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl512(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 512;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl768(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 768;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl1024(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 1024;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl1280(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 1280;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl1536(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 1536;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl1792(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 1792;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

    [UnmanagedCallersOnly]
    public static int EvaluateImpl2048(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
    {
        const int L1_SIZE = 2048;

        Vector256<short> maxVec = Vector256.Create((short)QA);
        Vector256<short> zeroVec = Vector256<short>.Zero;
        Vector256<int> sum = Vector256<int>.Zero;

        int SimdChunks = L1_SIZE / Vector256<short>.Count;

        var ourData = (Vector256<short>*)stmData;
        var theirData = (Vector256<short>*)ntmData;
        var ourWeights = (Vector256<short>*)(&l1Weights[0]);
        var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
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

        int output = Vector256.Sum(sum);
        output = (output / QA) + l1Bias;

        return output * OUTPUT_SCALE / (QA * QB);
    }

}
