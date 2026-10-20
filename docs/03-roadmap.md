# Yol haritası — DigitoyClient

Durum 2026-10-16, taslak. Fazlar sıralı; her fazın sonunda **kabul kriteri** gerçek sunucuya karşı doğrulanır
(sunucu simülasyonu yazılmaz). İlk oyun **Gin Rummy** (✅ kullanıcı). Süre tahmini verilmedi — her faz başında
game_rd/digitoyclient oturumunda ayrı plan + onay.

## Faz 0 — İskelet + dikey dilim (UI yok)
Revize 2026-10-16 (decisions.md): Common.dll hazır alınır, core.upm adapte edilir. **Adım 0 (keşif) tamam** — motor yüzeyi
(02 §sonu), Common yüzeyi (04), sunucu: test-node.digitoy.co ayakta, gateway listesi settings JSON'dan.
1. Klasör iskeleti `Assets/{Shared/{Scenes,Services},Games/GinRummy,Plugins}`; `Main.scene` → `Shared/Scenes/Entry.scene`;
   README "Açma" güncellenir.
2. `Assets/Plugins/Digitoy.Common.dll` ✔ (kullanıcı verdi, 2026-10-14 build) + `Digitoy.Common.version.md`.
3. **core.upm adaptasyonu (kopyala + Unity'den arındır, davranış aynı):** `Helpers/{Result,Service,Log,DeviceKeyHelper→stub}`,
   `Network/{Server,ServerDirectory,NetworkClient,ClientAuthParams,NetworkDelay}`, `Components/{Player,Game/Table,Game/Lobby,
   Auth(Guest yolu)}`, `DeviceKey` (PlayerPrefs GUID fallback), `Extensions/DataExtensions`. Kalıplar: `Awaitable`→`Task`,
   `Awaitable.NextFrameAsync`→`Frame.Next()`, `MainThreadAsync` silinir, `Debug`→`Log`, `SystemInfo/Application`→`Platform` shim,
   `GameProperties : ScriptableObject`→`[Asset] GameProperties` (yalnız ağ alanları).
4. **WS taşıma katmanı:** `BestWebSocketClient` → `EngineWebSocketClient` (motor `WebSocket`; `ReceiveAsync` döngüsü tek-thread,
   kilitler kalkar). Korunur: gateway hop + bütçe, requestId→TCS eşleme, 25 sn heartbeat, auth-on-open, `Connection` nesli, close nedeni çözümü.
5. `Games/GinRummy/Game.asset` (`[Asset] GameDefinition`: gameId=7, maxSeats, sahne ref'leri) + `Shared/Settings.asset`
   (`NetworkSettings`) + `Shared/Services/Game.cs` (`Current`, Faz 0'da `Switch` yok); `AppVariant` motor A1 bekler.
   `GinTable.cs` minimal projeksiyon.
6. `Shared/Scenes/Entry.scene` + geçici `Bootstrap` component: Play'de Guest login → PlayNow → Ready → sıra gelince
   PickFromDeck + Discard → log. Faz 1'de Entry akışıyla değişir.
7. AOT doğrulaması (02 §A3): `player_build` ile aynı dilim; `Serializers`/`CodecName` trim'de sağ mı.
8. ~~game_rd `Projects/yuzbirextra` tohumları → `Shared/`~~ İPTAL (2026-10-16): game_rd projeleri motor playground'udur, buradan
   bir şey alınmaz. Fontlar kullanıcı tarafından `Assets/Shared/Fonts/` altına kondu (Digitoy-{Primary,Bold,Regular,Light}).
- `Config`/`ServerDirectory` settings JSON: Faz 0'da sabit seed (`test-node.digitoy.co`), JSON okuyucu (02 §B2) Faz 1 başı.
- **Kabul:** editör Play'de konsol/`scene.dump` ile: test-node'a login → `TablePlayNow` → bir tur
  `PickFromDeck`/`Discard` → `TableState` decode edildi. Hiçbir UI yok.

## Faz 1 — Ortak kabuk (Shared kod) + Gin sahneleri
Durum 2026-10-16 gece: **Login tamam** (Unity'den aktarım, `Games/GinRummy/Scenes/Login.scene` + intent.md), `LoginFlow`/`AppBoot`/
`SceneNav`/`Button` Shared'da, Guest login → `Lobby.scene` (placeholder) geçişi test-node'a karşı çalışıyor. Entry sahnesi
kaldırıldı (startScene = Login; AppBoot.Ensure LoginFlow'da; AppVariant gelince startScene varyanttan). Login eksikleri intent.md'de.
- Entry (AppVariant okur, splash) → `Games/GinRummy/Scenes/Login` (device + "sosyal" butonları stub) → `Lobby` (Play Now,
  masa listesi `ScrollView`, profil başlığı, bakiye, ayarlar). Controller'lar Shared'da, sahneler Gin klasöründe.
- `DialogStack` + Dialog taban prefab'ı + MovieClip aç/kapa; ilk dialoglar: Prompt, Settings, Profile, Nick (TextField).
- Tema: `Shared/Theme/Default.theme` token'ları; Gin teması override örneği.
- Motor: TextField (02 §B1), safe-area (B3), küçük native yardımcılar (B4); C6 çevirici kararı; C7 locale içe aktarımı.
- **Kabul:** Gin varyantı Android cihazda açılır, login olur, lobide masa listesi görünür, Play Now masaya götürür
  (masa sahnesi boş). iOS/wasm build'i en az açılır.

## Faz 2 — Gin Rummy masası
- **Önce senkron temeli** (decisions 2026-10-20, legacy-analysis/05 §4) — **Adım 1–2 tamam:** `TableService` (`Version/SnapshotCount`,
  `Apply()`, kancalar, `ITableView`), `Games/GinRummy/GinTableService.cs` (el sahibi, `IGinTableView`), `TableServices` kaydı.
  Sırada: kayıtlı delta akışı testleri; UI bu tabanın üstüne gelir; her adım ayrı onay.
- `GinTable` (TableState projeksiyonu; tek state machine), kart atlası, `Hand/DeckPile/DiscardPile/MeldZone/Seat/Timer`
  component'leri; deal/draw/discard/knock/gin/round-over koreografileri MovieClip; drag/drop + ipucu Common `HandScore`.
- Finish payload sözleşmesi (11 kartın **sıralı** listesi), Rush varyantı (yalnız sunucu zamanı), Slam (istemci sunumu).
- Round-over / game-over dialogları, rematch, chat/emoji (TextField), masa ayarları.
- **Kabul:** iki gerçek istemci (biri Unity prod) aynı masada tam oyun oynar; skorlar sunucuyla birebir;
  reconnect sonrası masa doğru geri yüklenir. Ekip görsel kabulü (screenshot turu + cihaz).

## Faz 3 — Yüzbir Okey
- `Shared/Board` (ya da `Games/OkeyCommon`): ıstaka, taş, sürükleme, gösterge, orta deste — Okey ile paylaşılacak
  şekilde; `YuzbirTable` + Common `YuzbirGame/OkeyCheck/Meld`; `yuzbirextra.unity` `Logic` asmdef'i (MeldStateSnapshot,
  UsefulMeldPlanner) **saf C# olduğu için doğrudan alınır**.
- Yüzbir `Game.asset`, `Scenes/{Login,Lobby}` (Gin sahnelerinden kopyalanıp farklılaştırılır), lobi kartları;
  Gin lobisine "Okey oyna" → `Game.Switch` (relogin) ilk kez burada çalışır.
- **Kabul:** Faz 2 ile aynı ölçüt (Unity yuzbir istemcisiyle karşılıklı oyun).

## Faz 4 — Okey (klasik)
- Sunucu tarafı: `OkeyTablePluginBase` üstüne klasik Okey plugin'i **yok** → digitoyserver'da ayrı iş
  (referans: `digiplay-net/OkeyExtra/table/OkeyCheck.cs`). Common'a `Okey` kural klasörü.
- İstemci: Faz 3 board'u + `OkeyTable`; farklar (gösterge/okey, çift açma, bitiş puanı) oyun klasöründe.
- **Kabul:** sunucu plugin'iyle uçtan uca oyun.

## Faz 5 — Mağaza sürümü (native köprüler + meta)
- Native plugin iskeleti → IAP → reklam → Facebook/Apple login → push → Play Integrity/DeviceKey → Adjust.
- Meta özellikler (öncelik ürün kararı): Store/Offers → günlük bonus/misyon → sosyal (arkadaş/hediye/inbox) →
  leaderboard/sezon → mini oyunlar (`Module.LoadAsync` ile ayrı pak — 🔶).
- **Kabul:** Gin varyantı store'a yüklenebilir build; prod sunucuya karşı satın alma + reklam ödülü çalışır.

## Faz 6+ — Yeni oyunlar
Yeni oyun = `Games/<Oyun>/` + `Variant.asset` + sunucu plugin'i; Shared'a dokunmadan çıkabilmesi hedef.

## Açık sorular (kullanıcı/ekip)
1. Hedef platform sırası: Android → iOS → wasm? (native köprü önceliği, Faz 1 kabulü)
2. ~~`Config`/`ServerDirectory` JSON mu Codec mi?~~ JSON (kesin, decisions 2026-10-16).
3. Gin bot/offline oyun ilk sürümde var mı? (🔶 yok)
4. Faz 5 meta özelliklerinden ilk sürümde zorunlu olanlar (mağaza/reklam dahil mi)?
5. Unity prefab çevirici (02 §C6) yatırımı yapılacak mı?
6. Helpshift/Firebase (messaging dışı) kapsam dışı mı?
7. Yüzbir Unity istemcisi production'a çıkacak mı, yoksa doğrudan bu projeden mi çıkacak? (Faz 3 önceliğini etkiler)
