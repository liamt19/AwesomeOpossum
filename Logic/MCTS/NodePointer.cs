
namespace AwesomeOpossum.Logic.MCTS;

public struct NodePointer(uint index, uint half)
{
    public static readonly NodePointer Null = new(int.MaxValue, 1);
    public static readonly NodePointer Root = Null;

    public uint Value = (half << 31) | index;
    public NodePointer(uint index, int half) : this(index, (uint)half) { }

    public readonly uint Half => (Value >> 31);
    public readonly uint Index => (Value & 0x7FFFFFFF);

    public static NodePointer operator +(NodePointer l, uint r) => new NodePointer(l.Index + r, l.Half);

    public static bool operator ==(in NodePointer l, in NodePointer r) => l.Equals(r);
    public static bool operator !=(in NodePointer l, in NodePointer r) => !l.Equals(r);
    public bool Equals(in NodePointer r) => Value == r.Value;

    public override string ToString() => $"{Half}/{Index}";
}