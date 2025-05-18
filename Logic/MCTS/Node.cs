using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.MCTS;
public struct Node
{
    private const double Quantization =  16384.0 * 4;

    public ulong SumQ;
    public float PolicyValue;
    public uint Visits;

    public uint FirstChild;
    public byte NumChildren;

    public NodeState State;
    public Move Move;

    public bool IsTerminal => State.Kind != NodeStateKind.Unterminated;
    public bool IsOngoing => State.Kind == NodeStateKind.Unterminated;
    public bool HasChildren => (NumChildren != 0);
    public bool IsExpanded => (IsTerminal || HasChildren);
    public bool IsValid => (this != default);

    public readonly float QValue
    {
        get
        {
            if (Visits == 0)
                return 0.0f;

            double q = (SumQ / (double)Visits) / Quantization;
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
        var oldV = Interlocked.Add(ref Visits, 1) - 1;
        var oldQ = Interlocked.Add(ref SumQ, nq) - nq;
        //SumQSq += (q * q);

        return (float)(((nq + oldQ) / (1.0 + oldV)) / Quantization);
    }

    public static bool operator ==(Node l, Node r) => l.Equals(r);
    public static bool operator !=(Node l, Node r) => !l.Equals(r);

    public static bool Equals(Node l, Node r) => l.Visits == r.Visits && l.FirstChild == r.FirstChild && l.Move == r.Move;

    public override string ToString() => $"{State}, {Move}={PolicyValue} V={Visits} C={NumChildren} @ {FirstChild}";
}
