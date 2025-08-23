using AwesomeOpossum.Logic.Evaluation;
using AwesomeOpossum.Logic.Search;
using AwesomeOpossum.Logic.Threads;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.MCTS;

public static unsafe class Iteration
{
    public static float? PerformOne(Position pos, NodePointer nodePtr, ref uint depth)
    {
        SearchThread thisThread = pos.Owner;
        var hash = pos.Hash;
        var tree = thisThread.Tree;
        ref var node = ref tree[nodePtr];

        depth += 1;

        float? u;
        if (node.IsTerminal || node.Visits == 0)
        {
            if (node.Visits == 0)
                node.State = pos.PlayoutState();

            if (!node.IsTerminal && tree.TT.Probe(hash, out TTEntry* tte))
                u = tte->Q;
            else
                u = GetNodeValue(pos, nodePtr);
        }
        else
        {
            if (!node.IsExpanded) {
                if (!tree.Expand(pos, nodePtr, depth))
                    return null;
            }

            if (!tree.FetchChildren(nodePtr))
                return null;

            var childPtr = PickAction(pos, nodePtr, node);

            var move = tree[childPtr].Move;

            Debug.Assert(childPtr != default);
            Debug.Assert(childPtr.Index != 0);
            Debug.Assert(pos.IsLegal(move));

            pos.MakeMove(move);
            u = PerformOne(pos, childPtr, ref depth);
            pos.UnmakeMove(move);

            if (u is null)
                return null;

            tree.PropagateMateScores(ref node, tree[childPtr].State);
        }

        u = 1.0f - u;
        float newQ = node.Update(u);
        tree.TT.Store(hash, 1.0f - newQ);

        return u;
    }


    public static float GetNodeValue(Position pos, in NodePointer nodePtr)
    {
        SearchThread thisThread = pos.Owner;
        ref var node = ref thisThread.Tree[nodePtr];

        return node.State switch
        {
            (NodeStateKind.Loss, _) => 0.0f,
            (NodeStateKind.Draw, _) => 0.5f,
            (NodeStateKind.Win, _) => 1.0f,
            _ => EvaluateNode(pos)
        };
    }


    public static float EvaluateNode(Position pos)
    {
        float raw = ValueNetwork.Evaluate(pos);
        float wdl = raw.Sigmoid();

        return wdl;
    }


    public static NodePointer PickAction(Position pos, NodePointer nodePtr, in Node node)
    {
        var tree = pos.Owner.Tree;
        bool isRootNode = (node == tree.RootNode);

        var cpuct = SearchUtils.GetCPuct(node, isRootNode);
        var fpu = SearchUtils.GetFPU(node);
        var expl = SearchUtils.GetExplorationScale(node);
        expl *= cpuct;

        NodePointer bestChild = tree.GetBestChildFunc(nodePtr, (in Node n) => {
            var q = n.Visits == 0 ? fpu : n.QValue;
            var u = expl * n.ExplorationValue;
            return q + u;
        });

        return bestChild;
    }
}
