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
    private const double QuantSquared = Quantization * Quantization;

    public ulong SumQ;
    public ulong SumSquareQ;
    public float PolicyValue;
    public float Gini;
    public uint Visits;

    public uint FirstChild;
    public Move Move;
    public NodeState State;

    public byte NumChildren;

    public bool IsTerminal => State.Kind != NodeStateKind.Unterminated;
    public bool IsOngoing => State.Kind == NodeStateKind.Unterminated;
    public bool HasChildren => (NumChildren != 0);
    public bool IsExpanded => (IsTerminal || HasChildren);
    public bool IsValid => (this != default);

    public readonly float QValue => (float)Q64;
    private readonly double Q64 => Visits == 0 ? 0.0f : (SumQ / Visits) / Quantization;

    public readonly double SquareQ => (SumSquareQ / Visits) / QuantSquared;
    public readonly double Variance => (float)Math.Max(SquareQ - Math.Pow(Q64, 2), 0.0);

    public readonly float Impurity => float.Clamp(Gini, 0.0f, 1.0f);
    public readonly float ExplorationValue => PolicyValue / (1 + Visits);


    public void Set(Move m, float p)
    {
        Clear();
        Move = m;
        PolicyValue = p;
    }

    public void Clear()
    {
        SumQ = SumSquareQ = 0;
        PolicyValue = Gini = 0.0f;
        Visits = FirstChild = 0;
        NumChildren = 0;
        State = NodeState.Unterminated;
        Move = Move.Null;
    }

    public float Update(float? q)
    {
        var nq = (ulong)(q * Quantization);
        var oldV = FetchAdd(ref Visits, 1);
        var oldQ = FetchAdd(ref SumQ, nq);
        FetchAdd(ref SumSquareQ, nq * nq);

        return (float)(((nq + oldQ) / (1.0 + oldV)) / Quantization);
    }

    public static bool operator ==(in Node l, in Node r) => l.Equals(r);
    public static bool operator !=(in Node l, in Node r) => !l.Equals(r);

    public bool Equals(in Node r) => Visits == r.Visits && FirstChild == r.FirstChild && Move == r.Move;

    public override string ToString() => $"{State}, {Move}={PolicyValue} V={Visits} C={NumChildren} @ {FirstChild}";
}
