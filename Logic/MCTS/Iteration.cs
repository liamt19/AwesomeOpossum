using AwesomeOpossum.Logic.Search;
using AwesomeOpossum.Logic.Threads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.MCTS;

public static class Iteration
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
            Log($"{nodeIdx} Terminal");
            if (node.Visits == 0)
                node.State = pos.PlayoutState();

            u = GetNodeValue(pos, nodeIdx);
        }
        else
        {
            Log($"{nodeIdx} ok");
            if (!node.IsExpanded) {
                Log($"{nodeIdx} expanding");
                tree.Expand(pos, nodeIdx, depth);
            }

            var bestChild = PickAction(pos, nodeIdx, node);

            var childIdx = nodeIdx + bestChild;
            var move = tree[childIdx].Move;

            Debug.Assert(pos.IsLegal(move));

            pos.MakeMove(move);
            u = PerformOne(pos, childIdx, ref depth);
            pos.UnmakeMove(move);
        }

        u = 1.0f - u;
        node.Update(u);

        return u;
    }

    public static float GetNodeValue(Position pos, uint nodeIdx)
    {
        SearchThread thisThread = pos.Owner;
        ref var node = ref thisThread.Tree[nodeIdx];

        return node.State switch
        {
            NodeState.Loss => 0.0f,
            NodeState.Draw => 0.5f,
            NodeState.Win => 1.0f,
            _ => EvaluateNode(pos, nodeIdx)
        };
    }

    public static float EvaluateNode(Position pos, uint nodeIdx)
    {
        SearchThread thisThread = pos.Owner;
        ref var node = ref thisThread.Tree[nodeIdx];

        float f = Random.Shared.NextSingle();
        f = float.Clamp(f, 0.0000001f, 1.0f - 0.0000001f);
        if (f == 0.5f)
            f += 0.0000001f;

        return f;
    }

    public static uint PickAction(Position pos, uint nodeIdx, in Node node)
    {
        var tree = pos.Owner.Tree;
        bool isRootNode = (node == tree.RootNode);

        var cpuct = SearchUtils.GetCPuct(node, isRootNode);
        var fpu = SearchUtils.GetFPU(node);
        var expl = SearchUtils.GetExplorationScale(node);

        uint bestChild = tree.GetBestChild(nodeIdx, (in Node n) => {
            var q = n.Visits == 0 ? fpu : n.QValue;
            
            var u = expl * n.PolicyValue / (1 + n.Visits);
            return q + u;
        });

        return bestChild;
    }
}
