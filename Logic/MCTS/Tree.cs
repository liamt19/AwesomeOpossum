
using AwesomeOpossum.Logic.Evaluation;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AwesomeOpossum.Logic.MCTS;

[InlineArray(2)] public struct FillBuffer { ulong _; }

public unsafe class Tree
{
    public TranspositionTable TT;

    public Node* Nodes;
    private ulong TotalNodes;

    public ulong ExpandedNodes;
    public ulong FetchedNodes;

    private ulong NodesPerHalf;
    private int CurrentHalf;

    private ulong CurrentFill => Filled[CurrentHalf];
    public FillBuffer Filled;

    public bool IsEmpty => Filled[CurrentHalf] == 0;

    public uint FillLevel => (uint)((1000 * CurrentFill) / NodesPerHalf);
    public Span<Node> NodeSpan => new(Nodes, (int)TotalNodes);
    public ref Node RootNode => ref this[0];
    public NodePointer RootNodePointer => new(0, CurrentHalf);

    public Span<Node> DBG_ROOT0 => new(&Nodes[0], 21);
    public Span<Node> DBG_ROOT1 => new(&Nodes[NodesPerHalf], 21);

    public Move BestRootMove => BestRootAction.bestMove;
    public (Move bestMove, float q) BestRootAction { get { var (_, move, q) = GetBestAction(RootNodePointer); return (move, q); } }

    private uint RealIndex(uint idx) => ((uint)((uint)CurrentHalf * NodesPerHalf) + idx);
    private uint RealIndex(in NodePointer ptr) => ((uint)(ptr.Half * NodesPerHalf) + ptr.Index);

    public ref Node this[uint idx] => ref Nodes[RealIndex(idx)];
    public ref Node this[in NodePointer ptr] => ref Nodes[RealIndex(ptr)];

    public Tree(int mb)
    {
        Nodes = default;
        TotalNodes = 0;
        Filled[0] = Filled[1] = 0;
        CurrentHalf = 0;

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

        TotalNodes = nodesToAlloc;
        Nodes = AlignedAllocZeroedHuge<Node>((nuint)TotalNodes);
        NodesPerHalf = nodesToAlloc / 2;

        TT.Resize(entriesToAlloc);

        Clear();
    }


    public void Clear()
    {
        Filled[0] = Filled[1] = 0;
        CurrentHalf = 0;

        ExpandedNodes = FetchedNodes = 0;

        int numThreads = SearchOptions.Threads;
        ulong clustersPerThread = TotalNodes / (ulong)numThreads;
        Debug.Assert(clustersPerThread < int.MaxValue);
        Parallel.For(0, numThreads, new ParallelOptions { MaxDegreeOfParallelism = numThreads }, (i) =>
        {
            ulong start = clustersPerThread * (ulong)i;

            //  Only clear however many remaining clusters there are if this is the last thread
            ulong length = i == numThreads - 1 ? TotalNodes - start : clustersPerThread;

            Span<Node> span = new(&Nodes[start], (int)length);
            span.Fill(Node.Null);
        });

        TT.Clear();
    }


    public void ClearHalf(int half) => ClearHalf((uint)half);
    public void ClearHalf(uint half)
    {
        Filled[(int)half] = 0;
        ExpandedNodes = FetchedNodes = 0;
    }


    public bool ReserveNodes(uint toAdd, out NodePointer newFilled) => ReserveNodes(toAdd, (uint)CurrentHalf, out newFilled);
    public bool ReserveNodes(uint toAdd, int half, out NodePointer newFilled) => ReserveNodes(toAdd, (uint)half, out newFilled);
    public bool ReserveNodes(uint toAdd, uint half, out NodePointer newFilled)
    {
        uint newIdx = (uint)Interlocked.Add(ref Filled[(int)half], toAdd) - toAdd;
        newFilled = new(newIdx, half);
        return newIdx + toAdd < NodesPerHalf;
    }


