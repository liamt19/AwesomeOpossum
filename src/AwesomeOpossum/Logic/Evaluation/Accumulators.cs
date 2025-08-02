using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace AwesomeOpossum.Logic.Evaluation
{
    public unsafe struct ValueAccumulator
    {
        public readonly short* Accumulation;

        public ValueAccumulator() => Accumulation = AlignedAllocZeroed<short>(ValueNetwork.L1_SIZE);
        public void Dispose() => NativeMemory.AlignedFree(Accumulation);
    }

    public unsafe struct PolicyAccumulator
    {
        public readonly short* Accumulation;

        public PolicyAccumulator() => Accumulation = AlignedAllocZeroed<short>(PolicyNetwork.L1_SIZE);
        public void Dispose() => NativeMemory.AlignedFree(Accumulation);
    }

}