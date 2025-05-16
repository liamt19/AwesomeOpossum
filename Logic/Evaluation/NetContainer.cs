
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

using static AwesomeOpossum.Logic.NN.Bucketed768;

namespace AwesomeOpossum.Logic.NN
{
    public unsafe struct NetContainer<T, W>
    {
        public readonly T* FTWeights;
        public readonly T* FTBiases;
        public readonly W* L1Weights;
        public readonly W* L1Biases;

        public NetContainer()
        {
            FTWeights = (T*)AlignedAllocZeroed((nuint)sizeof(T) * INPUT_SIZE * L1_SIZE * INPUT_BUCKETS);
            FTBiases  = (T*)AlignedAllocZeroed((nuint)sizeof(T) * L1_SIZE);

            L1Weights = (W*)AlignedAllocZeroed((nuint)sizeof(W) * L1_SIZE * 2 * OUTPUT_BUCKETS);
            L1Biases  = (W*)AlignedAllocZeroed((nuint)sizeof(W) * OUTPUT_BUCKETS);
        }
    }
}
