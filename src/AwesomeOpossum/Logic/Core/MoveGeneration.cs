using AwesomeOpossum.Logic.Evaluation;
using AwesomeOpossum.Logic.MCTS;
using System.Numerics.Tensors;

namespace AwesomeOpossum.Logic.Core
{
    public unsafe partial class Position
    {
        /// The "GenNoisy" approach was inspired by the move generation of Stockfish.


        /// <summary>
        /// Generates the pseudo-legal moves for all of the pawns in the position, placing them into the 
        /// Move <paramref name="list"/> starting at the index <paramref name="size"/> and the new number
        /// of moves in the list is returned.
        /// <para></para>
        /// Only moves which have a To square whose bit is set in <paramref name="targets"/> will be generated.
        /// <br></br>
        /// For example:
        /// <br></br>
        /// When generating captures, <paramref name="targets"/> should be set to our opponent's color mask.
        /// <br></br>
        /// When generating evasions, <paramref name="targets"/> should be set to the <see cref="LineBB"/> between our king and the checker, which is the mask
        /// of squares that would block the check or capture the piece giving check.
        /// </summary>
        public int GenPawns<GenType>(Move* list, ulong targets, int size) where GenType : MoveGenerationType
        {
            bool noisyMoves  = typeof(GenType) == typeof(GenNoisy);
            bool evasions    = typeof(GenType) == typeof(GenEvasions);
            bool nonEvasions = typeof(GenType) == typeof(GenNonEvasions);

            ulong rank7 = (ToMove == White) ? Rank7BB : Rank2BB;
            ulong rank3 = (ToMove == White) ? Rank3BB : Rank6BB;

            int up = ShiftUpDir(ToMove);

            ulong us   = bb.Colors[ToMove];
            ulong them = bb.Colors[Not(ToMove)];
            ulong captureSquares = evasions ? State->Checkers : them;

            ulong emptySquares = ~bb.Occupancy;

            ulong ourPawns = us & bb.Pieces[Pawn];
            ulong promotingPawns    = ourPawns & rank7;
            ulong notPromotingPawns = ourPawns & ~rank7;

            int theirKing = State->KingSquares[Not(ToMove)];

            if (!noisyMoves)
            {
                //  Include pawn pushes
                ulong moves    = Shift(up, notPromotingPawns) & emptySquares;
                ulong twoMoves = Shift(up, moves & rank3)     & emptySquares;

                if (evasions)
                {
                    //  Only include pushes which block the check
                    moves    &= targets;
                    twoMoves &= targets;
                }

                while (moves != 0)
                {
                    int to = poplsb(&moves);
                    list[size++] = new Move(to - up, to);
                }

                while (twoMoves != 0)
                {
                    int to = poplsb(&twoMoves);
                    list[size++] = new Move(to - up - up, to);
                }
            }

            if (promotingPawns != 0)
            {
                ulong promotions = Shift(up, promotingPawns) & emptySquares;
                ulong promotionCapturesL = Shift(up + Direction.WEST, promotingPawns) & captureSquares;
                ulong promotionCapturesR = Shift(up + Direction.EAST, promotingPawns) & captureSquares;

                if (evasions || noisyMoves)
                {
                    //  Only promote on squares that block the check or capture the checker.
                    promotions &= targets;
                }

                while (promotions != 0)
                {
                    int to = poplsb(&promotions);
                    size = MakePromotionChecks(list, to - up, to, false, size);
                }

                while (promotionCapturesL != 0)
                {
                    int to = poplsb(&promotionCapturesL);
                    size = MakePromotionChecks(list, to - up - Direction.WEST, to, true, size);
                }

                while (promotionCapturesR != 0)
                {
                    int to = poplsb(&promotionCapturesR);
                    size = MakePromotionChecks(list, to - up - Direction.EAST, to, true, size);
                }
            }

            //  Don't generate captures for quiets
            ulong capturesL = Shift(up + Direction.WEST, notPromotingPawns) & captureSquares;
            ulong capturesR = Shift(up + Direction.EAST, notPromotingPawns) & captureSquares;

            while (capturesL != 0)
            {
                int to = poplsb(&capturesL);
                list[size++] = new Move(to - up - Direction.WEST, to);
            }

            while (capturesR != 0)
            {
                int to = poplsb(&capturesR);
                list[size++] = new Move(to - up - Direction.EAST, to);
            }

            if (State->EPSquare != EPNone && !noisyMoves)
            {
                if (evasions && (targets & (SquareBB[State->EPSquare + up])) != 0)
                {
                    //  When in check, we can only en passant if the pawn being captured is the one giving check
                    return size;
                }

                ulong mask = notPromotingPawns & PawnAttackMasks[Not(ToMove)][State->EPSquare];
                while (mask != 0)
                {
                    int from = poplsb(&mask);
                    list[size++] = new Move(from, State->EPSquare, Move.FlagEnPassant);
                }
            }

            return size;


            int MakePromotionChecks(Move* list, int from, int promotionSquare, bool isCapture, int size)
            {
                list[size++] = new Move(from, promotionSquare, Move.FlagPromoQueen);

                if (!noisyMoves || isCapture)
                {
                    list[size++] = new Move(from, promotionSquare, Move.FlagPromoKnight);
                    list[size++] = new Move(from, promotionSquare, Move.FlagPromoRook);
                    list[size++] = new Move(from, promotionSquare, Move.FlagPromoBishop);
                }

                return size;
            }
        }


