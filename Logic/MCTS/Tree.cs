using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AwesomeOpossum.Logic.MCTS;

public unsafe class Tree
{
    public Node* Nodes;
    private ulong NodesLength;
    private ulong Filled;

    public bool IsEmpty => Filled == 0;

    public Span<Node> NodeSpan => new(Nodes, (int)NodesLength);
    public ref Node RootNode => ref this[0];

    public ref Node this[uint idx] => ref Nodes[idx];

    public Tree()
    {
        Nodes = default;
        NodesLength = 0;
        Filled = 0;
    }

    public void Resize(ulong mb)
    {
        if (Nodes != default)
            NativeMemory.AlignedFree(Nodes);

        NodesLength = mb * 0x100000UL / (ulong)sizeof(Node);
        Nodes = AlignedAllocZeroed<Node>((nuint)NodesLength);
    }

    public void Clear()
    {
        Filled = 0;

        const int numThreads = 2;
        ulong clustersPerThread = NodesLength / numThreads;
        Parallel.For(0, numThreads, new ParallelOptions { MaxDegreeOfParallelism = numThreads }, (i) =>
        {
            ulong start = clustersPerThread * (ulong)i;

            //  Only clear however many remaining clusters there are if this is the last thread
            ulong length = i == numThreads - 1 ? NodesLength - start : clustersPerThread;

            NativeMemory.Clear(&Nodes[start], (nuint)sizeof(Node) * (nuint)length);
        });
    }

    public void Expand(Position pos, uint nodeIndex, uint depth)
    {
        ref Node thisNode = ref this[nodeIndex];


    }

}
