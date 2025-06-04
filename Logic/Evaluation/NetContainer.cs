
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
#pragma warning disable CA1715 // Identifiers should have correct prefix

namespace AwesomeOpossum.Logic.Evaluation;

public unsafe struct ValueNetContainer<T, W>
{
    public readonly T* FTWeights;
    public readonly T* FTBiases;
    public readonly W* L1Weights;
    public readonly W* L1Biases;

    public ValueNetContainer()
    {
        FTWeights = (T*)AlignedAllocZeroed((nuint)sizeof(T) * ValueNetwork.N_FTW);
        FTBiases  = (T*)AlignedAllocZeroed((nuint)sizeof(T) * ValueNetwork.N_FTB);
        L1Weights = (W*)AlignedAllocZeroed((nuint)sizeof(W) * ValueNetwork.N_L1W);
        L1Biases  = (W*)AlignedAllocZeroed((nuint)sizeof(W) * ValueNetwork.N_L1B);
    }
}

public unsafe struct PolicyNetContainer<T, W>
{
    public readonly T* FTWeights;
    public readonly T* FTBiases;
    public readonly W* L1Weights;
    public readonly W* L1Biases;

    public PolicyNetContainer()
    {
        FTWeights = (T*)AlignedAllocZeroed((nuint)sizeof(T) * PolicyNetwork.N_FTW);
        FTBiases  = (T*)AlignedAllocZeroed((nuint)sizeof(T) * PolicyNetwork.N_FTB);
        L1Weights = (W*)AlignedAllocZeroed((nuint)sizeof(W) * PolicyNetwork.N_L1W);
        L1Biases  = (W*)AlignedAllocZeroed((nuint)sizeof(W) * PolicyNetwork.N_L1B);
    }
}
