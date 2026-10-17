// Faz 0 minimal projeksiyon: TableService.CurrentState'ten Gin görünümü okur, asla yazmaz. Faz 2'de GameTable tabanı +
// tam state machine ile genişler; şimdilik dikey dilimin loglaması için.
using System.Collections.Generic;
using System.Text;
using M;

namespace Digitoy.GinRummy
{
    public static class GinTable
    {
        public static TableState State => TableService.I?.CurrentState;
        public static TablePhase Phase => TableService.I?.Phase ?? TablePhase.None;
        public static TableSubPhase SubPhase => State?.SubPhase ?? TableSubPhase.None;
        public static bool IsMyTurn => TableService.I?.IsMyTurn ?? false;
        public static List<int> MyHand => TableService.I?.GetPlayer()?.Hand;
        public static int DeckCount => State?.DeckCount ?? 0;
        public static int DeckTop => State?.DeckLastNo ?? -1;
        public static int TopDiscard(int relativePos = 1) => TableService.I?.GetDiscardAt(relativePos) ?? -1;

        public static string Describe()
        {
            var s = State;
            if (s == null) return "table: none";
            var sb = new StringBuilder();
            sb.Append($"table#{s.TableId} phase={s.Phase}/{s.SubPhase} turn={s.TurnNo} owner={s.TurnOwner} action={s.Action} deck={s.DeckCount} top={s.DeckLastNo}");
            if (s.Players != null)
                foreach (var (pos, seat) in s.Players)
                    sb.Append($"\n  seat{pos} {seat.Nick}({seat.PlayerId}) hand=[{(seat.Hand == null ? "" : string.Join(",", seat.Hand))}] last={seat.LastMove} score={seat.Score}");
            if (s.Discards != null)
                foreach (var (pos, list) in s.Discards)
                    sb.Append($"\n  discard{pos}=[{string.Join(",", list)}]");
            return sb.ToString();
        }
    }
}
