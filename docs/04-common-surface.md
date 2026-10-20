# Common.dll yüzeyi — istemcinin kullandığı kısım

Kaynak: `Assets/Plugins/Digitoy.Common.dll` (1.0.0.0, 2026-10-14 22:46, 257 KB) reflection dökümü (2026-10-16).
Common hazır alınır, burada model/Codec işi yapılmaz (bkz. decisions.md). Tam döküm oturum dosyasında; burada yalnız
istemcinin Faz 0–2'de dokunduğu yüzey.

## Codec / çerçeve
- `M.Codec(isClient)`: `BeginEncoding()` → `Encode(long requestId)`, `Encode((long)endpoint)`, `EncodeObject(obj)` →
  `GetBuffer()` (ArraySegment). Alış: `BeginDecoding(buf,len,off)` → `TryDecodeNumber()` requestId → `DecodeObject()`.
  `requestId == -1` push, `0` ping cevabı. `ZThreshold = 256` (üstü deflate; Codec kendi `Inflater/Deflater`'ı ile).
- `M.Serializer<T>.GetSerializer()`, `M.Serializers.ForType/ForName` — generator'lı; istemci doğrudan çağırmaz, `EncodeObject/DecodeObject` kullanır.
- `[CodecName]` tipleri kısa alan adlı dict olarak gider; `Encoded.Of<T>/Decode<T>` sunucu tarafı.

## Uçlar (`M.Endpoint`, `GetEndpoint(id)` yolu verir)
| id | ad | yol | kullanım |
|---|---|---|---|
| 0 | AuthLogin | api/auth/login | WS açılınca ilk istek: `AuthRequest` → `AuthResult` |
| 1 | AuthLogout | api/auth/logout | |
| 2 | WebsocketConnect | api/websocket/connect | WS URL: `wss://<gateway>/api/websocket/connect` (localhost/LAN için `ws`) |
| 100–102 | ConfigGet/Definitions/Remote | api/config/* | `ConfigResult{Definitions, Json, Version}` |
| 200 | PlayerGetState | api/player/getstate | `PlayerState` |
| 201 | PlayerGetProfile | api/player/profile | `ProfileRequest` → `PlayerPreview` |
| 300 | TableList | api/table/list | `TableListRequest` → `TableListResponse{Tables}` |
| 301/302/303 | TableCreate/Join/Leave | | `GameActionRequest` (Action=CreateTable/JoinTable/LeaveTable) |
| 304 | TableAction | api/table/move | `GameActionRequest{Action, Params}` → `GameActionResult` |
| 305 | TablePlayNow | api/table/playnow | `GameActionRequest{Queries, Bet…}` |
| 310–314 | Lobby* | | `OnlineStatsRequest/Response`, `AvailablePlayersRequest/Response` |
| 400 | EventExecute | | `EventRequest` → `EventExecutionResult` |
| 500 | LeaderboardGet | | |
| 600/601 | Friendship* | | |
| 700–704 | Message* | | `SendMessageRequest/Result`, `ConversationRequest/Result`, `UnreadsResult` |
| 800 | Ping | api/ping | heartbeat (uygulama çerçevesi; sunucu 60 sn'de düşürür, istemci 25 sn) |

HTTP (WS dışı): `GET {base}/api/config/{gameId}/settings?version=&uid=&t=` → JSON
`{version, settings{ios_force_version, android_force_version, maintenance, policy_version, privacy_url, terms_url, support_url}, gateways[], seeds[], serverTime}`.
Test: `https://test-node.digitoy.co` (gameId Gin = 7), prod `https://api.digitoy.co`.

## Auth
- `AuthRequest{GameId, Token, FbToken, AppleToken, ReferralCode, Params}`; `Params` anahtarları `AuthRequestParams`:
  `deviceKey, dkMethod, sdeviceKey, sdkMethod, os, appVersion, platform, migrationKey0..5, keep` (+ istemci analitik
  anahtarları core.upm `ClientAuthParams`: model, connection, carrier, emulatorScore, source…).
- `AuthResult{Ok, Token, PlayerId, Error?, Ban, Current, Target}`; `ErrorCode.AccountConflict` → Current/Target seçimi.
- `CloseEvent{Reason: CloseReason, CurrentToken}`; `CloseReason` 4000–4010 (Normal, Unauthorized, Forbidden, IdleTimeout,
  AnotherLogin, Kicked, Aborted, Logout, ProxyShutdown, SiloUnavailable, GrainDeactivated).

## Masa
- `GameAction`: ortak 1–10 (PlayerJoined, PlayerLeft, Start, Timeout, Finish, PlayerKicked, Refresh, GameTimeout,
  PlayerUpdated, UpdatePlaytime); Gin 700–710 (Pass, Ready, PickFromDeck, PickFromDiscard, Discard, RequestFinish,
  OpponentFinished, SendFinish, NewGame, NewRound, DeckEmpty); 1000–1003 PlayNow/CreateTable/JoinTable/LeaveTable;
  Okey 11/15/100–102; Poker 200–204.
- `GameActionRequest{GameId, PlayerId?, TableId?, Bet?, Queries: List<FlagQuery>, Action?, Params: List<long>, Source, Item, MinBet?, MaxBet?}`
- `GameActionResult{Error?, TableState, CallerEvent: TableExecutionEvent, Ok}`
- `TableState{TableId, GameId, TurnNo, TurnOwner, Phase: TablePhase, SubPhase: TableSubPhase, Players/Spectators:
  Dictionary<int,SeatPreview>, Deck, DeckCount, DeckLastNo, Discards: Dictionary<int,List<int>>, Flags, Bet, Jackpot,
  Action?, ServerTime, TimeoutAt, GameEndsAt, PluginState: Dictionary<string,long>}`
- **Delta sözleşmesi (Common'da yazılı değil, sunucu kodundan çıkarıldı — legacy-analysis/05 §3):** `null` alan = değişmedi
  (`Hand=null` = maskeli/değişmedi, `Phase=None` = değişmedi); `Hand=[]` = boş; `Players` kısmi → koltuk bazlı merge, ayrılma
  `LastMove=PlayerLeft/Kicked`; boş cevap/`TableId=0` = Clear; **kendi `Hand` yalnız Join/Refresh/Start/Finish'te** (çek/at/aç
  deltaları eli taşımaz); `TurnNo` Yüzbir'de bit alanı (pick/gösterge/round), seq değil; Yüzbir board'u `Discards[4/5/6/21/35]`;
  `PluginState` istemciye gitmez. **Eksik → digitoyserver işi:** `TableState.Seq`, sözleşmenin doc-comment olarak yazılması.
- `TablePhase`: None, Waiting, Starting, Dealing, Playing, Finishing, RoundOver, GameOver. `TableSubPhase`: None, Idle, ReadyCheck, Pass.
- `SeatPreview{PlayerId?, FacebookId?, Nick, Stats, Balance?, Hand: List<int>, LastMove?, Score?, Position, EventResult, Message}`
- `Gin` bayrakları (long): Straight 256, Joker 512, Rush 1024, Private 2048, Joinable 4096, Score, LobbyId, NoShuffle;
  `GinCreateBuilder.Set(...).Build()` → `List<FlagQuery>`; `GinFlags` extension'ları.
- Kurallar: `M.GinRummy.{CardHelper, HandScore, HandSorter, RoundScorer, LayoffResult, RoundResult, MeldType, RoundOutcome}`;
  `M.Yuzbir.*`, `M.Okey.OkeyCheck`; `M.NickValidator`.

## Oyuncu
- `PlayerState{PlayerId, Nick, FacebookId, AppleId, Inventory: Dictionary<string,InventoryItem>, EventStates,
  LeaderboardStates, ServerTime, ReferralCode, CurrentTableId, Friends…}`
- `PlayerPreview{PlayerId, Nick, Location, Online?, Stats, FriendshipStatus?, Rank?, Score?, UnreadMessageCount?}`
