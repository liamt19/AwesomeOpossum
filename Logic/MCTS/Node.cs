using System;
using System.Collections.Generic;
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
    public float PolicyValue;
    public uint Visits;

    public uint FirstChild;
    public byte NumChildren;

    public NodeState State;
    public Move Move;

    public bool IsTerminal => State != NodeState.Unterminated;
    public bool HasChildren => (NumChildren != 0);
}
