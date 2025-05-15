using System;
using System.Collections.Generic;
using System.Drawing;
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

    public Tree(int mb)
    {
        Nodes = default;
        NodesLength = Filled = 0;

        Resize(mb);
    }

    public void Resize(int mb)
    {
        if (Nodes != default)
            NativeMemory.AlignedFree(Nodes);

        NodesLength = (ulong)mb * 0x100000UL / (ulong)sizeof(Node);
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

    public uint ReserveNodes(uint additional)
    {
        var newFilled = Interlocked.Add(ref Filled, additional);
        return (uint)newFilled;
    }

    public Span<Node> ChildrenOf(uint parent) => ChildrenOf(this[parent]);
    public Span<Node> ChildrenOf(in Node parentNode)
    {
        var child = parentNode.FirstChild;
        Debug.Assert(parentNode.HasChildren);
        Debug.Assert(child != 0);
        Debug.Assert(child < NodesLength);

        return new Span<Node>(&Nodes[child], parentNode.NumChildren);
    }

    public void Expand(Position pos, uint nodeIndex, uint depth)
    {
        ref Node thisNode = ref this[nodeIndex];

        ScoredMove* moves = stackalloc ScoredMove[256];
        uint count = (uint)pos.GenLegal(moves);

        float maxScore = float.MinValue;
        for (uint i = 0; i < count; i++)
        {
            var p = SearchUtils.PolicyForMove(pos, moves[i].Move);
            moves[i].Score = p;
            maxScore = MathF.Max(maxScore, p);
        }

        var newPtr = ReserveNodes(count);
        var pst = SearchUtils.GetPST(depth, this[nodeIndex].QValue);

        float total = 0.0f;
        for (uint i = 0; i < count; i++)
        {
            var p = moves[i].Score;
            moves[i].Score = float.Exp((moves[i].Score - maxScore) / pst);
            total += moves[i].Score;
        }

        for (uint i = 0; i < count; i++)
        {
            var p = moves[i].Score / total;
            var ptr = newPtr + i;

            this[ptr].Set(moves[i].Move, p);
        }

        thisNode.NumChildren = (byte)count;
        thisNode.FirstChild = newPtr;
    }

    public delegate float ChildSelector(in Node node);
    public uint GetBestChild(uint nodeIndex, ChildSelector F)
    {
        int bestIdx = int.MaxValue;
        float bestScore = float.MinValue;

        ref var thisNode = ref this[nodeIndex];
        var children = ChildrenOf(nodeIndex);
        for (int i = 0; i < children.Length; i++)
        {
            var score = F(children[i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        return (uint)bestIdx;
    }

}
