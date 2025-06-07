using AwesomeOpossum.Logic.Evaluation;
using AwesomeOpossum.Logic.Threads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.MCTS;

public static unsafe class SearchUtils
{
    public static float GetCPuct(in Node node, bool isRootNode)
    {
        var cpu = isRootNode ? 0.5f : 0.25f;
        
        var scale = 4000.0f;
        cpu *= 1 + float.Log10((node.Visits + scale) / scale);

        return cpu;
    }

    public static float GetFPU(in Node node)
    {
        return 1.0f - node.QValue;
    }

    public static float GetExplorationScale(in Node node)
    {
        var expl = float.Exp(0.5f * float.Log(Math.Max(node.Visits, 1)));

        var gini = float.Min(0.75f - 2.0f * float.Log(node.Impurity + 0.001f), 2.0f);
        expl *= gini;

        return expl;
    }

    public static float GetTemperatureAdjustment(int depth, float q)
    {
        var value = (q - Math.Min(q, TemperatureQInc)) / (1.0f - TemperatureQInc);

        //  sin(x * pi / 2) / 8
        var dCoef = ((2 - (depth % 4)) * (depth % 2)) / 8.0f;

        return 1.0f + value * TemperatureScale + dCoef;
    }

    public static float PolicyForMove(Position pos, Move move)
    {
        return PolicyNetwork.Evaluate(pos, move);
    }
}
