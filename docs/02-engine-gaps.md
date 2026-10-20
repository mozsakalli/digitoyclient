# Motor eksikleri — DigitoyClient'ın game_rd'den beklediği işler

Durum 2026-10-17. Kaynak: [legacy-analysis/00-port-assessment.md](legacy-analysis/00-port-assessment.md) §4 (2026-10-14)
+ engine-notes'ta o tarihten sonra kapanan maddeler. Her madde game_rd'de **ayrı plan/onay** ister; burası yalnız
sipariş listesi ve öncelik. Motor kaynağı bu projede yok; motor işi game_rd oturumunda yapılır.

Kapanmış (engine-notes): ScrollView (10-15), BoxStyle/Tema/isimli durumlar (10-15), nested prefab (10-15), Regex alt
kümesi (10-14; `NickValidator`, `LocalizationFormatter` için yeterli), Common.dll AOT boot (10-13/14), Http/WebSocket
(Net.cs), `render.describe`/küçük screenshot RPC, SVG asset Faz 1 (10-16), **B2 JSON** (`Json.Parse/FromJson/ToJson`,
`JsonNode`, 10-17), **C4 rich text** (`<b> <i> <u> <color> <size> <font=ad> <link=id>`, `LayoutBox.LinkAt(e)`, 10-17),
**C10 textSize = em** (10-17; `LayoutBox.font` artık tablo adı — `fonts_list`).

## A — Varyant / build (Faz 0'da gerekli)
| # | İş | Not |
|---|---|---|
| A1 | **Build varyantı** (`AppVariant`): `ProjectSettings/Variants/<ad>.asset` tanıma; editörde aktif varyant seçimi; pak/Android/iOS build'inde `productName/bundleId/icon/splash/orientation/versionCode` override + `startGame` referansı manifest'e; Play aktif varyantla başlar. **Sahne listesi filtrelenmez** — tüm oyunlar pak'a girer (decisions 2026-10-16). | Bugün `PlayerSettings.asset` tek `bundleIdentifier`. Tasarım 01-architecture §3. Tema ile birlikte: `theme=` + `variant=` build parametreleri. |
| A2 | `Variant.Current` runtime erişimi (pak manifestinde varyant id + `startGame` asset ref) | ALC reload güvenli (statik tip cache yok). |
| A3 | Common.dll **generator'lı sürümünün** AOT doğrulaması: `Serializers.For<T>()` tablosu + `CodecName.GetCustomAttribute` yolu lean/full'de canlı mı; `ICodecModel` tiplerinin trim'de korunması | Legacy B1 → artık "doğrula" işi. Kabul: gerçek sunucuya login + `TableState` decode. |

## B — Blokerler (Faz 1 kabuk için)
| # | İş | Not |
|---|---|---|
| B1 | **TextField** component (LayoutBox üstü; caret/seçim/IME; `GameHost.EvKeyDown/EvText` tüketimi) + native `ShowKeyboard/HideKeyboard` (Android/iOS) + klavye yüksekliği event'i | Nick, masa kodu, chat, arkadaş arama, destek formu. Editör tarafında `GuiTextField` çekirdeği var; oyun-tarafı yok. |
| B2 | ~~JSON okuyucu/yazıcı~~ **KAPANDI 10-17** (`Json`, `JsonNode`, `JsonWriter`, `[JsonField]`) | İstemci settings akışı yazıldı (10-17): `Config.LoadGameSettingsAsync/RefreshDirectoryAsync/LoadRemoteSettingsAsync`, `LocalCache.ApplySettingsResponse`, `GameSettings`; LoginFlow force update / maintenance / policy. |
| B3 | **Safe-area inset API** (`Screen.safeArea` karşılığı; Android cutout, iOS notch/home) | Unity `SafeArea.cs` 10 K. Kabuk yerleşimi için şart. |
| B4 | Uygulama durumu: pause/resume ✔ var; **ağ erişilebilirlik probu** (`InternetProbe` karşılığı), `OpenURL`, panoya kopyala, paylaşım (Native Share), haptic | Küçük native shim fonksiyonları; tek pakette planlanabilir. |

