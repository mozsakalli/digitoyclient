# Gin Rummy Extra → DigitoyEngine port — ön inceleme (2026-10-14)

Kaynaklar: `C:\work\digitoygames\ginrummyextra.unity` (Unity istemci), `C:\work\digitoygames\core.upm` (Digitoy SDK),
`C:\work\digitoygames\digitoyserver` (sunucu + `Common` paylaşımlı kütüphane). Ayrıntılı raporlar bu dizinde:
`01-unity-gameplay-analysis.md`, `02-core-upm-analysis.md`, `03-server-analysis.md`, `04-unity-ui-feature-inventory.md`,
`05-digiplay-sync-pattern.md` (2026-10-20: eski motor senkron deseni, Unity kart kaybı kök nedeni, yeni delta sözleşmesi).
Motor yetenekleri `engine_api` + `docs/engine-notes.md` üzerinden çıkarıldı (motor kaynağına girilmedi).

---

## 1. Büyük resim (tek paragraf)

Oyun **sunucu-otoriter** bir 2 kişilik Gin Rummy. Kurallar (meld/deadwood/knock/gin/undercut/layoff/rush skorlaması)
sunucuda `Digitoy.Common` (`namespace M`, netstandard2.1, saf POCO) içinde **zaten tek ve temiz** bir yerde yaşıyor:
`Common\Ginrummy\{HandScore, HandSorter, RoundScorer, CardHelper, GinFlags}`. Unity istemci ise bu kuralların
**üç ayrı, birbirinden habersiz kopyasını** taşıyor (online UI yolu, bot/offline yolu, hint yolu) ve bunlar view
kodunun içine gömülü. Kullanıcının "gameplay logic düzgün implemente edilmemiş" hissi **doğru** — ama sorun kuralların
yanlışlığı değil, istemcide **tek bir oyun durum modeli olmaması** ve mantığın MonoBehaviour/DOTween/async
callback'lerine dağılmış olması. Port, bu nedenle "çeviri" değil **"yeniden yazım + Common'ı paylaşarak sadeleştirme"**
olarak planlanmalı.

---

## 2. Gameplay mantığı — teşhis

### 2.1 Üç kopya, sıfır ortak çekirdek

| Yol | Nerede | Ne yapıyor |
|---|---|---|
| Online UI | `Table\Scripts\Actions\GameRules.cs`, `Hands\PlayerHand.cs` (559 sat.), `Utilities\CardUtils.cs` (`CalculateDeadwood:83`, `FindAllMelds:89`, `CanLayoffOnMelds:189`) | Buton uygunluğu, deadwood gösterimi, drag/drop izinleri |
| Bot/offline | `Managers\BotGameManager.cs` (**1 351 sat.**), `BotGame\BotGameMeldTracker.cs`, `Bot\*Strategy.cs` (4 adet) | Tam ikinci oyun motoru: deste, tur, skor, timeout, pause/resume, UI |
| Hint | `BotGame\TableHintController.cs` (584 sat.) | Meld/knock/gin/discard ipuçları için kuralları yeniden yorumlar |

Sunucu `Common\Ginrummy` ile bunların hiçbiri aynı kodu kullanmıyor; divergence riski yapısal.

### 2.2 İki state machine, elle senkron
`FlowController._currentPhase` (Dealing/Slam/Melding/GameOver/RoundOver/Exit/RushTimeout) **ve** `TableContext.currentUIState`
(11 değerli enum) paralel tutuluyor; geçişler DOTween callback + `.Forget()` async + sunucu event'i üçlüsünden tetikleniyor
(`FlowController.cs:25,355-368`, `BotGameManager.cs:1219-1261`, `PlayService.cs:87-90`). Faz değişince eski tween'in
callback'inin koşması, destroy sonrası continuation, aynı aksiyonun iki kez tetiklenmesi riski rapor 01 §4.5-4.6'da örnekli.

### 2.3 View ↔ kural karışımı
- `TableContext` (475 sat.): DI konteyneri + view-model + UI state sahibi aynı MonoBehaviour.
- `CardExtensions` (729 sat.): adı "kural" çağrıştırıyor, içeriği DOTween koreografisi; `GinGame.Context` global'ine bağımlı.
- `DeckPile.cs:160-204`, `CardDefault.cs:152-192`: input handler içinde `GinGame.Rules.Can*` çağrıları.
- `GinGame.Context/Manager/Rules` statik service locator; `global using static GameManager`.

