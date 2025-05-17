
namespace AwesomeOpossum.Logic.MCTS;

public enum NodeStateKind : byte
{
    Unterminated = 0,
    Loss,
    Draw,
    Win
}

public record struct NodeState(NodeStateKind Kind, byte Length = 0)
{
    public static readonly NodeState Unterminated = new(NodeStateKind.Unterminated);
    public static readonly NodeState Draw = new(NodeStateKind.Draw);
    public static readonly NodeState Loss = new(NodeStateKind.Loss);
    public static readonly NodeState Win = new(NodeStateKind.Win);

    public static NodeState MakeLoss(byte l) => new(NodeStateKind.Loss, l);
    public static NodeState MakeWin(byte l) => new(NodeStateKind.Win, l);
}
