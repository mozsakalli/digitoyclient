# Karar günlüğü — DigitoyClient

Kronolojik, en yeni üstte. Her madde: tarih, karar, gerekçe, etkilenen doküman.

## 2026-10-18 — Facebook Login = motor plugin'i (`Assets/Plugins/Facebook`), ilk gerçek plugin
- **Model:** game_rd `docs/plugins.md`. `Runtime/Facebook.cs` (`Plugin<Facebook.Config>`; JSON/id protokolü; `Login()` →
  motor-bağımsız `LoginResult{Status,Token,UserId,Error}`), `Editor/FacebookHooks.cs` (Android resString'ler, iOS plist/SPM,
  WASM script) + `FacebookMock.cs` (editör Play: status/login/logout taklidi), `Android/` Gradle **library** modülü
  (`facebook-login:17.0.0`, manifest meta-data + `FacebookActivity`/`CustomTabActivity`, Kotlin `FacebookPlugin`), `iOS/`, `WASM/`.
- **core.upm'den korunan davranış:** `_loginInProgress` kilidi (ikinci çağrı `Busy`), süresi dolmamış cached token varsa SDK
  ekranı açılmaz (`status` isteği; `AccessToken.isExpired`), `Logout`. **Alınmayan:** iOS ATT/Limited login + `timeScale`
  dondurma (iOS fazında), `FB.Init` (Android SDK manifest meta-data ile kendi başlar).
- **Bağlama:** `Server.LoginAsync(LoginType.Facebook)` → `Plugin.Get<Facebook>().Login()`; `Cancel→FacebookAuthCancelled`,
  diğer→`FacebookAuthFailed`; token varken `req.Token=null` (JWT fast-path'in sosyal girişi guest'e döndürmesine karşı, core.upm
  notu). `LoginFlow`: FB butonu bağlı; `LastAuthType==Facebook` oto-login'e girer (plugin cached token kullanır). **Yok:**
  `keepPlayerId`/AccountConflict çözümü (ayrı karar).
