using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.MCTS;

public enum NodeState : byte
{
    Unterminated = 0,
    Loss,
    Draw,
    Win
}

public struct Node
{
    private const int Quantization =  16384 * 4;

    public ulong SumQ;
    public float PolicyValue;
    public uint Visits;

    public uint FirstChild;
    public byte NumChildren;

    public NodeState State;
    public Move Move;

    public bool IsTerminal => State != NodeState.Unterminated;
    public bool HasChildren => (NumChildren != 0);
    public bool IsExpanded => (IsTerminal || HasChildren);

    public readonly float QValue
    {
        get
        {
            var v = Visits;
            if (v == 0)
                return 0.0f;

            double q = (SumQ / v) / (double)Quantization;
            return (float)q;
        }
    }

    public void Set(Move m, float p)
    {
        Clear();
        Move = m;
        PolicyValue = p;
    }

    public void Clear()
    {
        PolicyValue = 0.0f;
        Visits = 0;
        SumQ = 0;
        FirstChild = 0;
        NumChildren = 0;
        State = NodeState.Unterminated;
        Move = Move.Null;
    }

    public float Update(float? q)
    {
        var nq = (ulong)((double)q * Quantization);
        var oldV = Interlocked.Add(ref Visits, 1);
        var oldQ = Interlocked.Add(ref SumQ, nq);
        //SumQSq += (q * q);

        return (float)((double)((q + oldQ) / (1 + oldV)) / Quantization);
    }

    public static bool operator ==(Node l, Node r) => l.Equals(r);
    public static bool operator !=(Node l, Node r) => !l.Equals(r);

    public static bool Equals(Node l, Node r) => l.PolicyValue == r.PolicyValue && l.FirstChild == r.FirstChild && l.Move == r.Move;

    public override string ToString() => $"{State}, {Move}={PolicyValue} #{Visits}";
}
