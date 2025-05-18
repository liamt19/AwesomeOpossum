using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static AwesomeOpossum.Logic.Transposition.TranspositionTable;


namespace AwesomeOpossum.Logic.Transposition
{
    public struct TTEntry
    {
        public ushort Key;
        private ushort _Q;

        public readonly float Q => _Q / (float)ushort.MaxValue;

        [MethodImpl(Inline)]
        public void SetQ(float u) => _Q = (ushort)(u * (float)ushort.MaxValue);


        [MethodImpl(Inline)]
        public void Replace(ulong hash, float u)
        {
            Key = (ushort)hash;
            SetQ(u);
        }

        public override string ToString() => $"{Key} {Q}";
    }
}
