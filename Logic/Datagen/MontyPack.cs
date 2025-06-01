using AwesomeOpossum.Logic.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.Datagen;

public unsafe struct MontyCastling
{
    public bool chess960;
    public fixed byte castle_mask[64];
    public fixed byte rook_files[4];

    public static MontyCastling FromPosition(Position pos)
    {
        MontyCastling m = new();

        m.chess960 = pos.IsChess960;

        m.rook_files[0] = (byte)GetIndexFile(pos.CastlingRookSquare(CastlingStatus.WK));
        m.rook_files[1] = (byte)GetIndexFile(pos.CastlingRookSquare(CastlingStatus.WQ));
        m.rook_files[2] = (byte)GetIndexFile(pos.CastlingRookSquare(CastlingStatus.BK));
        m.rook_files[3] = (byte)GetIndexFile(pos.CastlingRookSquare(CastlingStatus.BQ));

        new Span<byte>(m.castle_mask, 64).Fill(15);
        m.castle_mask[m.rook_files[0]] = 7;
        m.castle_mask[m.rook_files[1]] = 11;
        m.castle_mask[m.rook_files[2] + 56] = 13;
        m.castle_mask[m.rook_files[3] + 56] = 14;
        m.castle_mask[pos.State->KingSquares[White]] = 3;
        m.castle_mask[(pos.State->KingSquares[Black] ^ 56) + 56] = 12;

        return m;
    }

    public static MontyCastling FromStartpos()
    {
        MontyCastling m = new();

        m.chess960 = UCI_Chess960;

        m.rook_files[0] = 0;
        m.rook_files[1] = 7;
        m.rook_files[2] = 0;
        m.rook_files[3] = 7;

        new Span<byte>(m.castle_mask, 64).Fill(15);
        m.castle_mask[m.rook_files[0]] = 7;
        m.castle_mask[m.rook_files[1]] = 11;
        m.castle_mask[m.rook_files[2] + 56] = 13;
        m.castle_mask[m.rook_files[3] + 56] = 14;
        m.castle_mask[E1] = 3;
        m.castle_mask[E8] = 12;

        return m;
    }
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MontyCompressedPosition(byte stm, byte enp_sq, byte rights, byte halfm, ushort fullm)
{
    public fixed ulong bbs[4];
    public byte stm = stm;
    public byte enp_sq = enp_sq;
    public byte rights = rights;
    public byte halfm = halfm;
    public ushort fullm = fullm;

    public static MontyCompressedPosition FromMontyPosition(in MontyPosition p)
    {
        MontyCompressedPosition m = new(p.stm, p.enp_sq, p.rights, p.halfm, p.fullm);
        m.bbs[0] = p.bbs[1];
        m.bbs[1] = p.bbs[5] ^ p.bbs[6] ^ p.bbs[7];
        m.bbs[2] = p.bbs[3] ^ p.bbs[4] ^ p.bbs[7];
        m.bbs[3] = p.bbs[2] ^ p.bbs[4] ^ p.bbs[6];

        return m;
    }
}


[StructLayout(LayoutKind.Sequential)]
public unsafe struct MontyPosition(byte stm, byte enp_sq, byte rights, byte halfm, ushort fullm)
{
    public fixed ulong bbs[8];
    public byte stm = stm;
    public byte enp_sq = enp_sq;
    public byte rights = rights;
    public byte halfm = halfm;
    public ushort fullm = fullm;

    public static MontyPosition FromPosition(Position pos)
    {
        MontyPosition m = new();

        fixed (ulong* src = pos.bb.Colors)
            Unsafe.CopyBlock(m.bbs, src, sizeof(ulong) * 2);

        fixed (ulong* src = pos.bb.Pieces)
            Unsafe.CopyBlock(&m.bbs[2], src, sizeof(ulong) * 6);

        m.stm = (byte)pos.ToMove;
        m.enp_sq = (byte)pos.State->EPSquare;
        m.halfm = (byte)pos.State->HalfmoveClock;
        m.fullm = (byte)pos.FullMoves;

        //  Swap the bit representations of KQkq -> kqKQ because Monty has this reversed
        var rights = (byte)pos.State->CastleStatus;
        m.rights = (byte)(((rights & 0b1100) >> 2) | ((rights & 0b0011) << 2));

        return m;
    }

    public static MontyPosition FromStartpos()
    {
        MontyPosition m = new(0, 0, 15, 0, 1);
        m.bbs[0] = 65535;
        m.bbs[1] = 18446462598732840960;
        m.bbs[2] = 71776119061282560;
        m.bbs[3] = 4755801206503243842;
        m.bbs[4] = 2594073385365405732;
        m.bbs[5] = 9295429630892703873;
        m.bbs[6] = 576460752303423496;
        m.bbs[7] = 1152921504606846992;

        return m;
    }
}

public unsafe struct SearchData
{
    public Move best_move;
    public float score;
    public fixed uint visit_distribution[MoveListSize];

    public int NumChildren;
}

unsafe ref struct MontyPack
{
    public const int MaxSize = 1024;

    public MontyPosition startpos;
    public MontyCastling rights;
    public float result;
    public Span<SearchData> moves;

    public int NumEntries;

    public void Push(in SearchData sd)
    {
        moves[NumEntries++] = sd;
    }

    public void Clear()
    {
        startpos = MontyPosition.FromStartpos();
        rights = MontyCastling.FromStartpos();
        result = 0.0f;
        NumEntries = 0;
    }

    public bool IsAtMoveLimit => NumEntries == MaxSize - 1;

    public void AddResultsAndWrite(float result, BinaryWriter br)
    {
        MontyCompressedPosition mcp = MontyCompressedPosition.FromMontyPosition(startpos);
        
        for (int i = 0; i < 4; i++)
            br.Write(mcp.bbs[i]);

        br.Write(mcp.stm);
        br.Write(mcp.enp_sq);
        br.Write(mcp.rights);
        br.Write(mcp.halfm);
        br.Write(mcp.fullm);

        for (int i = 0; i < 4; i++)
            br.Write(rights.rook_files[i]);

        br.Write((byte)(((int)result) * 2));

        var moveSpan = moves[..NumEntries];
        foreach (var sd in moveSpan)
        {
            Debug.Assert(sd.score >= 0.0f && sd.score <= 1.0f);

            var s = (ushort)(sd.score * ushort.MaxValue);

            br.Write(sd.best_move.GetData());
            br.Write(s);
            
            if (sd.NumChildren == 0)
                continue;

            br.Write((byte)sd.NumChildren);

            uint maxVisits = 0;
            for (int i = 0; i < sd.NumChildren; i++)
                maxVisits = Math.Max(maxVisits, sd.visit_distribution[i]);

            for (int i = 0; i < sd.NumChildren; i++)
            {
                var scaled = (byte)(sd.visit_distribution[i] * 255.0f / maxVisits);
                br.Write(scaled);
            }
        }

        br.Write((ushort)0);
        br.Flush();
    }
}