### 2.4 Kural/protokol uyumsuzluk noktaları (port'ta netleştirilecek)
- **Slam**: istemcide ayrı faz + animasyon var; sunucu protokolünde Slam diye bir aksiyon **yok** (normal draw/discard/finish).
  Slam istemci-tarafı sunum (finisher'ın son kartı hızlı atması) — doc'ta MovieClip'e iner.
- **Big Gin**: `RoundScorer.cs:43-50` sabiti var ama "10 kartlı elde kullanılmaz"; istemcide merkezi `IsBigGin` yok. Port'ta UI
  Big Gin göstermemeli.
- **Rush**: sunucuda gerçek varyant (60 sn oyun, 10 sn tur, `AnalyzeRush` deadwood kıyası); istemcide `RushTimeoutPhase` +
  `GameRules.DiscardOnRushTimeout` ile yerel kopya. Port'ta yalnız sunucu zamanı gösterilir.
- **Finish payload**: `RequestFinish/SendFinish` `Params` = 11 kartın **sıralı** listesi (meld dizilimi anlam taşıyor); sunucu
  permütasyon doğrular, cevap gelmezse `HandSorter` otomatik dizer. Port'ta el sıralama UI'ı bu sözleşmeye sadık kalmalı.
- **Bot oyunu**: sunucuda bot AI yok; offline bot tamamen istemci. Port'ta bot = `Common.HandScore/HandSorter` üstüne ince
  strateji katmanı (mevcut 4 strateji dosyası ~40K'nın çoğu silinir).

### 2.5 Kalite notları (özet; kanıtlar rapor 01)
God class listesi: BotGameManager 1351, TableManager 725, CardExtensions 729, TableUIManager 661, TableHintController 584,
PlayService 581, Seat 565, PlayerHand 559, TableContext 475. Ölü kod: `SeatTimerOld.cs`. Magic number'lar
(`BotGameManager.cs:19-32` tur süreleri sunucudaki sabitlerle çakışıyor: istemci 25 sn, sunucu 20 sn!). Unity ekibinin kendi
`ARCHITECTURE_PLAN.md`'si (2267 satır, Şubat 2026) aynı teşhisi koyuyor; uygulanmamış.

---

## 3. Protokol ve paylaşılabilir kod

- Transport: **binary WebSocket**. Çerçeve `[requestId:long][endpoint:long][object]`; cevap `[requestId][object]`,
  `requestId==-1` → push event, `0` → ping. Codec tipli paket akışı (`Codec.PacketType`: NULL/TRUE/FALSE/SHORT/INT/FLOAT/LONG/
  UTF/DATE/LIST/DICT/ZOBJ…), nesneler `[CodecName]` kısaltılmış alan adlarıyla dict; polimorfik `DecodeObject()` tip adıyla çözer.
  Üst seviye deflate (`ZOBJ`, kendi `Inflater/Deflater`). Uygulama frame'i 60 sn gelmezse sunucu düşürür → 25 sn heartbeat
  (`Endpoint.Ping`). Kaynak: `core.upm\Runtime\Network\BestWebSocketClient.cs:303-307,449-456`.
- Masa aksiyonları tek `GameActionRequest{Action, Params:List<long>, …}` + `GameActionResult{Error, TableState, CallerEvent}`;
  Gin aksiyonları `GameAction` enum 700-710 (Pass/Ready/PickFromDeck/PickFromDiscard/Discard/RequestFinish/OpponentFinished/
  SendFinish/NewGame/NewRound/DeckEmpty), ortak push'lar 1-10. Rakip eli `FullView`'da maskelenir.
- **Yeniden kullanılacak**: `Digitoy.Common.dll` komple (modeller, Codec, Serializer, Compression, Ginrummy kuralları).
  core.upm'den **mantık olarak** port edilecek (Unity'siz yeniden yazım): `Server` durum makinesi (Idle/Connecting/Healthy/
  Reconnecting/Killed; relogin; disconnect nedenleri), `ServerDirectory` gateway seçimi, `DeviceKey` (CRC + sürüm), `Config`
  uzak ayar, `PlayerService/LobbyService/TableService/MessagingService` push yönlendirme, `TimeSync`.
- core.upm'nin **port edilmeyecek** %60'ı: `UI` (19 248 sat., Canvas/TMP/Addressables/DOTween), `Anim` (DOTween sarmalayıcı),
  `Dev` panelleri, `DialogService`. İstemcinin en çok kullandığı core tipleri: `Anim` 216, `Dialog` 178, `UILocale` 98,
  `Server` 90, `UIManager` 40, `UIButton` 34, `UICounter` 29, `UIParticleImage` 20.

---

## 4. MOTOR EKSİKLERİ — port'a engel sınıflandırması

Motorda **var** olup birebir karşılık gelenler: LayoutBox (anchor/pivot/flex layout/9-slice/gölge/metin), TextSprite (SDF,
outline/gradient), SpriteRenderer, ParticleSystem, MovieClip (doc koreografisi, label/action), Tween (zero-alloc), RenderGroup
(hiyerarşik alfa = CanvasGroup), ProgressRenderer (bar/radial/roundrect = `Image.fillAmount`), PixelEffect (.fx = grayscale/
shine; yuzbirextra'da örnekleri var), WebSocket (text+binary, headers), Http, PlayerPrefs, Audio (sfx + stream müzik), Prefab,
SceneLoader (additive), Module (pak modülleri), LocalizationTable + `@loc` bağı + `Format(args)`, PointerEvent (down/up/click/
drag), Camera projeksiyonları, AtlasGroup, Android/iOS ayarları, AOT (Common.dll linkleniyor).

### 4.1 🔴 BLOKER — oyun bağlanamaz/çalışmaz
| # | Eksik | Kanıt | Çözüm seçeneği |
|---|---|---|---|
| B1 | **`M.Serializer` AOT'de çalışmaz**: `Expression.Lambda(...).Compile()`, `MakeGenericType`, `Activator.CreateInstance(genericTip)`, `GetCustomAttribute<CodecNameAttribute>` ile tip adı→Type araması (`Common\Serializer.cs:21-78,162-231`, `CodecName.cs:32-79`). engine-notes: Common.dll yalnız **linklendi**, lean'de `M.*` 1772 kod ölü (hiç çalıştırılmadı). | engine-notes.md:210-262 | Common'a **source-generated codec** (tip başına `Encode/Decode` düz kod + ad→factory tablosu) — sunucu da aynı kodu kullanır, davranış değişmez. Editörde reflection yolu kalabilir (`#if`). Aotcompiler'da Expression desteği **yazılmamalı**. |
| B2 | **Regex yok** (corelib `Regex` gerçek motor değil) → `M.NickValidator.cctor` kırmızı | engine-notes.md:258 | NickValidator'ı regex'siz yaz (Common tarafı, küçük). |
| B3 | **JSON parser yok**: `Config` uzak ayarları, `ServerDirectory` seed listesi, IAP/offer katalogları Newtonsoft ile JSON | core.upm `Config.cs`, `ServerDirectory.cs` | Küçük reflection'sız JSON okuyucu (DOM) motor/corelib'e; ya da sunucu bu uçları Codec ile de sunuyorsa JSON'dan kaçın (doğrulanacak). |
| B4 | **Metin girişi bileşeni yok** (nick, özel masa kodu, chat, arkadaş arama, destek formu). `GameHost.EvKeyDown/EvText` var, odak/caret/IME/**mobil soft-keyboard açma** yok. | engine_api: TextField/InputField tipi yok | Motor: `TextField` component (LayoutBox üstü) + native `ShowKeyboard/HideKeyboard` köprüsü (Android/iOS). |
| B5 | **ScrollView yok** (lobi masa listesi, leaderboard, mağaza, inbox, arkadaşlar, emoji/sticker paneli, chat). `OverflowMode.Hidden` kırpma var, kaydırma/inertia/pooling yok. | engine_api | Motor: `ScrollBox` (LayoutBox + drag/inertia/clip + sanal liste kancası). |
| B6 | **Ana-thread/async sözleşmesi**: motor tek-thread (ContinueWith yasak, `Task.Run` yok). `BestWebSocketClient` worker thread + lock + `TaskCompletionSource` modeli. | engine-notes.md:671-693 | Ağ katmanı motorun `WebSocket.OnMessage` + `NativeAsync` ile sıfırdan; Common `Codec.Thread` ThreadStatic sorunsuz. Bloker değil ama "kopyala-yapıştır" yolunu kapatır. |

### 4.2 🟠 ÖNEMLİ — oynanır ama ürün çıkmaz (native/3. parti köprüler)
Motorda hiçbiri yok; her biri native shim + managed API ister. Gameplay port'u bunlarsız ilerler, mağaza sürümü ilerlemez.
IAP (Unity IAP, 22K `IAPManager`), reklam (MaxSdk; interstitial + rewarded "Watch&Collect"), Facebook login/paylaşım (59 kullanım),
Apple Sign-In, push bildirim izni/token, Adjust (25), Helpshift (20), Firebase (1), uzak avatar yükleme (Http var → yalnız decode
hattı: `AsyncJobs.ReadAsync` + `Texture` runtime yükleme doğrulanmalı), `Application.OpenURL`/paylaşım/panoya kopyala (özel
masa kodu), haptic, güvenli alan (`SafeArea.cs` 10K → `Screen` yalnız w/h/pixelRatio veriyor; **inset API yok**), ağ durumu
probu, uygulama pause/resume event'i (GameHost.Pause/Resume var ✔).

### 4.3 🟡 ORTA — iş gücü, yeniden yazımla çözülür
- **DOTween → Tween/MovieClip**: 307 kullanım; `DOPath/DOLocalPath` (kart yolları), `DOShake*`, `DOPunchScale`, `DOLocalJump`
  motorda tek ease'e sığmaz → `Curve` ile parametrik veya MovieClip'e iner. Zincir `Sequence` → `await` + TweenHandle.
- **Animator/.anim (39+39)** → MovieClip'e yeniden author (doc'ta). Spine yok ✔.
- **Mask/RectMask2D + iç içe scroll** → `OverflowMode.Hidden` yeterli mi (dönmüş/ölçekli kırpma, stencil iç içe) doğrulanmalı.
- **UIParticle (100 kullanım; Coffee)** → `ParticleSystem` UI katmanında SortingOrder ile; `UIParticleAttractor` (coin'ler
  hedefe akıyor) motorda yok → kod.
- **UILineBorder (custom mesh, 11K)** → LayoutBox border/radius + ProgressRenderer RoundRect (perimetre halkası var ✔).
- **CameraAnimator (FOV/dolly)** → `PerspectiveProjection` + Tween; 2D oyunda gerekliliği sorgulanmalı.
- **Font/TMP**: .ttf yok, TMP asset'leri → motor MSDF importer (var ✔); emoji/çok dilli glyph kapsamı `loc.glyphCheck` ile.
- **Dialog sistemi** (178 kullanım, 26+ dialog): motorda "Dialog/Popup yığını" primitifi yok → oyun kodunda `DialogStack`
  servisi + prefab; modal arka plan, geri tuşu (`GameHost.BackRequested` var ✔), açılış/kapanış MovieClip.
- **Addressables/uzak içerik** (album, avatar paketleri) → `Module.LoadAsync(pak)` + Http; CDN indirme/önbellek politikası yok.
- **Video** (1 mp4, 0.33 MB) → kaldır ya da sprite sheet.

### 4.4 ⚪ Araç/boru hattı
- Unity `.unity/.prefab/.asset` → `.scene/.prefab` dönüştürücü **yok**; 272 prefab, 6 sahne elle (scene.apply ile) kurulacak.
  Yarı otomatik bir "RectTransform+Image+TMP → LayoutBox" çevirici (editör aracı) 2-3 günlük yatırım, haftalar kazandırır.
- Locale `.asset` (27 dosya × 2 dil, Unity YAML) → `LocalizationTable` içe aktarıcı (küçük script).
- Asset hacmi: png 225/77 MB, psd 266/31 MB (kaynak), wav 30/47 MB (→ sıkıştırma), mp3 33/9.5 MB. Atlaslama motorda var.

---

## 5. Önerilen port stratejisi (plan değil, yön — onay sonrası detaylanır)

1. **Önce boru hattını kanıtla (dikey dilim)**: Common.dll codegen'li serializer → motor WebSocket üzerinden gerçek sunucuya
   login + `TablePlayNow` + bir tur `PickFromDeck/Discard` → `scene.dump` ile doğrula. B1-B3 burada kapanır; hiçbir UI yok.
2. **Domain katmanı Unity'den bağımsız**: `GinTable` (TableState projeksiyonu, tek state machine: Phase×SubPhase sunucudan),
   `GinRules` = `Common.HandScore/HandSorter/RoundScorer` (yerel ipucu/bot/UI uygunluğu hepsi buradan). Bot = aynı tablo
   modeline `ILocalDealer` ile besleme (online/offline **tek** akış).
3. **View katmanı doc-first**: masa sahnesi LayoutBox/Sprite + MovieClip (deal, slam, meld, round-over koreografileri doc'ta);
   kod yalnız tepkisel tween + input. `TableContext` yerine sahne referansları component alanı.
4. **UI çerçevesi minimal**: `DialogStack`, `Button`(yuzbirextra'da var), `ScrollBox`, `TextField` → sırayla motor eksikleri
   (B4, B5) bu adımda motor tarafına yazılır.
5. **Lobi/meta özellikleri** öncelik sırası ürün kararı: PlayNow+masa listesi → profil/ayarlar → günlük bonus/misyon →
   mağaza (IAP köprüsü gerekir) → sosyal (chat/hediye) → leaderboard/sezon → mini oyunlar.
6. **Native köprüler** (4.2) ayrı iş paketi; gameplay port'unu bloklamaz.

### Açık sorular (kullanıcıya)
- Hedef platform sırası (Android/iOS/WASM?) — native köprü önceliğini belirler.
- Sunucu `Config`/`ServerDirectory` uçları JSON zorunlu mu, Codec alternatifi var mı? (B3)
- Common'a source-generator eklemek sunucu ekibi açısından kabul edilebilir mi? (B1 — en temiz çözüm)
- Bot/offline oyun kapsamda mı (ilk sürüm için)?
- Hangi meta özellikler ilk sürümde zorunlu (mağaza/reklam dahil mi)?