    public void SwitchHalves()
    {
        var oldRoot = RootNodePointer;

        uint oldHalf = (uint)CurrentHalf;
        CurrentHalf = Not(CurrentHalf);

        var nodes = &Nodes[oldHalf * NodesPerHalf];
        for (ulong i = 0; i < NodesPerHalf; i++)
        {
            if (nodes[i].FirstChild.Half != oldHalf)
                nodes[i].ClearChildren();
        }

        ClearHalf(CurrentHalf);
        bool b = ReserveNodes(1, out var newRoot);
        Debug.Assert(b);

        this[newRoot].Clear();
        CopyNodeAcross(oldRoot, newRoot);
    }


    private void CopyNodeAcross(NodePointer src, NodePointer dst)
    {
        Debug.Assert(src != dst);

        this[dst] = this[src];
    }


    private void CopyAcross(NodePointer src, uint n, NodePointer dst)
    {
        for (uint i = 0; i < n; i++)
        {
            var a = (src + i);
            var b = (dst + i);
            if (false && a.Index <= 21)
                Console.Write($"copy_across src {a.Half}/{a.Index} == {this[a]}\tdst {b.Half}/{b.Index} == {this[b]}");

            CopyNodeAcross(src + i, dst + i);

            if (false && a.Index <= 21)
                Console.WriteLine($"\t-> dst {b.Half}/{b.Index} == {this[b]}");
        }
    }


    public bool FetchChildren(NodePointer parent)
    {
        var child = this[parent].FirstChild;
        if (child.Half == CurrentHalf)
            return true;

        var numChildren = this[parent].NumChildren;
        if (!ReserveNodes(numChildren, out var newPtr))
            return false;

        FetchedNodes += this[parent].NumChildren;

        CopyAcross(child, numChildren, newPtr);
        this[parent].FirstChild = newPtr;

        return true;
    }


    private void RemoveChildActions(int half) => RemoveChildActions((uint)half);
    private void RemoveChildActions(uint half)
    {
        var nodes = &Nodes[(half * NodesPerHalf)];
        for (ulong i = 0; i < NodesPerHalf; i++)
        {
            if (nodes[i].FirstChild.Half != half)
                nodes[i].ClearChildren();
        }
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
        Debug.Assert(CurrentFill == 0);

        var root = RootNodePointer;
        ReserveNodes(1, out _);
        this[root].Set(Move.Null, 0.0f);
        bool b = Expand(pos, root, 1);
        Debug.Assert(b);
        this[root].Update(1.0f - Iteration.GetNodeValue(pos, root));
    }


    public bool Expand(Position pos, NodePointer nodePtr, uint depth)
    {
        ref Node thisNode = ref this[nodePtr];

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

        var pst = SearchUtils.GetTemperatureAdjustment((int)depth, this[nodePtr].QValue);

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
    public NodePointer GetBestChildFunc(NodePointer nodePtr, ChildSelector F)
    {
        uint bestIdx = int.MaxValue;
        float bestScore = float.MinValue;

        ref var thisNode = ref this[nodePtr];
        var children = ChildrenOf(nodePtr);
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


    public (NodePointer idx, Move move, float q) GetBestAction(NodePointer nodePtr)
    {
        NodePointer idx = GetBestChild(nodePtr);
        Move move = this[idx].Move;
        float q = this[idx].QValue;
        return (idx, move, q);
    }


    public NodePointer GetBestChild(NodePointer nodePtr)
    {
        return GetBestChildFunc(nodePtr, (in Node n) => {
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
        NodePointer root = RootNodePointer;

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


    public void PrintRootVisits(Position pos)
    {
        var children = ChildrenOf(RootNode).ToArray().OrderByDescending(x => x.Visits).ToArray();

        foreach (var child in children)
            Log($"{child.Move.ToString(pos),-6} -> {child.Visits,7} visits, policy = {child.PolicyValue,7:0.0000}, score = {child.QValue,7}");
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
