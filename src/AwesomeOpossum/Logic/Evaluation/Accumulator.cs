using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace AwesomeOpossum.Logic.Evaluation
{
    public unsafe struct Accumulator
    {
        public readonly short* White;
        public readonly short* Black;

        public Accumulator()
        {
            White = AlignedAllocZeroed<short>(ValueNetwork.L1_SIZE * 2);
            Black = &White[ValueNetwork.L1_SIZE];
        }

        public Vector256<short>* this[int perspective] => (perspective == Color.White) ? (Vector256<short>*)White : (Vector256<short>*)Black;
        public void Dispose() => NativeMemory.AlignedFree(White);
    }

    public unsafe struct PolicyAccumulator
    {
        public readonly short* Accumulation;

        public PolicyAccumulator() => Accumulation = AlignedAllocZeroed<short>(PolicyNetwork.L1_SIZE);
        public void Dispose() => NativeMemory.AlignedFree(Accumulation);
    }

}