using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AwesomeOpossum.Logic.Transposition;

public static unsafe class Zobrist
{
    private static readonly ulong[] PSQHashes = new ulong[ColorNB * 6 * 64];
    private static readonly ulong[] CRHashes = new ulong[ColorNB * 2];
    private static readonly ulong[] EPHashes = new ulong[8];
    private static ulong BlackHash;
    private static readonly Random rand = new(0xBEEF);

    public static ulong HashForPiece(int pc, int pt, int sq) => PSQHashes[PSQIndex(pc, pt, sq)];
    public static ulong ColorHash => BlackHash;

    [ModuleInitializer]
    public static void Initialize()
    {
        for (int pt = Piece.Pawn; pt <= Piece.King; pt++)
        {
            for (int i = 0; i < 64; i++)
            {
                PSQHashes[PSQIndex(Color.White, pt, i)] = rand.NextUlong();
                PSQHashes[PSQIndex(Color.Black, pt, i)] = rand.NextUlong();
            }
        }

        for (int i = 0; i < 4; i++)
            CRHashes[i] = rand.NextUlong();

        for (int i = 0; i < 8; i++)
            EPHashes[i] = rand.NextUlong();

        BlackHash = rand.NextUlong();
    }

    public static ulong GetHash(Position position, ulong* pawnHash, ulong* nonPawnHash)
    {
        ref Bitboard bb = ref position.bb;

        ulong hash = 0;

        ulong white = bb.Colors[Color.White];
        ulong black = bb.Colors[Color.Black];

        for (int pc = White; pc <= Black; pc++)
        {
            ulong pieces = bb.Colors[pc];
            while (pieces != 0)
            {
                int idx = poplsb(&pieces);
                var pt = bb.GetPieceAtIndex(idx);
                var psq = PSQIndex(pc, pt, idx);
                hash ^= PSQHashes[psq];

                if (pt == Pawn)
                    *pawnHash ^= PSQHashes[psq];
                else
                    *nonPawnHash ^= PSQHashes[psq];
            }
        }

        if ((position.State->CastleStatus & CastlingStatus.WK) != 0)
            hash ^= CRHashes[0];
        if ((position.State->CastleStatus & CastlingStatus.WQ) != 0)
            hash ^= CRHashes[1];
        if ((position.State->CastleStatus & CastlingStatus.BK) != 0)
            hash ^= CRHashes[2];
        if ((position.State->CastleStatus & CastlingStatus.BQ) != 0)
            hash ^= CRHashes[3];

        if (position.State->EPSquare != EPNone)
            hash ^= EPHashes[GetIndexFile(position.State->EPSquare)];

        if (position.ToMove == Black)
            hash ^= BlackHash;

        return hash;
    }

    /// <summary>
    /// Updates the hash by moving the piece of type <paramref name="pt"/> and color <paramref name="color"/> from <paramref name="from"/> to <paramref name="to"/>.
    /// If the move is a capture, ZobristToggleSquare needs to be done as well.
    /// </summary>
    public static void ZobristMove(this ref ulong hash, int from, int to, int color, int pt)
    {
        Assert(from is >= A1 and <= H8, $"ZobristMove({from}, {to}, {color}, {pt}) wasn't given a valid From square! (should be 0 <= idx <= 63)");
        Assert(to is >= A1 and <= H8, $"ZobristMove({from}, {to}, {color}, {pt}) wasn't given a valid To square! (should be 0 <= idx <= 63)");
        Assert(color is White or Black, $"ZobristMove({from}, {to}, {color}, {pt}) wasn't given a valid piece color! (should be 0 or 1)");
        Assert(pt is >= Pawn and <= King, $"ZobristMove({from}, {to}, {color}, {pt}) wasn't given a valid piece type! (should be 0 <= pt <= 5)");

        var fromIndex = PSQIndex(color, pt, from);
        var toIndex = PSQIndex(color, pt, to);
        ref var start = ref MemoryMarshal.GetArrayDataReference(PSQHashes);

        hash ^= Unsafe.Add(ref start, fromIndex) ^ Unsafe.Add(ref start, toIndex);
    }

    /// <summary>
    /// Adds or removes the piece of type <paramref name="pt"/> and color <paramref name="color"/> at index <paramref name="idx"/>
    /// </summary>
    public static void ZobristToggleSquare(this ref ulong hash, int color, int pt, int idx)
    {
        Assert(color is White or Black, $"ZobristToggleSquare({color}, {pt}, {idx}) wasn't given a valid piece color! (should be 0 or 1)");
        Assert(pt is >= Pawn and <= King, $"ZobristToggleSquare({color}, {pt}, {idx}) wasn't given a valid piece type! (should be 0 <= pt <= 5)");
        Assert(idx is >= A1 and <= H8, $"ZobristToggleSquare({color}, {pt}, {idx}) wasn't given a valid square! (should be 0 <= idx <= 63)");

        var index = PSQIndex(color, pt, idx);

        hash ^= Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(PSQHashes), index);
    }

    /// <summary>
    /// Updates the castling status of the hash, and doesn't change anything if the castling status hasn't changed
    /// </summary>
    public static void ZobristCastle(this ref ulong hash, CastlingStatus prev, CastlingStatus toRemove)
    {
        ulong change = (ulong)(prev & toRemove);
        while (change != 0)
        {
            hash ^= Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(CRHashes), poplsb(&change));
        }
    }

    /// <summary>
    /// Sets the En Passant status of the hash, which is set to the <paramref name="file"/> of the pawn that moved two squares previously
    /// </summary>
    public static void ZobristEnPassant(this ref ulong hash, int file)
    {
        hash ^= Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(EPHashes), file);
    }

    /// <summary>
    /// Called each time White makes a move, which updates the hash to show that it's black to move now
    /// </summary>
    public static void ZobristChangeToMove(this ref ulong hash)
    {
        hash ^= BlackHash;
    }

    private static int PSQIndex(int color, int piece, int square) => (color * 64 * 6) + (piece * 64) + square;
}
