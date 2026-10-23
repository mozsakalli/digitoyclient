// World.Gin: Gin Rummy'nin (gameId 7) sunucu aynasındaki oyuna özel kısmı. Sunucu GinRummyTablePlugin'in client yansıması:
//   - Hamle OPTİMİSTİK: aynayı (elim, ıskarta, deste, LastMove) hemen yama → World.Act ile gönder; cevap beklenmez. Sunucunun cevabı
//     aynı delta olarak push yolundan aynaya girer (ıskarta/deste sunucu değeriyle ezilir, el delta'da yoktur → yerel yama kalır).
//   - Sunucu hamle deltalarında ELİ GÖNDERMEZ (yalnız Join/Refresh/Start/Finish/ResetSeat tam görünümünde). Reflect(): deltanın
//     anlamını ele yansıtır (rakibin hamlesi için gereksiz; benim hamlemde optimistik yama zaten yapmıştır → idempotent).
//   - Sunucu timeout'ta benim yerime OYNAMAZ, masadan atar; o yüzden "sunucu benim yerime çekti" dalı yok.
//   - Validate(): Playing'de oturuyorsam el 10/11 ve tekrarsız; ihlal → Refresh (Clear değil). InvalidMove cevabı → World.ActAsync Refresh.
// Kart kimliği kablo boyunca benzersiz (0-51, 52/53 joker) → el yaması küme mantığıyla idempotent.
using System.Collections.Generic;
using DigitoyEngine;
using M;

namespace Digitoy
{
    public static partial class World
    {
        public static class Gin
        {
            public const int GameId = 7;
            /// <summary>Sunucu GinRummyTablePlugin.Pile: tek ıskarta yığını anahtarı.</summary>
            public const int Pile = -1;

            // ── okuma ────────────────────────────────────────────────────────────
            public static int DeckCount => Table?.DeckCount ?? 0;
            public static int DeckTop => Table?.DeckLastNo ?? -1;
            public static List<int> Discards => Table?.Discards != null && Table.Discards.TryGetValue(Pile, out var d) ? d : null;
            public static int TopDiscard { get { var d = Discards; return d is { Count: > 0 } ? d[^1] : -1; } }
            public static bool IsPlaying => Phase == TablePhase.Playing;
            public static bool IsPassPhase => IsPlaying && SubPhase == TableSubPhase.Pass;
            /// <summary>Çekebilir miyim: Playing, sıram, elimde 10 (çektiysem 11 olur → doğal kilit).</summary>
            public static bool CanPick => IsPlaying && IsMyTurn && MyHand.Count == 10;
            public static bool CanPickFromDiscard => CanPick && TopDiscard >= 0;
            public static bool CanPickFromDeck => CanPick && !IsPassPhase && DeckTop >= 0;
            /// <summary>Atabilir miyim: Playing, sıram, elimde 11.</summary>
            public static bool CanDiscard => IsPlaying && IsMyTurn && MyHand.Count == 11;
            public static bool CanPass => IsPassPhase && IsMyTurn && MySeat?.LastMove != GameAction.Pass;

            // ── hamleler (optimistik + fire-and-forget). false = izin yok: view görseli geri alır, sunucuya hiçbir şey gitmez. ──
            public static bool PickFromDeck()
            {
                if (!CanPickFromDeck) return false;
                var card = DeckTop;
                AddOnce(MySeat.Hand ??= new(), card);
                Table.DeckCount = DeckCount - 1;
                Table.DeckLastNo = null;                 // yeni tepe sunucudan gelir
                Touch(GameAction.PickFromDeck);
                Act(GameAction.PickFromDeck, new List<long> { card });
                return true;
            }

            public static bool PickFromDiscard()
            {
                if (!CanPickFromDiscard) return false;
                var pile = Discards;
                var card = pile[^1];
                pile.RemoveAt(pile.Count - 1);
                AddOnce(MySeat.Hand ??= new(), card);
                if (IsPassPhase) Table.SubPhase = TableSubPhase.Idle;
                Touch(GameAction.PickFromDiscard);
                Act(GameAction.PickFromDiscard, new List<long> { card });
                return true;
            }

