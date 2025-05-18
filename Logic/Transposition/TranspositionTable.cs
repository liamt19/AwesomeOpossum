using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AwesomeOpossum.Logic.Transposition
{
    public unsafe class TranspositionTable
    {
        private TTEntry* Entries;
        public ulong EntryCount { get; private set; }

        public TranspositionTable(int mb)
        {
            Initialize(mb);
        }

        public unsafe void Initialize(int mb)
        {
            if (Entries != null)
                NativeMemory.AlignedFree(Entries);

            EntryCount = (ulong)mb * 0x100000UL / (ulong)sizeof(TTEntry);
            nuint allocSize = ((nuint)sizeof(TTEntry) * (nuint)EntryCount);

            //  On Linux, also inform the OS that we want it to use large pages
            Entries = AlignedAllocZeroed<TTEntry>((nuint)EntryCount, (1024 * 1024));
            AdviseHugePage(Entries, allocSize);
        }


        public void Clear()
        {
            int numThreads = SearchOptions.Threads;
            ulong EntriesPerThread = (EntryCount / (ulong)numThreads);

            Parallel.For(0, numThreads, new ParallelOptions { MaxDegreeOfParallelism = numThreads }, (int i) =>
            {
                ulong start = EntriesPerThread * (ulong)i;

                //  Only clear however many remaining Entries there are if this is the last thread
                ulong length = (i == numThreads - 1) ? EntryCount - start : EntriesPerThread;

                NativeMemory.Clear(&Entries[start], ((nuint)sizeof(TTEntry) * (nuint)length));
            });
        }


        [MethodImpl(Inline)]
        private TTEntry* ProbeHash(ulong hash) => Entries + ((ulong)(((UInt128)hash * (UInt128)EntryCount) >> 64));

        public bool Probe(ulong hash, out TTEntry* tte)
        {
            tte = ProbeHash(hash);
            var key = (ushort)hash;

            return (tte->Key == key);
        }

        public void Store(ulong hash, float u)
        {
            var p = ProbeHash(hash);
            p->Replace(hash, u);
        }
    }
}
