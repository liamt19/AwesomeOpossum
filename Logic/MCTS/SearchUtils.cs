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
        var cpu = isRootNode ? CPuctBaseRoot : CPuctBase;
        
        cpu *= 1 + float.Log10((node.Visits + CPuctVisitScale) / CPuctVisitScale);

        return cpu;
    }

    public static float GetFPU(in Node node)
    {
        return 1.0f - node.QValue;
    }

    public static float GetExplorationScale(in Node node)
    {
        return float.Exp(ExplTau * float.Log(Math.Max(node.Visits, 1)));
    }

    public static float GetTemperatureAdjustment(int depth, float q)
    {
        var winningAdj = PSTQScale * ((q - Math.Min(q, PSTQInc)) / (1.0f - PSTQInc));

        var depthAdj = (PSTNumer / MathF.Pow(depth, PSTPow)) - PSTOffset;

        //  sin(x * pi / 2) / ...
        var sinAdj = ((2 - (depth % 4)) * (depth % 2)) / PSTSinDiv;

        return 1.0f + winningAdj + depthAdj + sinAdj;
    }

    public static float PolicyForMove(Position pos, Move move)
    {
        return PolicyNetwork.Evaluate(pos, move);
    }
}