            public static bool Discard(int card)
            {
                if (!CanDiscard || MySeat.Hand is not { } hand || !hand.Contains(card)) return false;
                hand.Remove(card);
                (Table.Discards ??= new()).TryGetValue(Pile, out var pile);
                if (pile == null) Table.Discards[Pile] = pile = new List<int>();
                pile.Add(card);
                Touch(GameAction.Discard);
                Act(GameAction.Discard, new List<long> { card });
                return true;
            }

            public static bool Pass()
            {
                if (!CanPass) return false;
                Touch(GameAction.Pass);
                Act(GameAction.Pass);
                return true;
            }

            /// <summary>Knock/gin: view'daki dizilişle 11 kart; atılacak kartı sunucu seçer (HandScore), OpponentFinished deltasında elden düşer.</summary>
            public static bool RequestFinish(IReadOnlyList<int> orderedHand)
            {
                if (!CanDiscard || orderedHand.Count != 11) return false;
                Act(GameAction.RequestFinish, ToParams(orderedHand));
                return true;
            }

            /// <summary>Finishing'de savunan dizilişini gönderir (Rush'ta 10 kart olabilir).</summary>
            public static void SendFinish(IReadOnlyList<int> orderedHand) => Act(GameAction.SendFinish, ToParams(orderedHand));

            public static void Ready() => ReadyAsync().Forget();

            // ── sunucu deltasının ele yansıması (ApplyTable sonunda, delta yolunda) ──
            /// <param name="mine">deltanın aktörü benim koltuğum.</param>
            /// <param name="prevTop">delta uygulanmadan ÖNCEKİ ıskarta tepesi.</param>
            internal static void Reflect(GameAction action, bool mine, int prevTop)
            {
                if (!mine || MySeat is not { } me) return;
                switch (action)
                {
                    case GameAction.PickFromDiscard:
                        if (prevTop >= 0) AddOnce(me.Hand ??= new(), prevTop);
                        break;
                    case GameAction.Discard:
                    case GameAction.DeckEmpty:
                    case GameAction.OpponentFinished:          // RequestFinish cevabı: knock kartı ıskartaya gitti
                        var thrown = TopDiscard;
                        if (thrown >= 0) me.Hand?.Remove(thrown);
                        break;
                }
            }

            /// <summary>El invariantları: Playing'de oturuyorsam el 10 ya da 11, tekrar yok. İhlal → log + Refresh. Aynı TableVersion için bir kez.</summary>
            internal static void Validate()
            {
                if (_validatedVersion == TableVersion) return;
                _validatedVersion = TableVersion;
                if (!IsPlaying || MyPosition < 0) return;
                var hand = MyHand;
                if (hand.Count is 10 or 11 && !HasDuplicate(hand)) return;
                Debug.LogWarning($"[world] Gin: el tutarsız (n={hand.Count}, action={Table?.Action}) → Refresh");
                RefreshAsync().Forget();
            }
            static int _validatedVersion = -1;

            // ── yardımcılar ───────────────────────────────────────────────────────
            static void Touch(GameAction move)
            {
                MySeat.LastMove = move;
                Table.Action = move;
                Dirty(WorldPart.Table);
                TableVersion++;
                Begin(); End();   // tur dışında çağrıldık (view input'u) → Changed(Table) hemen dağıtılsın
            }

            static void AddOnce(List<int> hand, int card) { if (!hand.Contains(card)) hand.Add(card); }

            static bool HasDuplicate(List<int> list)
            {
                for (var i = 0; i < list.Count; i++)
                    for (var j = i + 1; j < list.Count; j++)
                        if (list[i] == list[j]) return true;
                return false;
            }

            static List<long> ToParams(IReadOnlyList<int> cards)
            {
                var p = new List<long>(cards.Count);
                foreach (var c in cards) p.Add(c);
                return p;
            }
        }
    }
}
