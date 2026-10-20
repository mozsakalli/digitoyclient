// Eski digiplay GinTableModel : TableModel'in karşılığı (legacy-analysis/05 §1, §4). Kendi elin TEK sahibi:
//   snapshot (OnSnapshot) → cache'teki Hand'den REPLACE, HandSetVersion++   (eski refreshBoard)
//   delta    (OnDelta)    → Action'a göre YAMA + View'a hamle çağrısı       (eski updateGame switch → ctrl.throwCard/pickFromBoard)
// Kendi hamle: anında MyHand'e uygula + Pending kilidi (eski currentTurn=-500) + fire-and-forget gönder; echo idempotent uygulanır
// (varsa dokunma / yoksa ekle). Hiçbir yer cevabı BEKLEMEZ. Tutarsızlık (el sayısı, tekrar, atılan kart elde yok) → Clear değil Refresh
// (eski validationError). View'a bildirim doğrudan metot çağrısı; delegate/kuyruk yok, animasyonlar paralel.
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Digitoy.GinRummy
{
    /// <summary>Gin masa sahnesinin servise açtığı yüz (eski GinController hamle metotları). seat = ekran (relative) koltuğu, 0 = ben.
    /// Her çağrı: ilgili elemanı modelden idempotent yenile + animasyon; birbirinden bağımsız, paralel.</summary>
    public interface IGinTableView : ITableView
    {
        void PickFromDeck(int seat);
        /// <summary>card: yerden alınan kart.</summary>
        void PickFromDiscard(int seat, int card);
        /// <summary>card: atılan kart (yeni ıskarta tepesi). timeout: sunucu süre dolunca attı.</summary>
        void Discard(int seat, int card, bool timeout);
        void Pass(int seat);
        void Ready(int seat);
        void DeckEmpty();
        /// <summary>Knock/gin ilan edildi (Finishing); eller henüz açık değil. Skor snapshot'la (Refresh(true)) gelir.</summary>
        void Finishing(int seat);
    }

    public sealed class GinTableService : TableService
    {
        public static new GinTableService I => TableService.I as GinTableService;
        public new IGinTableView View { get => base.View as IGinTableView; set => base.View = value; }

        const int Pile = -1;                      // sunucu GinRummyTablePlugin.Pile
        // Kart kimliği kablo boyunca BENZERSİZ: 0-51 kartlar, 52 = Joker1, 53 = Joker2 (GinRummyShuffle, Common HandScore/HandSorter).
        // Bu yüzden el yaması küme mantığıyla (AddOnce/Contains) idempotent kalır; jokerli varyantta iki joker ayrı id ile girer/çıkar.
        // Eski digiplay'de iki joker aynı numaraydı ve CardLayer özel "duplicate joker" işliyordu — burada gerek yok. Validate() tekrar görürse Refresh.

        /// <summary>Elimdeki kartlar (sıra view'da; burada yalnız üyelik). Dışarıdan yazılmaz.</summary>
        public IReadOnlyList<int> MyHand => _hand;
        readonly List<int> _hand = new();
        /// <summary>Snapshot ile el replace edildikçe artar (eski HandSetVersion); view "elimi baştan kur" kararını buna göre verir.</summary>
        public int HandSetVersion { get; private set; }
        /// <summary>Hamle gönderildi, echo gelmedi: input kilidi.</summary>
        public bool Pending { get; private set; }

        int _pendingPick = -1;      // desteden/yerden istediğim kart (echo'da ele girer)
        int _pendingDiscard = -1;   // attığım kart (echo'da farklıysa sunucu süre dolunca benim yerime atmıştır)
        int _prevTop = -1;          // bu deltadan ÖNCEKİ ıskarta tepesi (PickFromDiscard'da alınan kart)
        int _validatedVersion = -1;

        // ── okuma yardımcıları ────────────────────────────────────────────────────
        public int DeckCount => CurrentState?.DeckCount ?? 0;
        public int DeckTop => CurrentState?.DeckLastNo ?? -1;
        public int TopDiscard => GetDiscard(Pile);
        public List<int> Discards => GetDiscardList(Pile);
        public TableSubPhase SubPhase => CurrentState?.SubPhase ?? TableSubPhase.None;
        public bool IsPassPhase => Phase == TablePhase.Playing && SubPhase == TableSubPhase.Pass;
        public bool IsPlaying => Phase == TablePhase.Playing;
        /// <summary>Çekebilir miyim: sıram, kilit yok, elimde 10.</summary>
        public bool CanPick => IsPlaying && IsMyTurn && !Pending && _hand.Count == 10;
        /// <summary>Atabilir miyim: sıram, kilit yok, elimde 11.</summary>
        public bool CanDiscard => IsPlaying && IsMyTurn && !Pending && _hand.Count == 11;
        public bool CanPass => IsPassPhase && IsMyTurn && !Pending;

        // ── kendi hamlelerim (optimistic + fire-and-forget; View'ı burada çağırmayız — sürükleyen view zaten biliyor, echo herkes için çağrılır) ──
        public void PickFromDeck()
        {
            if (!CanPick || DeckTop < 0) return;
            _pendingPick = DeckTop;
            AddOnce(_pendingPick);
            Pending = true;
            Send(GameAction.PickFromDeck, new List<long> { _pendingPick });
        }

        public void PickFromDiscard()
        {
            var card = TopDiscard;
            if (!CanPick || card < 0) return;
            _pendingPick = card;
            AddOnce(card);
            Pending = true;
            Send(GameAction.PickFromDiscard, new List<long> { card });
        }

        public void Discard(int card)
        {
            if (!CanDiscard || !_hand.Contains(card)) return;
            _hand.Remove(card);
            _pendingDiscard = card;
            Pending = true;
            Send(GameAction.Discard, new List<long> { card });
        }

        public void Pass()
        {
            if (!CanPass) return;
            Pending = true;
            Send(GameAction.Pass);
        }

        public void Ready() => Send(GameAction.Ready);

        /// <summary>Knock/gin: view'daki dizilişle 11 kart; atılacak kartı sunucu seçer (HandScore), echo'da elden düşer.</summary>
        public void RequestFinish(IReadOnlyList<int> orderedHand)
        {
            if (!CanDiscard || orderedHand.Count != 11) return;
            Pending = true;
            Send(GameAction.RequestFinish, ToParams(orderedHand));
        }

        /// <summary>Finishing'de savunan dizilişini gönderir (Rush'ta 10 kart olabilir).</summary>
        public void SendFinish(IReadOnlyList<int> orderedHand) => Send(GameAction.SendFinish, ToParams(orderedHand));

        /// <summary>Varsayılan Play Now: ilk lobi (id 1), o lobinin alt bahsi, standart kurallar (skor yoksayılır). Lobi tanımı Economy
        /// Events'te tag "lobby", MetaData.Params "skorlar|minBet,maxBet|basamaklar" (Unity LobbyManager/ToLobbyData ile aynı).</summary>
        public override Task<Result> PlayNowAsync()
        {
            var bet = FirstLobbyMinBet();
            var queries = GinFlags.Query().Where(Gin.Joinable, true).Where(Gin.LobbyId, 1).Where(Gin.Score, GinFlags.DefaultScore).Ignore().Build();
            return PlayNowAsync(queries, bet);
        }

        static long FirstLobbyMinBet()
        {
            var events = Config.CachedDefinitions?.Events;
            if (events != null)
                foreach (var ev in events.Values)
                {
                    if (ev.Tags == null || !ev.Tags.Contains("lobby")) continue;
                    var p = ev.MetaData?.Params;
                    if (string.IsNullOrEmpty(p)) break;
                    var parts = p.Split('|');
                    if (parts.Length > 1 && long.TryParse(parts[1].Split(',')[0].Trim(), out var min) && min > 0) return min;
                    break;
                }
            return 100;
        }

        static List<long> ToParams(IReadOnlyList<int> cards)
        {
            var p = new List<long>(cards.Count);
            foreach (var c in cards) p.Add(c);
            return p;
        }

        // ── türev kancaları ──────────────────────────────────────────────────────
        protected override void OnSnapshot()
        {
            Pending = false;
            _pendingPick = -1;
            _hand.Clear();
            var h = GetPlayer()?.Hand;
            if (h != null) _hand.AddRange(h);
            HandSetVersion++;
            _prevTop = TopDiscard;
            Validate();
        }

        protected override void OnCleared()
        {
            _hand.Clear();
            Pending = false;
            _pendingPick = -1;
            _prevTop = -1;
        }

        protected override void OnDelta(TableState incoming, int actorSeat)
        {
            var me = actorSeat == 0 && !IsSpectator;
            if (me) Pending = false;
            var action = incoming.Action ?? GameAction.None;
            switch (action)
            {
                case GameAction.PickFromDeck:
                    // Delta eli taşımaz: benimse istediğim kart girer (echo idempotent); sunucu süre dolunca benim yerime çektiyse kartı bilmem → Validate Refresh'e götürür.
                    if (me && _pendingPick >= 0) { AddOnce(_pendingPick); _pendingPick = -1; }
                    View?.PickFromDeck(actorSeat);
                    break;
                case GameAction.PickFromDiscard:
                    var picked = me && _pendingPick >= 0 ? _pendingPick : _prevTop;
                    if (me && picked >= 0) AddOnce(picked);
                    _pendingPick = -1;
                    View?.PickFromDiscard(actorSeat, picked);
                    break;
                case GameAction.Discard:
                case GameAction.DeckEmpty:
                    var thrown = TopDiscard;
                    var timeout = me && thrown != _pendingDiscard;             // ben göndermedim → sunucu süre dolunca attı
                    if (me && thrown >= 0) _hand.Remove(thrown);               // optimistic attıysam no-op; timeout'ta düşer
                    _pendingDiscard = -1;
                    View?.Discard(actorSeat, thrown, timeout);
                    if (action == GameAction.DeckEmpty) View?.DeckEmpty();
                    break;
                case GameAction.OpponentFinished:                          // RequestFinish echo'su/push'u: knock kartı ıskartaya gitti
                    var knock = TopDiscard;
                    if (me && knock >= 0) _hand.Remove(knock);
                    View?.Finishing(actorSeat);
                    break;
                case GameAction.Pass:
                    View?.Pass(actorSeat);
                    break;
                case GameAction.Ready:
                    View?.Ready(actorSeat);
                    break;
            }
            _prevTop = TopDiscard;
            Validate();
        }

        void AddOnce(int card) { if (!_hand.Contains(card)) _hand.Add(card); }

        /// <summary>El invariantları (eski CardLayer validationError, burada modelde): Playing'de oturuyorsam el 10 ya da 11, tekrar yok,
        /// sıram ve çekmişsem 11. İhlal → log + Refresh (Clear değil). Aynı Version için bir kez.</summary>
        void Validate()
        {
            if (_validatedVersion == Version) return;
            _validatedVersion = Version;
            if (!IsPlaying || IsSpectator || GetMyPosition() < 0) return;
            var ok = _hand.Count is 10 or 11 && !HasDuplicate(_hand);
            if (ok) return;
            Debug.LogWarning($"GinTableService: el tutarsız (n={_hand.Count}, action={CurrentState?.Action}) → Refresh");
            RefreshAsync().Forget();
        }

        static bool HasDuplicate(List<int> list)
        {
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                    if (list[i] == list[j]) return true;
            return false;
        }

        /// <summary>Oyun yüklenince gameId → servis kaydı (Shared oyun adı bilmez; AppBoot TableServices.Create ile kurar).</summary>
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register() => TableServices.Register(7, static () => new GinTableService());

        public string Describe()
        {
            var s = CurrentState;
            if (s == null) return "table: none";
            var sb = new System.Text.StringBuilder();
            sb.Append($"table#{s.TableId} phase={s.Phase}/{s.SubPhase} turn={s.TurnNo} owner={s.TurnOwner} action={s.Action} deck={DeckCount} top={DeckTop} pending={Pending}");
            sb.Append($"\n  myHand=[{string.Join(",", _hand)}] v{HandSetVersion} discard=[{string.Join(",", Discards ?? new List<int>())}]");
            if (s.Players != null)
                foreach (var (pos, seat) in s.Players)
                    sb.Append($"\n  seat{pos} {seat.Nick}({seat.PlayerId}) last={seat.LastMove} score={seat.Score}");
            return sb.ToString();
        }
    }
}