        /// <summary>
        /// Generates all the pseudo-legal moves for the player whose turn it is to move, given the <see cref="MoveGenerationType"/>.
        /// These are placed in the Move <paramref name="list"/> starting at the index <paramref name="size"/> and the new number
        /// of moves in the list is returned.
        /// </summary>
        public int GenAll<GenType>(Move* list, int size = 0) where GenType : MoveGenerationType
        {
            bool noisyMoves  = typeof(GenType) == typeof(GenNoisy);
            bool evasions    = typeof(GenType) == typeof(GenEvasions);
            bool nonEvasions = typeof(GenType) == typeof(GenNonEvasions);

            ulong us   = bb.Colors[ToMove];
            ulong them = bb.Colors[Not(ToMove)];
            ulong occ  = bb.Occupancy;

            int ourKing   = State->KingSquares[ToMove];
            int theirKing = State->KingSquares[Not(ToMove)];

            ulong targets = 0;

            // If we are generating evasions and in double check, then skip non-king moves.
            if (!(evasions && MoreThanOne(State->Checkers)))
            {
                targets = evasions    ? LineBB[ourKing][lsb(State->Checkers)]
                        : nonEvasions ? ~us
                        : noisyMoves  ?  them
                        :               ~occ;

                size = GenPawns<GenType>(list, targets, size);
                size = GenNormal(list, Knight, targets, size);
                size = GenNormal(list, Bishop, targets, size);
                size = GenNormal(list, Rook, targets, size);
                size = GenNormal(list, Queen, targets, size);
            }

            ulong moves = NeighborsMask[ourKing] & (evasions ? ~us : targets);
            while (moves != 0)
            {
                list[size++] = new Move(ourKing, poplsb(&moves));
            }

            if (nonEvasions)
            {
                if (ToMove == White && (ourKing == E1 || IsChess960))
                {
                    if (CanCastle(occ, us, CastlingStatus.WK))
                        list[size++] = new Move(ourKing, CastlingRookSquares[(int)CastlingStatus.WK], Move.FlagCastle);

                    if (CanCastle(occ, us, CastlingStatus.WQ))
                        list[size++] = new Move(ourKing, CastlingRookSquares[(int)CastlingStatus.WQ], Move.FlagCastle);
                }
                else if (ToMove == Black && (ourKing == E8 || IsChess960))
                {
                    if (CanCastle(occ, us, CastlingStatus.BK))
                        list[size++] = new Move(ourKing, CastlingRookSquares[(int)CastlingStatus.BK], Move.FlagCastle);

                    if (CanCastle(occ, us, CastlingStatus.BQ))
                        list[size++] = new Move(ourKing, CastlingRookSquares[(int)CastlingStatus.BQ], Move.FlagCastle);
                }
            }

            return size;
        }


        /// <summary>
        /// Generates all of the legal moves that the player whose turn it is to move is able to make.
        /// The moves are placed into the array that <paramref name="legal"/> points to, 
        /// and the number of moves that were created is returned.
        /// </summary>
        public int GenLegal(Move* legal)
        {
            int numMoves = (State->Checkers != 0) ? GenAll<GenEvasions>(legal) :
                                                    GenAll<GenNonEvasions>(legal);

            int ourKing   = State->KingSquares[ToMove];
            int theirKing = State->KingSquares[Not(ToMove)];
            ulong pinned  = State->BlockingPieces[ToMove];

            Move* curr = legal;
            Move* end = legal + numMoves;

            while (curr != end)
            {
                if (!IsLegal(*curr, ourKing, theirKing, pinned))
                {
                    *curr = *--end;
                    numMoves--;
                }
                else
                {
                    ++curr;
                }
            }

            return numMoves;
        }

        public uint GenerateAndScoreLegals(Move* legal, ref Span<float> policies)
        {
            //  Note: Passing policies byref so that we can slice it here.
            //  This is both for convenience and correctness because logits are usually negative and
            //  we don't want TensorPrimitives.Max seeing the trailing 0.0f's in the span.

            PolicyNetwork.RefreshPolicyAccumulator(this);
            int numMoves = Checked ? GenAll<GenEvasions>(legal) : GenAll<GenNonEvasions>(legal);

            int ourKing = State->KingSquares[ToMove];
            int theirKing = State->KingSquares[Not(ToMove)];
            ulong pinned = State->BlockingPieces[ToMove];

            Move* curr = legal;
            Move* end = legal + numMoves;
            while (curr != end)
            {
                if (!IsLegal(*curr, ourKing, theirKing, pinned))
                {
                    *curr = *--end;
                    numMoves--;
                }
                else
                {
                    ++curr;
                }
            }

            policies = policies[..numMoves];
            for (int i = 0; i < numMoves; i++)
                policies[i] = PolicyNetwork.Evaluate(this, legal[i]);

            return (uint)numMoves;
        }


        public int GenNormal(Move* list, int pt, ulong targets, int size)
        {
            // TODO: JIT seems to prefer having separate methods for each piece type, instead of a 'pt' parameter
            // This is far more convenient though

            ulong occ = bb.Occupancy;
            ulong ourPieces = bb.Pieces[pt] & bb.Colors[ToMove];
            while (ourPieces != 0)
            {
                int idx = poplsb(&ourPieces);
                ulong moves = bb.AttackMask(idx, ToMove, pt, occ) & targets;

                while (moves != 0)
                {
                    int to = poplsb(&moves);
                    list[size++] = new Move(idx, to);
                }
            }

            return size;
        }


        /// <summary>
        /// Generates the pseudo-legal evasion or non-evasion moves for the position, depending on if the side to move is in check.
        /// The moves are placed into the array that <paramref name="pseudo"/> points to, 
        /// and the number of moves that were created is returned.
        /// </summary>
        public int GenPseudoLegal(Move* pseudo)
        {
            return (State->Checkers != 0) ? GenAll<GenEvasions>   (pseudo)
                                          : GenAll<GenNonEvasions>(pseudo);
        }

    }
}
