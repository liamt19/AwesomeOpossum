using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AwesomeOpossum.Logic.MCTS
{
    public unsafe struct TreeRootState
    {
        public string FEN = default;
        public Bitboard Board = default;
        public StateInfo State = default;

        public TreeRootState() { }

        public static bool operator ==(in TreeRootState l, in TreeRootState r) => l.Equals(r);
        public static bool operator !=(in TreeRootState l, in TreeRootState r) => !l.Equals(r);
        //public bool Equals(in TreeRootState r) => (Board == r.Board && State == r.State);
        public bool Equals(in TreeRootState r) => (FEN == r.FEN);

        public static bool operator ==(in TreeRootState l, in Position r) => l.Equals(r);
        public static bool operator !=(in TreeRootState l, in Position r) => !l.Equals(r);
        public bool Equals(in Position r) => (FEN == r.GetFEN());

        public static TreeRootState FromPosition(Position pos)
        {
            TreeRootState newState = new()
            {
                FEN = pos.GetFEN(),
                Board = pos.bb,
                State = *pos.State
            };

            return newState;
        }

        public override string ToString()
        {
            return $"{State.Hash ^ Board.Occupancy}";
        }
    }
}
