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

        public fixed bool NeedsRefresh[2];
        public fixed bool Computed[2];
        public NetworkUpdate Update;

        public Accumulator()
        {
            White = AlignedAllocZeroed<short>(ValueNetwork.L1_SIZE);
            Black = AlignedAllocZeroed<short>(ValueNetwork.L1_SIZE);

            NeedsRefresh[Color.White] = NeedsRefresh[Color.Black] = true;
            Computed[Color.White] = Computed[Color.Black] = false;
        }

        public Vector256<short>* this[int perspective] => (perspective == Color.White) ? (Vector256<short>*)White : (Vector256<short>*)Black;

        [MethodImpl(Inline)]
        public void CopyTo(Accumulator* target)
        {
            Unsafe.CopyBlock(target->White, White, ByteSize);
            Unsafe.CopyBlock(target->Black, Black, ByteSize);

            target->NeedsRefresh[0] = NeedsRefresh[0];
            target->NeedsRefresh[1] = NeedsRefresh[1];

        }

        [MethodImpl(Inline)]
        public void CopyTo(ref Accumulator target, int perspective)
        {
            Unsafe.CopyBlock(target[perspective], this[perspective], ByteSize);
            target.NeedsRefresh[perspective] = NeedsRefresh[perspective];
        }

        public void ResetWithBiases(short* biases, uint byteCount)
        {
            Unsafe.CopyBlock(White, biases, byteCount);
            Unsafe.CopyBlock(Black, biases, byteCount);
        }

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

    public unsafe struct BucketCache
    {
        /// <summary>
        /// 2 Boards, 1 for each perspective
        /// </summary>
        public Bitboard[] Boards = new Bitboard[ColorNB];
        public Accumulator Accumulator = new Accumulator();

        public BucketCache() { }
    }


    public unsafe struct PerspectiveUpdate
    {
        public fixed int Adds[2];
        public fixed int Subs[2];
        public int AddCnt = 0;
        public int SubCnt = 0;

        public PerspectiveUpdate() { }

        [MethodImpl(Inline)]
        public void Clear()
        {
            AddCnt = SubCnt = 0;
        }

        [MethodImpl(Inline)]
        public void PushSub(int sub1)
        {
            Subs[SubCnt++] = sub1;
        }

        [MethodImpl(Inline)]
        public void PushSubAdd(int sub1, int add1)
        {
            Subs[SubCnt++] = sub1;
            Adds[AddCnt++] = add1;
        }

        [MethodImpl(Inline)]
        public void PushSubSubAdd(int sub1, int sub2, int add1)
        {
            Subs[SubCnt++] = sub1;
            Subs[SubCnt++] = sub2;
            Adds[AddCnt++] = add1;
        }

        [MethodImpl(Inline)]
        public void PushSubSubAddAdd(int sub1, int sub2, int add1, int add2)
        {
            Subs[SubCnt++] = sub1;
            Subs[SubCnt++] = sub2;
            Adds[AddCnt++] = add1;
            Adds[AddCnt++] = add2;
        }
    }

    [InlineArray(2)]
    public unsafe struct NetworkUpdate
    {
        public PerspectiveUpdate _Update;
    }

}