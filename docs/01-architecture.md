# Hedef mimari — DigitoyClient

Durum: **taslak, 2026-10-16**. ✅ = kullanıcı kararı, 🔶 = öneri (onay bekliyor). Kararlaşınca bu dosya güncellenir;
kalıcı karar günlüğü [decisions.md](decisions.md); Common yüzeyi [04-common-surface.md](04-common-surface.md).

## 1. Temel ilkeler
1. ✅ **Tek kaynak ağacı, tek build, oyun başına uygulama kimliği.** Build **tüm oyunları** içerir; `AppVariant` yalnız
   mağaza kimliğini (bundleId, ikon, ad) ve **başlangıç oyununu** seçer. Oyuncu runtime'da oyun değiştirebilir (lobide
   "Okey oyna" → relogin). Sahne görünümü, tema, içerik oyuna göre tamamen değişebilir — paylaşım zorunluluk değil imkân.
2. **Sunucu otoriter, istemcide tek durum modeli.** Kurallar `Digitoy.Common` (`M.*`) — istemci kural kopyası
   yazmaz; ipucu/bot/UI uygunluğu da Common üstünden. Unity istemcisinin "3 kopya" hatası tekrarlanmaz.
3. **Motor değişmezleri aynen** (game_rd `docs/engine-notes.md`): doc = kaynak, runtime sahneye sızmaz; hot path
   sıfır alloc; koreografi MovieClip (doc'ta), tepkisel hareket Tween; edit modunda çalışması gereken görsel
   component `[ExecuteInEditMode]`; veri = `[Asset]` sınıf → `.asset`, referans GUID.
4. **core.upm adapte edilir, Unity UI yeniden yazılır** (decisions 2026-10-16). Servis/ağ mantığı kopyalanıp Unity'den
   arındırılır; MonoBehaviour/DOTween/Canvas dokusu taşınmaz.
5. **Oyun oturumları tam izole, aynı anda tek oyun.** Aynı deviceKey + farklı gameId = farklı player; PlayerState/
   Inventory/Config oyun başına. Oyun değişimi = logout + servis reset + yeni gameId ile Guest login.

## 2. Klasör sözleşmesi
```
digitoyclient/
  Assets/
    Shared/                    tüm oyunların ortak kabuğu — KOD + PARÇA PREFAB; oyun adı geçmez; sahne yok (Entry hariç)
      Scenes/   Entry.scene (AppVariant okur → startGame.loginScene)
      Services/ Server, Auth, Session, Game (Current/Switch), PlayerService, LobbyService, TableService, MessagingService,
                Friends, Economy, Config, TimeSync, DeviceKey, Prefs      ← core.upm/Components + Network adaptasyonu, saf C#
      Ui/       DialogStack, Dialog çerçeve prefab'ı, Button/Toggle/Counter/Avatar/Toast, ScrollList kalıpları, TextField kullanımı
      Controllers/ LoginFlow, LobbyController, ProfileDialog, StoreDialog… (yerleşim bilmez; sahnedeki GO'lara ref ile bağlanır)
      Theme/    BoxStyle/Theme asset'leri, Fonts/, Effects/ (*.fx), ortak atlas
      Icons/    genel ikonlar (SVG) — tek kopya, oyun klasörüne ikon kopyalanmaz
      Audio/    ortak SFX
      Localization.asset  kabuk metinleri (shell.*)
    Games/
      GinRummy/  Game.asset (GameDefinition), Scenes/{Login,Lobby,Table}.scene(+intent), Dialogs/ (oyuna özel kompozisyonlar),
                 Cards/ atlas, Components/ (GinTable, Hand, DeckPile, MeldZone, Seat…), MovieClips/, Localization.asset (gin.*)
      Yuzbir/    Game.asset, Scenes/, Tiles/, Board (Okey ile paylaşılan taban), Components/
      Okey/      Game.asset, Scenes/, Yuzbir'in Board tabanını kullanır
    Plugins/
      Digitoy.Common.dll       hazır alınır (digitoyserver build çıktısı); sürüm notu yanında
  ProjectSettings/  PlayerSettings.asset (ortak) + Variants/<ad>.asset (AppVariant; bkz. §3)
  docs/
```
- Oyun klasörü **Shared'a bağımlı olabilir**, tersi yasak. Oyunlar birbirine bağımlı olamaz; Yüzbir/Okey ortak board
  kodu `Shared/Board/` ya da `Games/OkeyCommon/` olarak ayrı yaşar (🔶 Faz 3'te karar).
- **Sahne oyuna aittir, kod ortaktır.** Login/Lobby/dialog ekranları her oyunda ayrı sahne/prefab kompozisyonu; Shared
  controller'ları takılır. İkinci oyun ilk oyunun sahnesini kopyalayıp farklılaştırarak başlar; ortaklık nested prefab ile.
- Tek `DigitoyClient.Game.csproj` (editör üretir); tüm oyunlar tek assembly, **tüm oyunlar tek pak**.
- **Asset bütçesi** (decisions 2026-10-16): görseller mümkün olduğunca SVG; genel ikon/font/parça/efekt/SFX yalnız
  Shared'da tek kopya; oyun klasörüne yalnız oyuna özgü görsel (kart/taş atlası, masa arka planı). Localization kabuk
  (`Shared`) + oyun (`Games/<Oyun>`) tabloları (editör desteği 02 §C9; gelene kadar tek tablo + önek).

## 3. Varyant modeli
Üç ayrı veri (Unity `GameProperties`'in bölünmüş hâli — decisions 2026-10-16):
| | Sayı | Zaman | İçerik |
|---|---|---|---|
| **`AppVariant`** `ProjectSettings/Variants/<ad>.asset` | mağaza uygulaması başına | derleme | `productName`, `bundleId/packageName`, `icon`, `splash`, `orientation`, `versionCode`, **`startGame`** (GameDefinition ref), store app ID'leri, reklam unit ID'leri, AppsFlyer/Adjust token, FB izinleri |
| **`GameDefinition`** `Games/<Oyun>/Game.asset` | oyun başına, hepsi build'de | runtime seçilir | `gameId`, `displayName`, `maxSeats`, `loginScene/lobbyScene/tableScene`, `dialogs{profile, settings, store…}` prefab ref'leri, loc tabloları, lobi kart prefab'ı, oyuna özel ses ref'leri |
| **`NetworkSettings`** `Shared/Settings.asset` | tek | sabit | test/prod URL, `useTestServer`, CDN kökü (zaman aşımları `Server.ConnectionTimeoutSeconds/RequestTimeoutSeconds` sabitleri) |
| Dev override | tek | yerel | `customServerUrl`, `OverrideDeviceKey`, `cloneEnabled` — asset'e yazılmaz (UserSettings/PlayerPrefs) |

- Build: aktif `AppVariant` seçilir (PlayerSettings ya da build parametresi); yalnız kimlik alanları override edilir,
  **sahne listesi filtrelenmez** (02-engine-gaps §A1). Editör Play de aktif varyantla başlar.
- Runtime: `Variant.Current` (AppVariant) ve `Game.Current` (GameDefinition; başlangıçta `Variant.Current.startGame`).
  `Game.Switch(def)`: `Server.Logout` → `ServiceRegistry.ResetAll()` → `Server.Initialize(def.gameId)` → Guest login
  (`Token=null`, aynı deviceKey) → `LoadSessionAsync` → `def.lobbyScene`. Token `token.<gameId>` anahtarıyla saklanır.
- Kabuk kodu `Game.Current.id`'ye değil **referanslara** bakar (`Game.Current.dialogs.profile`) — `if (game == "gin")` yasak.

## 4. Tema
Motorda tema katmanı var (engine-notes 2026-10-15: sahne/prefab dosyası içinde isimli override katmanı; build-time).
- **Sezon teması (xmas, kurban…) build'de seçilir** (`player_build theme=…`), build'deki tüm oyunlar aynı temayla pişer;
  ikon/splash da değişir → sezon = ayrı mağaza sürümü. Runtime tema seçimi yok.
- Ortak parçanın (avatar çerçevesi, buton) katmanı **en iç Shared prefab'ında** → tüm oyun sahneleri otomatik alır; oyun
  sahnesi kendi katmanını ekleyebilir.
- Oyunlar arası görünüm farkı tema ile **değil**, oyun başına sahne/prefab ile çözülür (§2). Kural: kabuk prefab'ları
  renk/kenar/font'u **token** ile alır, sabit değer yazmaz.
## 5. Katmanlar
```
Common (M.*)        kurallar + wire modelleri (dışarıdan gelir, dokunulmaz; ihtiyaç → digitoyserver PR)
Shared/Services     saf C#; motor yüzeyi yalnız WebSocket/Http/PlayerPrefs/NativeAsync/Frame; event'ler C# event
Shared/Ui+Dialogs   Component'ler; servis event'lerini dinler, sahneyi projekte eder
Games/<Oyun>        TableState → sahne projeksiyonu (tek state machine; Phase×SubPhase sunucudan), MovieClip koreografi
```
- **Ağ:** core.upm `Server`/`ServerDirectory` **adapte edilir** (decisions 2026-10-16/18): bağlantı durum makinesi, relogin,
  heartbeat 25 sn, reconnect/gateway failover aynen. İki katman, arayüz yok: `NetworkClient` (tek url'ye bağlan, auth-on-open,
  requestId→TCS, istek zaman aşımı, heartbeat, kapanış — motor `WebSocket` `Send(ReadOnlySpan)` ile kopyasız) ve **`World`**
  (`World.Network`: gateway hop + bütçe + settings tazeleme, reconnect döngüsü, istek; `World.Session`: login + oturum yükleme —
  eski `Server` sınıfı 2026-10-23'te World'e eridi, decisions). Auth bayrağı yalnız `NetworkClient.IsAuthenticated`.
  WS şeması: wasm `wss`, diğer hedefler `ws` (`NetworkSettings.ToWebSocketUrl`, tek kopya). `Codec` (Common) → `Endpoint`
  çağrıları `Task<T>` (motor tek-thread; `NativeAsync.Pump` frame başında — ContinueWith/Task.Run yok; `Awaitable`→`Task`,
  `MainThreadAsync` gereksiz). Push event'leri (`requestId == -1`) tek kapı `World.Push` (SessionReady'ye kadar düşer) → `World.Apply`.
- **Oyun oturumu:** `Game.Current` tek aktif; `Game.Switch` = logout → `ServiceRegistry.ResetAll()` → yeni gameId ile
  Guest login → lobby. Servisler statik singleton kalır (core.upm deseni); iki oyun aynı anda yok (decisions 2026-10-16).
- **Oyun durumu (decisions 2026-10-20, ayrıntı legacy-analysis/05 §4):** eski digiplay birebir, araya katman yok.
  `TableService` (= eski `TableModel`): delta merge cache'i (null = değişmedi, `Hand=[]` = boş, boş cevap = Clear) + `Version` +
  `SnapshotCount`; push ve cevap tek `Apply()` yolundan; türev kancaları `OnSnapshot/OnDelta/OnCleared`; view'a **tek `View` alanı**
  (`ITableView.Refresh(full)/SeatChanged/Cleared`), doğrudan çağrı, delegate yok. `GinTableService : TableService` (= eski
  `GinTableModel`): **kendi elin tek sahibi** — snapshot'ta replace, deltada `Action`'a göre yama + `IGinTableView` hamle çağrısı
  (anlık, paralel, kuyruk yok), kendi hamle optimistic + `Pending` kilidi + fire-and-forget, echo idempotent, invariant ihlali →
  `Refresh`. View: `Refresh(true)` idempotent uzlaştırma (el id-multiset, deste sayı, ıskarta son-N), `Refresh(false)` hafif;
  diziliş view'da; snapshot'ta `Clear()` yok. Servis kaydı `TableServices` (gameId → factory, `[ModuleInitializer]`).
- **Bot/offline (Gin):** kapsam kararı bekliyor (🔶 ilk sürümde yok önerisi). Olursa aynı `GinTable` modelini
  `ILocalDealer` besler; strateji Common `HandScore/HandSorter` üstüne ince katman.
- **Dialog yığını:** `DialogStack` servisi (modal arka plan, geri tuşu `GameHost.BackRequested`, açılış/kapanış MovieClip,
  sonuç `Task<T>`). Unity `DialogBase` 26+ dialog'un karşılığı.
- **Lobi listesi / leaderboard / store:** motor `ScrollView` + prefab havuzu; sanal liste gerekirse Faz 1'de ölçülür.
- **Metin girişi:** motor `TextField` (yok — 02 §B) nick, masa kodu, chat, arkadaş arama için.

## 6. Unity → motor karşılık tablosu (çalışma sözlüğü)
| Unity / core.upm | DigitoyClient |
|---|---|
| Canvas + RectTransform + Image + TMP | `LayoutBox` (anchor/flex/9-slice/metin), `TextSprite` |
| CanvasGroup alpha | `RenderGroup` |
| Image.fillAmount | `ProgressRenderer` |
| Animator/.anim, DOTween Sequence (sahne intro, deal, meld, round-over) | `MovieClip` (doc'ta) |
| DOTween tepkisel (hover, drag geri dönüş) | `Tween` extension'ları |
| UIParticle / UIParticleAttractor | `ParticleSystem` + kod (attractor yok, 02 §C) |
| Mask/RectMask2D, ScrollRect | `OverflowMode.Hidden`, `ScrollView` |
| DialogBase | `DialogStack` + Dialog prefab |
| UILocale / LocalizationManager | `LocalizationTable` + `@loc` + `Format(args)` |
| GameProperties (ScriptableObject) | `AppVariant` + `GameDefinition` + `NetworkSettings` (§3) |
| PlayerPrefsService / LocalCache | `PlayerPrefs` |
| BestWebSocketClient / UnityWebRequest | `WebSocket` / `Http` (Net.cs) |
| Addressables uzak içerik (album, avatar paketleri) | `Module.LoadAsync(pak)` + `Http` indirme |
| SRDebugger / DevConsole | editör RPC (`render.describe`, `scene.dump`) + oyun içi küçük debug overlay |
| Unity IAP / MAX / Facebook / Apple / Firebase / Adjust | native köprüler (02 §D) |
