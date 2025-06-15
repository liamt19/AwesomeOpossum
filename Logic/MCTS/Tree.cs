using AwesomeOpossum.Logic.Evaluation;
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
    public TranspositionTable TT;

    public Node* Nodes;
    private ulong NodesLength;
    private ulong Filled;

    public bool IsEmpty => Filled == 0;

    public uint FillLevel => (uint)((1000 * Filled) / NodesLength);
    public Span<Node> NodeSpan => new(Nodes, (int)NodesLength);
    public ref Node RootNode => ref this[0];

    public Move BestRootMove => BestRootAction.bestMove;
    public (Move bestMove, float q) BestRootAction { get { var (_, move, q) = GetBestAction(new(0, 0)); return (move, q); } }

    private uint RealIndex(in NodePointer ptr) => ptr.Index;

    public ref Node this[uint idx] => ref Nodes[idx];
    public ref Node this[in NodePointer ptr] => ref Nodes[RealIndex(ptr)];


    public Tree(int mb)
    {
        Nodes = default;
        NodesLength = 0;
        Filled = 0;

        TT = new();

        Resize(mb);
    }

    public void Resize(int numMB)
    {
        if (Nodes != default)
            NativeMemory.AlignedFree(Nodes);

        ulong mb = (ulong)numMB * 1024 * 1024;

        ulong div = (ulong)sizeof(TTEntry) + ((ulong)sizeof(Node) * 4);
        ulong entriesToAlloc = mb / div;
        ulong nodesToAlloc = entriesToAlloc * 4;

        NodesLength = nodesToAlloc;
        Nodes = AlignedAllocZeroedHuge<Node>((nuint)NodesLength);

        TT.Resize(entriesToAlloc);
    }

    public void Clear()
    {
        Filled = 0;

        int numThreads = SearchOptions.Threads;
        ulong clustersPerThread = NodesLength / (ulong)numThreads;
        Parallel.For(0, numThreads, new ParallelOptions { MaxDegreeOfParallelism = numThreads }, (i) =>
        {
            ulong start = clustersPerThread * (ulong)i;

            //  Only clear however many remaining clusters there are if this is the last thread
            ulong length = i == numThreads - 1 ? NodesLength - start : clustersPerThread;

            NativeMemory.Clear(&Nodes[start], (nuint)sizeof(Node) * (nuint)length);
        });

        TT.Clear();
    }

    public bool ReserveNodes(uint additional, out NodePointer newFilled)
    {
        uint newIdx = (uint)Interlocked.Add(ref Filled, additional) - additional;
        newFilled = new(newIdx, 0);
        return newIdx < NodesLength;
    }

    public Span<Node> ChildrenOf(in NodePointer parent) => ChildrenOf(this[parent]);
    public Span<Node> ChildrenOf(uint parent) => ChildrenOf(this[parent]);
    public Span<Node> ChildrenOf(in Node parentNode)
    {
        Debug.Assert(parentNode.HasChildren);

        var child = parentNode.FirstChild;
        fixed (Node* p = &this[child])
            return new Span<Node>(p, parentNode.NumChildren);
    }


    public void PushRoot(Position pos)
    {
        Debug.Assert(Filled == 0);

        NodePointer root = new(0, 0);
        ReserveNodes(1, out _);
        this[0].Set(Move.Null, 0.0f);
        Expand(pos, root, 1);
        this[0].Update(1.0f - Iteration.GetNodeValue(pos, root));
    }


    public bool Expand(Position pos, NodePointer nodeIndex, uint depth)
    {
        ref Node thisNode = ref this[nodeIndex];

        ScoredMove* moves = stackalloc ScoredMove[256];
        uint count = (uint)pos.GenLegal(moves);

        PolicyNetwork.RefreshPolicyAccumulator(pos);

        float maxScore = float.MinValue;
        for (uint i = 0; i < count; i++)
        {
            moves[i].Score = SearchUtils.PolicyForMove(pos, moves[i].Move);
            maxScore = MathF.Max(maxScore, moves[i].Score);
        }

        if (!ReserveNodes(count, out NodePointer newPtr))
            return false;

        var pst = SearchUtils.GetTemperatureAdjustment((int)depth, this[nodeIndex].QValue);

        float total = 0.0f;
        for (uint i = 0; i < count; i++)
        {
            moves[i].Score = float.Exp((moves[i].Score - maxScore) / pst);
            total += moves[i].Score;
        }

        for (uint i = 0; i < count; i++)
        {
            this[newPtr + i].Set(moves[i].Move, (moves[i].Score / total));
        }

        thisNode.NumChildren = (byte)count;
        thisNode.FirstChild = newPtr;

        return true;
    }

    public void PropagateMateScores(ref Node parent, in NodeState childState)
    {
        if (childState.Kind == NodeStateKind.Unterminated || childState.Kind == NodeStateKind.Draw)
            return;

        if (childState.Kind == NodeStateKind.Loss)
        {
            parent.State = NodeState.MakeWin((byte)(childState.Length + 1));
            return;
        }

        bool isLosing = true;
        byte maxWinLen = childState.Length;
        var firstChild = parent.FirstChild.Index;
        for (uint i = firstChild; i < firstChild + parent.NumChildren; i++)
        {
            var s = this[i].State;
            if (s.Kind == NodeStateKind.Win)
            {
                maxWinLen = Math.Max(maxWinLen, s.Length);
            }
            else
            {
                isLosing = false;
                break;
            }
        }

        if (isLosing)
            parent.State = NodeState.MakeLoss((byte)(maxWinLen + 1));
    }

    /// <summary>
    /// Lambda to be called on each child node, returning a float score
    /// </summary>
    public delegate float ChildSelector(in Node node);
    
    /// <summary>
    ///  Returns the index of the child within the tree
    /// </summary>
    public NodePointer GetBestChildFunc(NodePointer nodeIndex, ChildSelector F)
    {
        uint bestIdx = int.MaxValue;
        float bestScore = float.MinValue;

        ref var thisNode = ref this[nodeIndex];
        var children = ChildrenOf(nodeIndex);
        for (uint i = 0; i < children.Length; i++)
        {
            var score = F(children[(int)i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        return thisNode.FirstChild + bestIdx;
    }

    public (NodePointer idx, Move move, float q) GetBestAction(NodePointer nodeIndex)
    {
        NodePointer idx = GetBestChild(nodeIndex);
        Move move = this[idx].Move;
        float q = this[idx].QValue;
        return (idx, move, q);
    }

    public NodePointer GetBestChild(NodePointer nodeIndex)
    {
        return GetBestChildFunc(nodeIndex, (in Node n) => {
            if (n.Visits == 0)
                return float.NegativeInfinity;

            return n.State switch
            {
                (NodeStateKind.Loss, _) => 1.0f + n.State.Length,
                (NodeStateKind.Win, _) => n.State.Length - MaxPly,
                (NodeStateKind.Draw, _) => 0.5f,
                _ => n.QValue
            };
        });
    }

    public (List<Move> list, float score) GetPV(uint depth)
    {
        List<Move> list = [];

        bool mate = this[0].IsTerminal;
        NodePointer root = new(0, 0);

        var (idx, move, q) = GetBestAction(root);
        float score = q;
        if (this[idx].IsValid)
        {
            score = this[idx].State switch
            {
                (NodeStateKind.Loss, _) => ScorePVWin,
                (NodeStateKind.Draw, _) => 0.5f,
                (NodeStateKind.Win, _) => ScorePVLoss,
                _ => q
            };
        }
        list.Add(move);

        while ((mate || depth > 0) && this[idx].IsValid && this[idx].HasChildren)
        {
            (idx, move, q) = GetBestAction(idx);
            list.Add(move);
            depth--;
        }

        return (list, score);
    }

    public void Debug_GetRootMoves(Position pos)
    {
        var children = ChildrenOf(RootNode);

        foreach (var child in children)
        {
            pos.MakeMove(child.Move);
            var h = pos.Hash;
            pos.UnmakeMove(child.Move);

            bool found = TT.Probe(h, out TTEntry* tte);
            Log($"{child} -> {(found ? tte->Key : string.Empty)} {(found ? tte->Q : string.Empty)}");
        }
    }
}
