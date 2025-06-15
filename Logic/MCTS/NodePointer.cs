
namespace AwesomeOpossum.Logic.MCTS;

public struct NodePointer(uint index, uint half)
{
    public static readonly NodePointer Null = new(0, 0);

    public uint Value = (half << 31) | index;
    public readonly uint Half => (Value >> 31);
    public readonly uint Index => (Value & 0x7FFFFFFF);

    public static explicit operator uint(in NodePointer n) => n.Index;
    public static explicit operator NodePointer(uint u) => new(u, 0);
    public static NodePointer operator +(NodePointer l, uint r) => (NodePointer)(l.Value + r);


    public static bool operator ==(in NodePointer l, in NodePointer r) => l.Equals(r);
    public static bool operator !=(in NodePointer l, in NodePointer r) => !l.Equals(r);
    public bool Equals(in NodePointer r) => Value == r.Value;
}