# Hedef mimari — DigitoyClient

Durum: **taslak, 2026-10-16**. ✅ = kullanıcı kararı, 🔶 = öneri (onay bekliyor). Kararlaşınca bu dosya güncellenir;
kalıcı karar günlüğü `docs/decisions.md` (Faz 0'da açılır).

## 1. Temel ilkeler
1. ✅ **Tek kaynak ağacı, oyun başına uygulama.** Her oyun ayrı store/FB uygulaması olarak çıkar (sayı artacak); build
   **varyant** seçer. Sahne görünümü, tema, içerik oyuna göre tamamen değişebilir — paylaşım zorunluluk değil imkân.
2. **Sunucu otoriter, istemcide tek durum modeli.** Kurallar `Digitoy.Common` (`M.*`) — istemci kural kopyası
   yazmaz; ipucu/bot/UI uygunluğu da Common üstünden. Unity istemcisinin "3 kopya" hatası tekrarlanmaz.
3. **Motor değişmezleri aynen** (game_rd `docs/engine-notes.md`): doc = kaynak, runtime sahneye sızmaz; hot path
   sıfır alloc; koreografi MovieClip (doc'ta), tepkisel hareket Tween; edit modunda çalışması gereken görsel
   component `[ExecuteInEditMode]`; veri = `[Asset]` sınıf → `.asset`, referans GUID.
4. **Unity'den çeviri değil yeniden yazım.** Unity kodu "ne yapıyor"u öğrenmek için okunur; MonoBehaviour/DOTween/
   async-callback dokusu taşınmaz.

## 2. Klasör sözleşmesi
```
digitoyclient/
  Assets/
    Shared/                    tüm oyunların ortak kabuğu (oyun-bağımsız; oyun adı geçmez)
      Scenes/   Entry.scene (varyant yükler), Login.scene, Lobby.scene
      Services/ Server, Auth, Session, PlayerService, LobbyService, TableService, MessagingService, Friends, Economy,
                Config, TimeSync, DeviceKey, Prefs            ← core.upm/Components + Network mantığının portu, saf C#
      Ui/       DialogStack, Dialog taban prefab'ı, Button/Toggle/Counter/Avatar/Toast, ScrollList kalıpları, TextField kullanımı
      Dialogs/  Profile, Settings, Store, DailyBonus, Social/Friends, Chat, Gifts, Inbox, Leaderboard, Prompt, TableFilter …
      Theme/    BoxStyle/Theme asset'leri (varsayılan tema), Fonts/, Effects/ (*.fx), ortak atlas
      Localization/ ortak tablo (menü/dialog metinleri)
    Games/
      GinRummy/  Table.scene(+intent), Cards/ atlas, Components/ (GinTable, Hand, DeckPile, MeldZone, Seat…), MovieClips/,
                 Theme/ (oyun teması), Localization/ (oyuna özel metinler), Variant.asset
      Yuzbir/    Table.scene, Tiles/, Board (Okey ile paylaşılan taban), Components/, Theme/, Variant.asset
      Okey/      Table.scene, Yuzbir'in Board tabanını kullanır, Variant.asset
    Plugins/
      Digitoy.Common.dll       digitoyserver/Common build çıktısı (netstandard2.1, generator'lı); sürüm notu yanında
  ProjectSettings/  PlayerSettings.asset (ortak) + varyant başına ayarlar (bkz. §3)
  docs/
```
- Oyun klasörü **Shared'a bağımlı olabilir**, tersi yasak. Oyunlar birbirine bağımlı olamaz; Yüzbir/Okey ortak board
  kodu `Shared/Board/` ya da `Games/OkeyCommon/` olarak ayrı yaşar (🔶 Faz 3'te karar).
- Tek `DigitoyClient.Game.csproj` (editör üretir); tüm oyunlar tek assembly. Oyun ayrımı derleme değil **içerik + varyant**
  düzeyinde. (Okey'in olmadığı Gin build'inde Okey component'leri DLL'de durur; AOT trim kullanılmayan kodu atar — pak'a
  yalnız varyantın sahneleri ve onların referansladığı asset'ler girer.)

## 3. Varyant modeli (motor işi gerektirir — bkz. 02-engine-gaps §A)
🔶 Öneri:
- `Games/<Oyun>/Variant.asset` (`[Asset] GameVariant`): `id` ("ginrummy"), `displayName`, `bundleId` (android/ios ayrı),
  `startScene` (Shared Entry'den sonra gidilecek Lobby/Table), `tableScene`, `theme` (Theme asset ref), `gameProperties`
  (sunucu `gameId`, gateway listesi/`ServerDirectory` seed, store ürün kataloğu, feature bayrakları), `icon`, `splash`,
  `localizationTables[]`, `scenes[]` (pak'a girecek sahne listesi).
- Build: editörde "aktif varyant" seçimi (`PlayerSettings.activeVariant` ya da build komutuna parametre). Pak/Android/
  iOS build'i varyantın `scenes[]` + bağımlılık kapanışını alır; `productName/bundleId/icon/orientation` varyanttan
  override edilir. Editör Play de aktif varyantla başlar.
- Runtime: `Variant.Current` tek statik erişim (startup'ta pak manifestinden okunur; ALC reload'da tazelenir).
  Kabuk kodu oyuna özel iş yapacaksa `Variant.Current.id`'ye değil **varyanttaki referanslara** bakar (örn. hangi masa
  sahnesi, hangi lobi kart prefab'ı) — `if (game == "gin")` yasak.

## 4. Tema
Motorda tema katmanı var (engine-notes 2026-10-15: Theme + BoxStyle v3 isimli durumlar, prefab 34 PASS). Kullanım:
- `Shared/Theme/Default.theme` tüm kabuk için; `Games/<Oyun>/Theme/<Oyun>.theme` onu **override** eder (varyant seçer).
- Bayram/sezon skin'i = ek tema asset'i, runtime'da `Config`'ten gelen anahtarla seçilir (sunucu `Config` modeli zaten var).
- Kural: kabuk prefab'ları renk/kenar/font'u **token** ile alır, sabit değer yazmaz; oyuna özel görünüm farkı tema ile
  çözülemiyorsa (yerleşim bambaşka) o ekran `Games/<Oyun>/`'da **ayrı sahne/prefab** olur — koşullu dallanma değil.

## 5. Katmanlar
```
Common (M.*)        kurallar + wire modelleri (dışarıdan gelir, dokunulmaz; ihtiyaç → digitoyserver PR)
Shared/Services     saf C#; motor yüzeyi yalnız WebSocket/Http/PlayerPrefs/NativeAsync/Frame; event'ler C# event
Shared/Ui+Dialogs   Component'ler; servis event'lerini dinler, sahneyi projekte eder
Games/<Oyun>        TableState → sahne projeksiyonu (tek state machine; Phase×SubPhase sunucudan), MovieClip koreografi
```
- **Ağ:** `Server` (bağlantı durum makinesi, relogin, heartbeat 25 sn, reconnect/gateway failover) → `Codec` (Common) →
  `Endpoint` çağrıları `Task<T>` (motor tek-thread; `NativeAsync.Pump` frame başında — ContinueWith/Task.Run yok).
  Push event'leri (`requestId == -1`) servis bazlı yönlendirme (Player/Lobby/Table/Messaging) — core.upm `Server.cs` deseni.
- **Oyun durumu:** `GameTable` (oyun-bağımsız: koltuklar, tur, zamanlayıcı — sunucu `TableState`) + oyuna özel türev
  (`GinTable`: el, deste, ıskarta, meld; `OkeyTable`: ıstaka, gösterge, orta). View bunları okur, asla yazmaz.
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
| GameProperties (ScriptableObject) | `[Asset] GameVariant` + `[Asset]` ayar sınıfları |
| PlayerPrefsService / LocalCache | `PlayerPrefs` |
| BestWebSocketClient / UnityWebRequest | `WebSocket` / `Http` (Net.cs) |
| Addressables uzak içerik (album, avatar paketleri) | `Module.LoadAsync(pak)` + `Http` indirme |
| SRDebugger / DevConsole | editör RPC (`render.describe`, `scene.dump`) + oyun içi küçük debug overlay |
| Unity IAP / MAX / Facebook / Apple / Firebase / Adjust | native köprüler (02 §D) |
