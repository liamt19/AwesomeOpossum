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
    public static float? PerformOne(Position pos, uint nodeIdx, ref uint depth)
    {
        SearchThread thisThread = pos.Owner;
        var hash = pos.Hash;
        var tree = thisThread.Tree;
        ref var node = ref tree[nodeIdx];

        depth += 1;

        float? u;
        if (node.IsTerminal || node.Visits == 0)
        {
            if (node.Visits == 0)
                node.State = pos.PlayoutState();

            if (!node.IsTerminal && tree.TT.Probe(hash, out TTEntry* tte))
            {
                u = tte->Q;
            }
            else
            {
                u = GetNodeValue(pos, nodeIdx);
            }

        }
        else
        {
            
            if (!node.IsExpanded) {
                if (!tree.Expand(pos, nodeIdx, depth))
                    return null;
            }

            var childIdx = PickAction(pos, nodeIdx, node);

            var move = tree[childIdx].Move;

            Debug.Assert(pos.IsLegal(move));
            Debug.Assert(childIdx != 0);

            pos.MakeMove(move);
            u = PerformOne(pos, childIdx, ref depth);
            pos.UnmakeMove(move);

            if (u is null)
                return null;

            tree.PropagateMateScores(ref node, tree[childIdx].State);
        }

        u = 1.0f - u;
        float newQ = node.Update(u);
        tree.TT.Store(hash, 1.0f - newQ);

        return u;
    }

    public static float GetNodeValue(Position pos, uint nodeIdx)
    {
        SearchThread thisThread = pos.Owner;
        ref var node = ref thisThread.Tree[nodeIdx];

        return node.State switch
        {
            (NodeStateKind.Loss, _) => 0.0f,
            (NodeStateKind.Draw, _) => 0.5f,
            (NodeStateKind.Win, _) => 1.0f,
            _ => EvaluateNode(pos, nodeIdx)
        };
    }

    public static float EvaluateNode(Position pos, uint nodeIdx)
    {
        SearchThread thisThread = pos.Owner;
        ref var node = ref thisThread.Tree[nodeIdx];

        float f = ValueNetwork.Evaluate(pos);
        float wdl = 1.0f / (1.0f + float.Exp(-f / 400.0f));

        return wdl;
    }

    public static uint PickAction(Position pos, uint nodeIdx, in Node node)
    {
        var tree = pos.Owner.Tree;
        bool isRootNode = (node == tree.RootNode);

        var cpuct = SearchUtils.GetCPuct(node, isRootNode);
        var fpu = SearchUtils.GetFPU(node);
        var expl = SearchUtils.GetExplorationScale(node);
        expl *= cpuct;

        uint bestChild = tree.GetBestChildFunc(nodeIdx, (in Node n) => {
            var q = n.Visits == 0 ? fpu : n.QValue;
            
            var u = expl * n.PolicyValue / (1 + n.Visits);
            return q + u;
        });

        return bestChild;
    }
}
