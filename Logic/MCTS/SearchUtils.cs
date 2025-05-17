using AwesomeOpossum.Logic.Evaluation;
using AwesomeOpossum.Logic.Threads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.MCTS
{
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
            return 0.5f * float.Exp(float.Log10(Math.Max(node.Visits, 1)));
        }

        public static float GetPST(uint depth, float q)
        {
            return 1.0f;
        }

        public static uint AssignScores(Position pos, ScoredMove* moves)
        {
            int size = pos.GenLegal(moves);

            for (int i = 0; i < size; i++)
            {
                moves[i].Score = PolicyForMove(pos, moves[i].Move);
            }

            return (uint)size;
        }

        public static int PolicyForMove(Position pos, Move move)
        {
            pos.MakeMove(move);
            int ev = Pesto.Evaluate(pos);
            pos.UnmakeMove(move);
            return -ev;
        }
    }
}