## C — Orta (iş gücü; motor özelliği ya da oyun kodu)
| # | İş | Not |
|---|---|---|
| C1 | Parametrik yol tween'i (`DOPath/DOLocalJump/DOShake/DOPunch` karşılığı) — `Curve` ile ya da MovieClip'e inme | Kart/taş uçuşları çoğu MovieClip'e iner; kalan tepkisel hareket için `Tween` + `Curve`. |
| C2 | `UIParticleAttractor` (coin'lerin hedefe akması) | Oyun kodu (`ParticleSystem` + hedef çekimi) — motor işi şart değil. |
| C3 | Dönmüş/ölçekli iç içe kırpma (`OverflowMode.Hidden` stencil davranışı) doğrulaması | ScrollView içinde döndürülmüş kart görünümü. |
| C4 | ~~Rich text~~ **KAPANDI 10-17** — TMPro alt kümesi + `<link=id>` + `LayoutBox.LinkAt(e)`; Login Agreement tek metne indi. | Kullanıcı metni `RichText.Escape(s)`. |
| C5 | Uzak görsel yükleme hattı: `Http` → `Texture` runtime decode (`AsyncJobs.ReadAsync`) + önbellek | Avatarlar, album. |
| C6 | Unity sahne/prefab → `.scene/.prefab` **yarı otomatik çevirici** (RectTransform+Image+TMP → LayoutBox) | 298+88 prefab elle kurulmaz; editör aracı 2-3 gün, haftalar kazandırır. 🔶 Faz 1 başında karar. |
| C7 | Unity locale `.asset` → `LocalizationTable` içe aktarıcı | Küçük script; 27 dosya × 2 dil (Gin). |
| C8 | Ses: wav → sıkıştırma kararı, stream müzik ✔ var | Gin 34 wav / 47 MB. |
| C9 | **Klasör kapsamlı localization tablosu**: `Assets/Games/<Oyun>/Localization.asset` — o klasördeki sahne/prefab metinleri otomatik oraya bağlansın (`@loc` kapsam çözümü en yakın üst klasör tablosu), `loc_*` araçları çoklu tabloyu listelesin | Runtime `Localize.LoadedTables/RegisterTable` zaten çoklu; eksik editör/bağlama tarafı. Gelene kadar tek tablo + `gin.`/`shell.` önek. Faz 1. |
| C10 | ~~`textSize` = em~~ **KAPANDI 10-17** — textSize artık TMP `fontSize` ile 1:1; `LayoutBox.font` guid yerine `Fonts.asset` adı (regular/light/bold/primary). | Eski sahnelerdeki guid `font` değerleri adlara çevrilecek; ×1.14 geçici kuralı iptal. |
| C11 | **Play'de `SceneNav`/`SceneLoader.LoadAsync` ile yüklenen (açık doc OLMAYAN) sahne, editör süreci boyunca ilk yüklenen hâliyle cache'leniyor** — `scene.save`, dosyaya dokunma, `play.stop/start`, `game.set` hiçbiri tazelemiyor; `.scene` için importer olmadığından `asset.reimport` da yok. Tek çare editörü kapatıp açmak. (10-20, Table.scene yeniden kurulurken) | Kaydedilen `.scene` için `SceneLoader` cache'ini geçersiz kıl (ya da `asset.reimport` `.scene`'i kabul etsin). |

## D — Native köprüler (Faz 5; gameplay'i bloklamaz, mağaza sürümünü bloklar)
IAP (Google Billing / StoreKit), reklam (MAX: interstitial + rewarded), Facebook login/paylaşım, Apple Sign-In, Google
Play Integrity + Widevine/Android ID (`DeviceKey`), push izni/token (FCM/APNs), Adjust, Helpshift (🔶 kapsam dışı
önerisi), Firebase (yalnız messaging kullanılıyor). Her biri: native shim + Kotlin/ObjC tarafı + `NativeAsync` tamamlanma
+ managed ince API. Önce ortak **"native plugin" iskeleti** (JNI/ObjC köprü deseni `Music.kt`/`Prefs` gibi) sonra tek tek.

## Öncelik sırası (DigitoyClient roadmap'ine göre)
Faz 0: A1, A2, A3 · Faz 1 başı: B2 (JSON, kesin), B1, B3, B4, C6, C7, C9 · Faz 2: C1, C3, C5 · Faz 3+: C2, C4, C8 · Faz 5: D.

Faz 0 keşfinde (2026-10-16, `engine_api`) doğrulanan motor yüzeyi — eksik yok: `WebSocket.ConnectAsync(url, ct, headers)`,
`Send(byte[])`, `ReceiveAsync`, `OnMessage/OnClose`, `CloseCode/CloseReason`; `Http.GetAsync/PostAsync`; `PlayerPrefs`
(Unity ile aynı imzalar); `NativeAsync.NextFrame/WaitSeconds/Pump`, `Wait.Seconds/Timeout`, `Frame.Next`; `GameHost.Pause/
Resume/BackRequested`; `Time.time/deltaTime/frameCount`. Not: `Screen` yalnız width/height/pixelRatio (B3 safe-area yok).
