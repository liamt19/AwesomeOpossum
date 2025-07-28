
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace AwesomeOpossum.Logic.Evaluation;

public readonly unsafe struct ValueNetContainer<T, W, U>
{
    private const int I = ValueNetwork.INPUT_BUCKETS;
    private const int O = ValueNetwork.OUTPUT_BUCKETS;
    private const int L1 = ValueNetwork.L1_SIZE;
    private const int L2 = ValueNetwork.L2_SIZE;
    private const int L3 = ValueNetwork.L3_SIZE;

    public readonly T* FTWeights;
    public readonly T* FTBiases;
    public readonly W** L1Weights;
    public readonly U** L1Biases;
    public readonly U** L2Weights;
    public readonly U** L2Biases;
    public readonly U** L3Weights;
    public readonly U* L3Biases;

    public ValueNetContainer()
    {
        FTWeights = (T*)AlignedAllocZeroed((nuint)sizeof(T) * 768 * L1 * I);
        FTBiases  = (T*)AlignedAllocZeroed((nuint)sizeof(T) * L1);

        var l1w = AlignedAllocZeroed<W>(O * L1 * L2);
        var l1b = AlignedAllocZeroed<U>(O * L2);
        var l2w = AlignedAllocZeroed<U>(O * L2 * L3);
        var l2b = AlignedAllocZeroed<U>(O * L3);
        var l3w = AlignedAllocZeroed<U>(O * L3);
        var l3b = AlignedAllocZeroed<U>(O);

        L1Weights = (W**)AlignedAllocZeroed((nuint)sizeof(W*) * O);
        L1Biases  = (U**)AlignedAllocZeroed((nuint)sizeof(U*) * O);
        L2Weights = (U**)AlignedAllocZeroed((nuint)sizeof(U*) * O);
        L2Biases  = (U**)AlignedAllocZeroed((nuint)sizeof(U*) * O);
        L3Weights = (U**)AlignedAllocZeroed((nuint)sizeof(U*) * O);
        L3Biases  = (U* )AlignedAllocZeroed((nuint)sizeof(U ) * O);

        for (int i = 0; i < O; i++)
        {
            L1Weights[i] = &l1w[i * L1 * L2];
            L1Biases[i]  = &l1b[i * L2];
            L2Weights[i] = &l2w[i * L2 * L3];
            L2Biases[i]  = &l2b[i * L3];
            L3Weights[i] = &l3w[i * L3];

            //L1Weights[i] = (W*)AlignedAllocZeroed((nuint)sizeof(W) * (L1 * L2));
            //L1Biases[i]  = (U*)AlignedAllocZeroed((nuint)sizeof(U) * (L2));
            //L2Weights[i] = (U*)AlignedAllocZeroed((nuint)sizeof(U) * (L2 * L3));
            //L2Biases[i]  = (U*)AlignedAllocZeroed((nuint)sizeof(U) * (L3));
            //L3Weights[i] = (U*)AlignedAllocZeroed((nuint)sizeof(U) * (L3));
        }
    }
}

public readonly unsafe struct PolicyNetContainer<T, W, U>
{
    public readonly T* FTWeights;
    public readonly T* FTBiases;
    public readonly W* L1Weights;
    public readonly U* L1Biases;

    public PolicyNetContainer()
    {
        FTWeights = (T*)AlignedAllocZeroed((nuint)sizeof(T) * PolicyNetwork.N_FTW);
        FTBiases  = (T*)AlignedAllocZeroed((nuint)sizeof(T) * PolicyNetwork.N_FTB);
        L1Weights = (W*)AlignedAllocZeroed((nuint)sizeof(W) * PolicyNetwork.N_L1W);
        L1Biases  = (U*)AlignedAllocZeroed((nuint)sizeof(U) * PolicyNetwork.N_L1B);
    }

    public void TransposeL1W()
    {
        var rowLen = PolicyNetwork.L1_PAIRS;
        var colLen = PolicyNetwork.N_L1W / rowLen;
        var temp = new W[PolicyNetwork.N_L1W];

        fixed (W* p = temp)
            Unsafe.CopyBlock(p, L1Weights, (uint)(sizeof(W) * PolicyNetwork.N_L1W));

        for (int r = 0; r < rowLen; r++)
        {
            W* slice = L1Weights + (r * colLen);

            for (int c = 0; c < colLen; c++)
            {
                slice[c] = temp[(rowLen * c) + r];
            }
        }
    }
}
