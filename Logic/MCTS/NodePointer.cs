
namespace AwesomeOpossum.Logic.MCTS;

public struct NodePointer
{
    public static readonly NodePointer Null = new(1u, int.MaxValue);

    public uint Value;

    public readonly uint Half => (Value >> 31);
    public readonly uint Index => (Value & 0x7FFFFFFF);

    public NodePointer(int half, uint index) : this((uint)half, index) { }
    public NodePointer(int half, int index) : this((uint)half, (uint)index) { }
    public NodePointer(uint half, uint index)
    {
        Value = (half << 31) | index;
    }

    public static NodePointer operator +(NodePointer l, uint r) => new NodePointer(l.Half, l.Index + r);

    public static bool operator ==(in NodePointer l, in NodePointer r) => l.Equals(r);
    public static bool operator !=(in NodePointer l, in NodePointer r) => !l.Equals(r);
    public bool Equals(in NodePointer r) => Value == r.Value;
    public override string ToString() => $"{Half}/{Index}";
}