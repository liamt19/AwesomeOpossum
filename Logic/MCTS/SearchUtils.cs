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
        
        var scale = 5000.0f;
        cpu *= 1 + float.Log10((node.Visits + scale) / scale);

        return cpu;
    }

    public static float GetFPU(in Node node)
    {
        return 1.0f - node.QValue;
    }

    public static float GetExplorationScale(in Node node)
    {
        return float.Exp(0.5f * float.Log10(Math.Max(node.Visits, 1)));
    }

    public static float GetTemperatureAdjustment(uint depth, float q)
    {
        var value = (q - Math.Min(q, TemperatureQInc)) / (1.0f - TemperatureQInc);
        return 1.0f + value * TemperatureScale;
    }

    public static float PolicyForMove(Position pos, Move move)
    {
        return PolicyNetwork.Evaluate(pos, move);
    }
}
