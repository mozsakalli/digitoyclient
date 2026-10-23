// World olay kataloğu: sunucudan gelen mesaj akışından türetilen ANLAMLI olaylar. Üç kaynak: (1) sunucunun açık işaretleri
// (TableState.Action, SeatPreview.LastMove, PlayerPreview.FriendshipAction, EventExecutionResult.EventId, CloseReason),
// (2) model farkı (faz, sıra, koltuk, envanter, arkadaş online), (3) zaman (EventState.NextAt, InventoryItem.ResetTime).
// Değişmez: olay fırladığında World modelleri ZATEN günceldir. Olaylar opsiyonel ipucudur (animasyon/ses/toast); hiçbir
// view olaya bağımlı doğruluk taşımaz — Refresh World'den okuyarak her zaman doğru görüntüyü kurar.
using M;

namespace Digitoy
{
    public enum Ev
    {
        None = 0,

        // ── bağlantı ──
        Connecting,
        Connected,          // soket + auth tamam (Amount=1 reconnect sonrası, 0 ilk bağlantı)
        SessionLoading,     // auth ok; definitions + player state + masa yükleniyor
        SessionReady,       // definitions + player state + masa yüklendi
        Reconnecting,       // Amount = deneme / Aux = en fazla
        Reconnected,        // reconnect sonrası oturum yeniden yüklendi
        Disconnected,       // Reason; Flag = internet yok
        Killed,             // Reason: Logout/Unauthorized/AnotherLogin/Kicked/IdleTimeout
        Dead,               // denemeler bitti

        // ── masa: ben ──
        TableEntered,       // masaya girdim (oturdum ya da izliyorum)
        TableLeft,          // masadan çıktım; Flag = atıldım
        TableSnapshot,      // tam görünüm uygulandı (join/refresh/el başı) — view her şeyi modelden yeniden kurar
        MyTurn,
        // ── masa: koltuk (Seat = sunucu pozisyonu) ──
        SeatJoined,
        SeatLeft,           // Flag = atıldı
        SeatUpdated,        // avatar/stats/nick
        SeatReady,
        SeatChat,           // Text
        // ── masa: akış ──
        PhaseChanged,       // From → To
        GameStarted,        // Waiting/Starting → Playing (yeni oyun)
        RoundStarted,       // RoundOver → Playing
        RoundOver,
        GameOver,
        TurnChanged,        // Seat = yeni sıra sahibi
        Move,               // oyuna özel aksiyon (Pick/Discard/Pass/…): Seat = aktör (-1 bilinmiyor), Move = aksiyon

        // ── oyuncu / ekonomi ──
        PlayerLoaded,       // PlayerState (ilk ya da reconnect) uygulandı
        InventoryChanged,   // Id = item, Amount = Δ
        InventoryReset,     // dönemsel eşya sıfırlandı (ResetTime geçti)
        EventApplied,       // Id = eventId (sunucu event sonucu uygulandı; günlük bonus alındı vb.)
        EventProgress,      // Id = eventId; EventState yenilendi
        EventAvailable,     // Id = eventId; cooldown/cycle sonu geçti → tekrar alınabilir
        LeaderboardChanged, // Id

        // ── sosyal ──
        FriendsLoaded,
        FriendOnline,       // PlayerId
        FriendOffline,
        FriendMoved,        // PlayerId; Text = Location (masa)
        FriendUpdated,      // ChangeLook
        FriendRequest,
        FriendAccepted,
        FriendRemoved,
        MessageReceived,    // PlayerId = gönderen
        ChallengeReceived,  // PlayerId; Text = Location
        SupportUnread,      // Amount

        // ── lobi ──
        OnlineStatsChanged,

        // ── istek ──
        RequestFailed,      // Move = istenen aksiyon, Text = ErrorCode
    }

    /// <summary>Tek olay kaydı (alloc yok). Alan anlamı Ev'e göre; kullanılmayanlar varsayılan.</summary>
    public readonly struct WorldEvent
    {
        public readonly Ev Kind;
        public readonly int Seat;           // sunucu koltuk pozisyonu; -1 = yok/bilinmiyor
        public readonly long PlayerId;
        public readonly GameAction Move;
        public readonly string Id;          // item / event / leaderboard id
        public readonly string Text;        // chat / location
        public readonly long Amount;        // Δ miktar, attempt, unread
        public readonly int Aux;            // max attempt vb.
        public readonly TablePhase From, To;
        public readonly CloseReason Reason;
        public readonly bool Flag;

        public WorldEvent(Ev kind, int seat = -1, long playerId = 0, GameAction move = GameAction.None, string id = null,
            string text = null, long amount = 0, int aux = 0, TablePhase from = TablePhase.None, TablePhase to = TablePhase.None,
            CloseReason reason = default, bool flag = false)
        {
            Kind = kind; Seat = seat; PlayerId = playerId; Move = move; Id = id; Text = text; Amount = amount; Aux = aux;
            From = from; To = to; Reason = reason; Flag = flag;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder(Kind.ToString());
            if (Seat >= 0) sb.Append(" seat=").Append(Seat);
            if (PlayerId != 0) sb.Append(" pid=").Append(PlayerId);
            if (Move != GameAction.None) sb.Append(' ').Append(Move);
            if (Id != null) sb.Append(" id=").Append(Id);
            if (Text != null) sb.Append(" \"").Append(Text).Append('"');
            if (Amount != 0) sb.Append(" n=").Append(Amount);
            if (Aux != 0) sb.Append('/').Append(Aux);
            if (From != TablePhase.None || To != TablePhase.None) sb.Append(' ').Append(From).Append("→").Append(To);
            if (Reason != default) sb.Append(' ').Append(Reason);
            if (Flag) sb.Append(" !");
            return sb.ToString();
        }
    }

    /// <summary>World'ün hangi parçası değişti (Changed bildirimi).</summary>
    [System.Flags]
    public enum WorldPart { None = 0, Connection = 1, Table = 2, Player = 4, Friends = 8, Online = 16 }
}
