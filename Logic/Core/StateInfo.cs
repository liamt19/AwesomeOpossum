﻿using System.Runtime.InteropServices;

using AwesomeOpossum.Logic.Evaluation;

namespace AwesomeOpossum.Logic.Core
{
    /// <summary>
    /// Contains information for a single moment of a <see cref="Position"/>.
    /// <br></br>
    /// When a <see cref="Move"/> is made, the <see cref="Position"/> will update one of these 
    /// with information such as the move's captured piece and changes to either players <see cref="CastlingStatus"/>, 
    /// and keep track of the squares that checks can occur on.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct StateInfo
    {
        public static readonly nuint StateCopySize = (nuint)Marshal.OffsetOf<StateInfo>(nameof(_pad0));

        [FieldOffset(  0)] public fixed ulong CheckSquares[PieceNB];
        [FieldOffset( 48)] public fixed ulong BlockingPieces[2];
        [FieldOffset( 64)] public fixed ulong Pinners[2];
        [FieldOffset( 80)] public fixed int KingSquares[2];
        [FieldOffset( 88)] public ulong Checkers = 0;
        [FieldOffset( 96)] public ulong Hash = 0;
        [FieldOffset(104)] public ulong PawnHash = 0;
        [FieldOffset(112)] public fixed ulong NonPawnHash[2];
        [FieldOffset(128)] public int HalfmoveClock = 0;
        [FieldOffset(132)] public int EPSquare = EPNone;
        [FieldOffset(136)] public int CapturedPiece = None;
        [FieldOffset(140)] public int PliesFromNull = 0;
        [FieldOffset(144)] public CastlingStatus CastleStatus = CastlingStatus.None;
        [FieldOffset(148)] private fixed byte _pad0[4];
        [FieldOffset(152)] public Accumulator* Accumulator;



        public StateInfo()
        {

        }

    }
}
