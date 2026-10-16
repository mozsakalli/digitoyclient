# Motor eksikleri — DigitoyClient'ın game_rd'den beklediği işler

Durum 2026-10-16. Kaynak: [legacy-analysis/00-port-assessment.md](legacy-analysis/00-port-assessment.md) §4 (2026-10-14)
+ engine-notes'ta o tarihten sonra kapanan maddeler. Her madde game_rd'de **ayrı plan/onay** ister; burası yalnız
sipariş listesi ve öncelik. Motor kaynağı bu projede yok; motor işi game_rd oturumunda yapılır.

Kapanmış (engine-notes): ScrollView (10-15), BoxStyle/Tema/isimli durumlar (10-15), nested prefab (10-15), Regex alt
kümesi (10-14; `NickValidator`, `LocalizationFormatter` için yeterli), Common.dll AOT boot (10-13/14), Http/WebSocket
(Net.cs), `render.describe`/küçük screenshot RPC, SVG asset Faz 1 (10-16).

## A — Varyant / build (Faz 0'da gerekli)
| # | İş | Not |
|---|---|---|
| A1 | **Build varyantı**: `[Asset] GameVariant` tanıma; editörde aktif varyant seçimi; pak/Android/iOS build'inde varyantın sahne listesi + bağımlılık kapanışı; `productName/bundleId/icon/orientation/versionCode` override; Play aktif varyantla başlar | Bugün `PlayerSettings.asset` tek `startScene/scenes/bundleIdentifier`. Tasarım 01-architecture §3. Alternatif (🔶): varyant = ayrı `ProjectSettings/Variants/<id>.asset` ve PlayerSettings yalnız ortak alanlar. |
| A2 | `Variant.Current` runtime erişimi (pak manifestinde varyant id + asset ref) | ALC reload güvenli (statik tip cache yok). |
| A3 | Common.dll **generator'lı sürümünün** AOT doğrulaması: `Serializers.For<T>()` tablosu + `CodecName.GetCustomAttribute` yolu lean/full'de canlı mı; `ICodecModel` tiplerinin trim'de korunması | Legacy B1 → artık "doğrula" işi. Kabul: gerçek sunucuya login + `TableState` decode. |

## B — Blokerler (Faz 1 kabuk için)
| # | İş | Not |
|---|---|---|
| B1 | **TextField** component (LayoutBox üstü; caret/seçim/IME; `GameHost.EvKeyDown/EvText` tüketimi) + native `ShowKeyboard/HideKeyboard` (Android/iOS) + klavye yüksekliği event'i | Nick, masa kodu, chat, arkadaş arama, destek formu. Editör tarafında `GuiTextField` çekirdeği var; oyun-tarafı yok. |
| B2 | **JSON okuyucu/yazıcı** (reflection'sız DOM, corelib ya da engine) | `Config` uzak ayarları, `ServerDirectory` seed listesi, IAP/offer katalogları JSON. 🔶 Alternatif: sunucu bu uçları Codec ile de versin → sunucu ekibiyle görüşülecek. |
| B3 | **Safe-area inset API** (`Screen.safeArea` karşılığı; Android cutout, iOS notch/home) | Unity `SafeArea.cs` 10 K. Kabuk yerleşimi için şart. |
| B4 | Uygulama durumu: pause/resume ✔ var; **ağ erişilebilirlik probu** (`InternetProbe` karşılığı), `OpenURL`, panoya kopyala, paylaşım (Native Share), haptic | Küçük native shim fonksiyonları; tek pakette planlanabilir. |

## C — Orta (iş gücü; motor özelliği ya da oyun kodu)
| # | İş | Not |
|---|---|---|
| C1 | Parametrik yol tween'i (`DOPath/DOLocalJump/DOShake/DOPunch` karşılığı) — `Curve` ile ya da MovieClip'e inme | Kart/taş uçuşları çoğu MovieClip'e iner; kalan tepkisel hareket için `Tween` + `Curve`. |
| C2 | `UIParticleAttractor` (coin'lerin hedefe akması) | Oyun kodu (`ParticleSystem` + hedef çekimi) — motor işi şart değil. |
| C3 | Dönmüş/ölçekli iç içe kırpma (`OverflowMode.Hidden` stencil davranışı) doğrulaması | ScrollView içinde döndürülmüş kart görünümü. |
| C4 | Rich text (inline bold/link/renk) + span tıklama | "Hizmet Koşulları" metni; engine-notes 10-10 açık eksik. |
| C5 | Uzak görsel yükleme hattı: `Http` → `Texture` runtime decode (`AsyncJobs.ReadAsync`) + önbellek | Avatarlar, album. |
| C6 | Unity sahne/prefab → `.scene/.prefab` **yarı otomatik çevirici** (RectTransform+Image+TMP → LayoutBox) | 298+88 prefab elle kurulmaz; editör aracı 2-3 gün, haftalar kazandırır. 🔶 Faz 1 başında karar. |
| C7 | Unity locale `.asset` → `LocalizationTable` içe aktarıcı | Küçük script; 27 dosya × 2 dil (Gin). |
| C8 | Ses: wav → sıkıştırma kararı, stream müzik ✔ var | Gin 34 wav / 47 MB. |

## D — Native köprüler (Faz 5; gameplay'i bloklamaz, mağaza sürümünü bloklar)
IAP (Google Billing / StoreKit), reklam (MAX: interstitial + rewarded), Facebook login/paylaşım, Apple Sign-In, Google
Play Integrity + Widevine/Android ID (`DeviceKey`), push izni/token (FCM/APNs), Adjust, Helpshift (🔶 kapsam dışı
önerisi), Firebase (yalnız messaging kullanılıyor). Her biri: native shim + Kotlin/ObjC tarafı + `NativeAsync` tamamlanma
+ managed ince API. Önce ortak **"native plugin" iskeleti** (JNI/ObjC köprü deseni `Music.kt`/`Prefs` gibi) sonra tek tek.

## Öncelik sırası (DigitoyClient roadmap'ine göre)
Faz 0: A1, A2, A3, B2 (ya da Codec alternatifi) · Faz 1: B1, B3, B4, C6, C7 · Faz 2: C1, C3, C5 · Faz 3+: C2, C4, C8 · Faz 5: D.
