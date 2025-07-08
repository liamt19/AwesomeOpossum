using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace AwesomeOpossum.Logic.Evaluation
{
    public unsafe struct Accumulator
    {
        public const int ByteSize = ValueNetwork.L1_SIZE * sizeof(short);

        public readonly short* White;
        public readonly short* Black;

        public Accumulator()
        {
            White = AlignedAllocZeroed<short>(ValueNetwork.L1_SIZE);
            Black = AlignedAllocZeroed<short>(ValueNetwork.L1_SIZE);
        }

        public Vector256<short>* this[int perspective] => (perspective == Color.White) ? (Vector256<short>*)White : (Vector256<short>*)Black;

        public void Dispose()
        {
            NativeMemory.AlignedFree(White);
            NativeMemory.AlignedFree(Black);
        }
    }

    public unsafe struct PolicyAccumulator
    {
        public const int ByteSize = PolicyNetwork.L1_SIZE * sizeof(short);

        public readonly short* White;
        public readonly short* Black;

        public PolicyAccumulator()
        {
            White = AlignedAllocZeroed<short>(PolicyNetwork.L1_SIZE);
            Black = AlignedAllocZeroed<short>(PolicyNetwork.L1_SIZE);
        }

        public Vector256<short>* this[int perspective] => (perspective == Color.White) ? (Vector256<short>*)White : (Vector256<short>*)Black;

        public void Dispose()
        {
            NativeMemory.AlignedFree(White);
            NativeMemory.AlignedFree(Black);
        }
    }

}