- **Ayar:** `ProjectSettings/Plugins/Facebook.asset` — şimdilik PLACEHOLDER appId/clientToken (`[Required]` build'i düşürmesin);
  gerçek kimlik oyun başına **varyant overlay**'inde (`ProjectSettings/Variants/<oyun>/Plugins/Facebook.asset`) olacak;
  varyant henüz tanımlı değil. `clientToken` `[Sensitive]` (AI okuyamaz).
- Etkilenen: 01-architecture (Plugins), 02-engine-gaps (Faz 5 FB kalemi kapandı).

## 2026-10-18 — Ağ katmanı sadeleştirme: INetworkClient kaldırıldı, hop döngüsü Server'da, zero-GC gönderim
- **`INetworkClient` silindi**; tek implementasyon `NetworkClient` (eski `EngineWebSocketClient`). Motorun tek ağ katmanı var,
  soyutlama Unity'deki Best HTTP/mock değişimi içindi. Authenticator imzası `Func<CancellationToken, Task<ErrorCode?>>`.
- **Katman:** gateway hop + toplam bütçe + `Config.RefreshDirectoryAsync` döngüsü `Server.ConnectTransportAsync`'e taşındı;
  `NetworkClient.ConnectAsync(url, timeout, ct, relogin)` tek adrese bağlanır → `(error, opened)`. Taşıma katmanı artık
  Config/Game'e bağımlı değil (eski transport → Config → Server döngüsü kırıldı).
- **Auth bayrağı tek yerde:** `NetworkClient.IsAuthenticated`; `Server._isAuthenticated` kaldırıldı.
- **Reconnect hızlı:** kopuşta ön internet probu yok, ilk deneme ANINDA (backoff 0/1/2 sn); probe yalnız deneme başarısız
  olunca. Denemeler arası `State` `Reconnecting`'de kalır (Dead/Reconnecting titremesi yok).
- **Zero-GC:** gönderim `Codec` tamponundan `WebSocket.Send(ReadOnlySpan)` ile kopyasız; istek zaman aşımı per-request CTS/Register
  yerine saniyelik tek tick'te süpürülür (dış iptal yalnız `CanBeCanceled` token için kayıt); heartbeat aynı tick'te
  (`Frame.Next` döngüsü yok); paket başına RX log kaldırıldı. `LatencyMs` heartbeat Ping gidiş-dönüşünden.
- **WS şeması:** wasm hedefi `wss`, diğer tüm hedefler `ws` (`NetworkSettings.ToWebSocketUrl`; `ServerDirectory` kendi kopyasını
  kullanmıyor).
- **Statik sıfırlama:** `ServiceRegistry.ResetSessionStatics` → `TimeSync.Reset` + `Config.Reset` (tek sahip); `PlayerService`
  artık TimeSync'i kendisi sıfırlamaz. `TableService.Dispose` (ölü) silindi. `Server.cs` blok namespace.
- Etkilenen: 01-architecture §Ağ.

## 2026-10-17 — Motor: textSize em, rich text, JSON geldi; metin kuralları
- **textSize = TMP fontSize 1:1** (×1.14 geçici kuralı iptal). `LayoutBox.font` **ad** (`fonts_list`: regular/light/bold/primary);
  guid yazılmaz, eski guid'ler adlara çevrilecek.
- **Rich text kuralı:** karışık biçimli cümle (link, kalın parça) **tek LayoutBox + tek loc anahtarı**; parçalara bölme yok.
  Gerçek kalın yüz `<font=bold>` (SDF `<b>` yalnız vurgu). Link = `<link=id><u>…</u></link>`, tıklama aynı GO'daki
  component'te `box.LinkAt(e)`. Kullanıcı/sunucu metni `RichText.Escape`. İlk uygulama: Login Agreement + `PolicyLink`.
- **İkonlar:** ayrı boş TTF `Shared/Fonts/Digitoy-Icon.ttf` (ad `icon`), SVG customGlyph'ler **ASCII harf** (a–z, A–J) üzerinde;
  metinde `<font=icon>a</font>` (`Icon.Tag`). PUA/`\uE0xx` yok, metin fontlarında customGlyphs yok. Font tablosu play başında
  yüklenir: `fonts_register` sonrası `play_stop`.
- **JSON** motor `Json.FromJson<T>/ToJson/JsonNode` ile. Settings akışı yazıldı: `Config.LoadGameSettingsAsync` (Login başında,
  2 tur × 15 s, seed → gömülü host) + `RefreshDirectoryAsync` (gateway tükenince). **Settings cache'i PlayerPrefs'te ham JSON
  string** (`GameSettings` ICodecModel değil; Codec yolu yalnız Common tipleri). `"settings":null` = sürüm eşit → cache.
  Force update / maintenance: errorText + form kapalı (OpenURL/Quit B4 gelince); policy değişimi log (Dialog Faz 1 kabuk).

## 2026-10-17 — UI referans çözünürlüğü 1920×1080 (önceki "1920 800" kararı düzeltildi)
- **Karar:** her sahnede `FitPerspectiveProjection.referenceResolution = 1920 1080`. Motor davranışı **contain / min-ölçek**
  (`scale = min(W/1920, H/1080)`) = Unity CanvasScaler `ScaleWithScreenSize + Expand` (Unity sahnelerinin tamamı böyle).
  Böylece Unity RectTransform/px değerleri 1:1 taşınır; "width-match" varsayımı yanlıştı (geniş ekranda yükseklik 1080'e
  kilitlenir, genişlik aspect'e göre 1920+ açılır).
- Elemanlar Unity'den küçük görünüyorsa sebep kamera değil boyutlardır; Unity'de prefab **override**'larına bak
  (Login butonları prefab 0×0, override 520×112).
- Etkilenen: Login.scene (uygulandı), Lobby.scene (1920 800 → 1920 1080 yapılacak), 01-architecture UI bölümü.
- **Metin boyutu:** motor `textSize` em değil **satır kutusu** (ascent−descent) → TMP `fontSize 48` ≈ bizde 60. Motor eksiği
  olarak kaydedildi (02-engine-gaps C10). Düzelene kadar Unity fontSize × {Primary 1.20, Bold/Light 1.14, Regular 1.12}.

## 2026-10-16 — Entry sahnesi yok; game_rd playground'dan asset alınmaz; UI referans çözünürlüğü
- **Entry yok:** `PlayerSettings.startScene` doğrudan oyunun Login sahnesi; ortak kurulum `AppBoot.Ensure(game, network)` LoginFlow
  Start'ında. AppVariant (motor A1/A2) gelince startScene varyanttan okunur, yine Entry gerekmez.
- **game_rd `Projects/*` motor playground'udur**, buradan kod/asset alınmaz (00-inventory §5 / roadmap Faz 0 adım 8 iptal).
  Fontlar `Assets/Shared/Fonts/` (kullanıcı koydu).
- **UI referans çözünürlüğü:** her sahnede `CameraComponent + FitPerspectiveProjection(fov 30, referenceResolution 1920 800)` →
  Unity Canvas 1920×1080 width-match ile aynı ölçek; Unity px değerleri 1:1 taşınır. Tek `fitScreen` kök kutu (Background),
  anchor'lar onun altında; alt gruplar alttan, üst gruplar üstten anchor'lı.
- **Log:** `Log.cs` kaldırıldı; motor `Debug.Log/LogWarning/LogError` (editör Console + log_tail).
- **Sahne geçişi:** `SceneNav.GoToAsync(key)` = `SceneLoader.LoadAsync(key, assets, catalog, null, replace:true)` (motor düzeltmesi
  2026-10-16 gece). Hedef sahne diskte olmalı.

## 2026-10-16 — Faz 0 adaptasyonunda FB/Apple login silinir
- **Karar:** `Auth.cs`/`Server.LoginAsync` kopyalanırken Facebook/Apple/InstantGame `#if` dalları **silinir**, yalnız Guest
  yolu kalır. Faz 5'te core.upm'den yeniden alınır (native köprü ile birlikte). `AccountConflict` çözümü (keep) de o zaman.

## 2026-10-16 — Asset optimizasyonu birinci öncelik; localization bölünür
- **Karar (asset):** Tek build'de çok oyun olduğundan asset bütçesi kritik. (1) Görseller **mümkün olduğunca SVG**
  (motor SVG asset Faz 1 var); bitmap yalnız fotoğraf/karmaşık illüstrasyon. (2) Genel ikonlar, fontlar, buton/çerçeve
  parçaları, efektler `Assets/Shared/`'da **tek kopya**; oyun klasörüne yalnız oyuna özgü görsel (kart/taş atlası, masa
  arka planı) girer. (3) Oyun klasörüne eklenen her bitmap için "Shared'da eşdeğeri var mı?" kontrolü; atlas paylaşımı
  Shared'da. (4) Ses: ortak SFX Shared'da; müzik stream.
- **Karar (localization):** Kabuk metinleri `Assets/Shared/Localization.asset`, oyuna özel metinler
  `Assets/Games/<Oyun>/Localization.asset` — tablo başına birkaç bin anahtar × dil, boyut sorun değil (yüzlerce KB);
  bölünmenin amacı düzen/çakışma, pak boyutu değil. Runtime çoklu tablo var (`Localize.LoadedTables/RegisterTable`);
  **editör tarafı** bugün yalnız tek kapsam tablosu üretiyor → 02-engine-gaps **C9** (klasör kapsamlı tablo). C9 gelene
  kadar tek tablo, anahtarlar `gin.`/`shell.` önekiyle.
- **Etkilenen:** 01-architecture §2, 02-engine-gaps C9.

## 2026-10-16 — Sezon teması = build-time, ikon dahil
- **Karar:** Mevsimsel tema (xmas, kurban…) motor tema katmanıyla (sahne/prefab dosyası içinde `themes:{ad}`), build'de
  seçilir (`player_build theme=…`); build'deki tüm oyunlar aynı temayla pişer, ikon/splash da varyant+tema ile değişir.
  Runtime tema seçimi **yok**; sezon = ayrı mağaza sürümü.
- Ortak parçanın teması en iç Shared prefab'ında taşınır → tüm oyun sahneleri otomatik alır; oyun sahnesi kendi katmanını
  da ekleyebilir.
- **Etkilenen:** 01-architecture §4 ("Config'ten runtime seçim" kaldırıldı).

## 2026-10-16 — Ekranlar: kod ortak, sahne oyuna ait
- **Karar:** Masa dışı ekranlar (Login, Lobby, Profil, Store, Ayarlar…) **kod/servis/parça prefab** olarak `Assets/Shared/`'da;
  **sahneler ve dialog kompozisyonları** `Assets/Games/<Oyun>/Scenes/`'de oyun başına. Oyunlar ekranı istediği kadar
  farklılaştırır (yerleşim, ek panel, MovieClip) — stil token'ı ile sınırlı değil. Shared'da yalnız `Entry.scene` bulunur.
- `GameDefinition` sahne/prefab referanslarını taşır (`loginScene, lobbyScene, tableScene, dialogs{…}`); kabuk kodu oyun adı
  değil referans okur.
- **Gerekçe:** Tek build'de tüm oyunlar olduğundan tema katmanı oyun ayrımı için kullanılamaz (build-time); nested prefab
  ortaklığı yeterli. İkinci oyun ilk oyunun sahnesini kopyalayıp bozarak başlar.
- **Etkilenen:** 01-architecture §2, §4, §5.

## 2026-10-16 — Tek build, tüm oyunlar; oyun oturumu tam izole, aynı anda tek oyun
- **Karar:** Pak/build **tüm oyunların** sahnelerini içerir. `AppVariant` (derleme zamanı) yalnız kimliği seçer:
  `bundleId/packageName, productName, icon/splash, startGame, store/reklam/attribution anahtarları`. `GameDefinition`
  (oyun başına, hepsi build'de): `gameId, maxSeats, sahne/prefab ref'leri, tema/loc ref'leri`. `NetworkSettings` (Shared,
  tek): test/prod URL, timeout, CDN kökü. Dev override'lar (customServerUrl, OverrideDeviceKey, clone) asset'e yazılmaz.
- **Oturum:** Sunucuda aynı deviceKey + farklı gameId = **farklı player**; PlayerState/Inventory/Config oyun başına izole.
  Oyun değişimi (`Game.Switch`): `Server.Logout` → `ServiceRegistry.ResetAll()` → `Server.Initialize(gameId)` → Guest login
  (`Token=null`, aynı deviceKey/Params, yeni `GameId`) → `LoadSessionAsync` → `Game.Current.lobbyScene`. Token `gameId`
  anahtarıyla saklanır (`token.<gameId>`), geri dönüşte hızlı yol. **Aynı anda iki oyun oturumu yok** (sunucu tarafında da
  büyük iş; gerekirse ileride konuşulur) → servisler statik singleton kalabilir.
- Unity `GameProperties` → `AppVariant` + `GameDefinition` + `NetworkSettings` olarak bölünür; `gameManagerPrefab`,
  `gameManagerAddressableKey` atılır. `Server.Properties.gameId` → `Game.Current.gameId`.
- **Etkilenen:** 01-architecture §1-3, §5; 02-engine-gaps A1 (build varyantı sahne filtrelemez); 03-roadmap Faz 0 adım 5.

## 2026-10-16 — core.upm adapte edilir, sıfırdan yazılmaz
- **Karar:** `core.upm/Runtime/{Network,Components,Helpers,DeviceKey,Extensions}` kodu bu projeye **kopyalanıp mekanik
  olarak Unity'den arındırılır**; davranış değiştirilmez. Yalnız WS taşıma katmanı (`BestWebSocketClient` içindeki Best HTTP
  soket kullanımı) motor `WebSocket` üstüne yeniden yazılır; gateway hop/bütçe, requestId eşleme, heartbeat, `Connection`
  nesli mantığı korunur.
- **Gerekçe:** Ölçüm (09:35): servis dosyalarında Unity referansı 0–17, hepsi 4 kalıp — `Awaitable`→`Task`, `Debug.Log`→motor
  log, `PlayerPrefs` (motor API'si birebir), `SystemInfo/Application`→küçük `Platform` shim. Yeniden yazım risk ve süre ekler,
  kazanç yok.
- **İlke:** kopyalanan kodda "tek değişiklik = bağımlılık"; iyileştirme ayrı iş. `namespace Digitoy` korunur.
- **Etkilenen:** 01-architecture §5 (Ağ), 00-inventory §2 (Network satırı), 03-roadmap Faz 0.

## 2026-10-16 — Common.dll hazır alınır
- **Karar:** `Assets/Plugins/Digitoy.Common.dll` her zaman kullanıcı tarafından (digitoyserver build) verilir. Bu projede
  Codec/model/serializer işi yapılmaz, Common kaynağı okunmaz; yüzey DLL'den çıkarılır (`docs/04-common-surface.md`).
- **Etkilenen:** 03-roadmap Faz 0 (Adım 2 → "teslim al"), 02-engine-gaps A3 (AOT doğrulaması DLL ile yapılır).

## 2026-10-16 — Config/ServerDirectory: JSON (sunucu sözleşmesi sabit)
- **Karar:** `GET /api/config/{gameId}/settings` JSON döner (`version, settings{}, gateways[], seeds[], serverTime`);
  test-node'a karşı doğrulandı. İstemci JSON okuyucu gerektirir → 02-engine-gaps **B2 kesinleşti**. Faz 0'da gateway listesi
  sabit seed ile geçilir, settings çağrısı JSON gelince eklenir.
