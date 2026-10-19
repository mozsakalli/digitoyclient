# Login.scene (Yüzbir) — niyet günlüğü (append-only)

## 2026-10-18 — Unity yuzbirextra LoginScene'den aktarım (KAYDEDİLMEDİ, undo'lu)
Prompt: "101 loginscene sahnesini import et; PSB importer var; butonlar vs LayoutBox ile, fazladan asset alma."
Kaynak: `yuzbirextra.unity/Assets/YuzbirExtra/Scenes/Login/LoginScene.unity` (Canvas 1920×1080 Expand) YAML'dan rect/anchor
değerleri okunarak; Unity MCP yok. Alınan görseller YALNIZ `Games/Yuzbir/Login/Sprites/{Logo.psb, bg-login.jpg}` —
Logo.psb motor PsdImporter ile katmanlara ayrılır (`Logo.psb#12 Shadow, #21 Base, #17 Text`; #29 Background kullanılmıyor).
Unity buton sprite'ları (ButtonFb1/ButtonDefault1/ButtonApple1, button_light, light-red-horizontal) ALINMADI: renkleri
örneklenip LayoutBox fill/border/light ile çizildi.
Yapı (Unity px 1:1, y ters; referans 1920×1080):
- Camera(1) CameraComponent + FitPerspectiveProjection fov 30, ref `1920 1080`.
- Background(2) LayoutBox fitScreen, sprite bg-login.jpg Cover = UI kökü; + LoginFlow (game=Games/Yuzbir/Game.asset [gameId 5],
  network=Shared/Settings.asset, autoLoginLastType=false dev).
- Logo(3) 570×456 merkez `0 -75`, scale 1.52 (Unity 0,75 / 1.52). Çocuklar: Shadow(4) #12 570×441 `0 -7` fill #0f0f3dd9
  (Unity tint), Base(5) #21 518×391 `0 -32.5`, Text(6) #17 410×313 `0 -32.5`. + RenderGroup + MovieClip **Splash**
  (Fps 30, 16 kare, PlayOnStart): Scale 3→3 (f0–3) →1.52 OutBack (f15); Alpha 0→1 (f0–3). Unity `[MovieClip] Splash` karşılığı.
- LoginForm(7) 1476×120 merkez `0 305`, layout Horizontal spacing 40 justify Center (Unity HorizontalLayoutGroup+ContentSizeFitter):
  FacebookButton(8) 430×120 fill #2445ef→#1736d1, üst 2px açık kenar, radius 22, gölge, lightColor (Unity "Light" overlay'i
  yerine yerleşik), Primary 64, `<font=icon>a</font>  CONNECT`; GuestButton(9) 430×120 fill #0000004d (Unity ButtonDefault1 =
  yarı saydam siyah + üst beyaz çizgi), "GUEST LOGIN"; Or(10) [inaktif, Unity'de yalnız iOS] LineTop/LineBottom/OrText;
  AppleButton(14) [inaktif] 436×120 beyaz. + RenderGroup + MovieClip **LoginFormIntro** (12 kare, PlayOnStart; GO LoginFlow
  tarafından aktive edilince oynar — kod değişikliği yok): anchoredPos `0 600`→`0 305` OutBack (f0–9), Alpha 0→1.
- Agreement(15) alt-orta `0 -85` 1500×60 Regular 38 rich text (loc `login.agreement`, Gin ile ortak) + PolicyLink.
- Progress(16) merkez `0 302` 400×60 → Bar(17) ProgressRenderer Bar 400×60 radius 30, Track beyaz, Fill yeşil #6ddb3a→#45b020,
  Padding 10 (Unity Track/Thumb).
- VersionLabel(18) sağ-alt `-110 -44` Regular 30 (loc `login.version_label`, LoginFlow yazar); ErrorText(19) `0 -200` kırmızı.
Loc: metinler aynıysa editör Gin anahtarlarını paylaştı (`login.facebook_button/apple_button/or_text/agreement/version_label`);
farklı olan `yuzbir.login.guest_button` = "GUEST LOGIN". Tabloda yalnız `en`; Unity TR metinleri (BAĞLAN / MİSAFİR GİRİŞİ /
YA DA / "Giriş tuşlarına basarak … kabul etmiş olursunuz.") `tr` dili eklenince `loc.set` ile girilecek.
Invariantlar:
- Referans 1920×1080, Background tek UI kökü, Unity px 1:1; buton = LayoutBox stili (sprite yok), ikon `<font=icon>`.
- Logo katmanları PSB alt-öğesi (`Logo.psb#id`); yeni katman gerekirse PSB'ye eklenir, PNG export edilmez.
- Koreografi doc'ta (Splash, LoginFormIntro); LoginFlow görünürlüğü SetActive ile yönetir, MovieClip PlayOnStart ile tetiklenir.
- Or/Apple inaktif; iOS'ta açma kararı LoginFlow'a (platform) ait, sahneye değil.
Doğrulama: iphone-14-pro-max landscape (932×430). f7 Splash büyük logo + fade; f82 form + agreement; anim.goto LoginForm f4 yarı
saydam kayma. input_tap Guest (560,336) hit → test-node Authenticated → PlayerId=1031 → `Lobby.scene` yok (SceneNav hata;
Lobby placeholder sonraki adım).
Eksik: Lobby placeholder, PlayerSettings scenes/startScene (varyant), `tr` dili, Unity Background alpha fade (Splash'ta
Background'a RenderGroup koymak tüm çocukları soldurur — atlandı), Apple glyph/iOS görünürlüğü.

## 2026-10-18 — ErrorToast + Splash v2 (KAYDEDİLMEDİ, undo'lu)
Prompt: "Login errorText hata durumunda gelsin, bir süre dursun, gitsin; prefab olsun" / "açılış introsu çok basit kaldı, mevcutu iyileştir".
ErrorToast: ErrorText(19) → ErrorToast; LayoutBox sol-üst anchor `0 0`, anchoredPos `0 20`, pivot X ile görünürlük (1 = ekran dışı sol,
0 = görünür; boyuttan bağımsız). MovieClip **Toast** 30 fps 171 kare, PlayOnStart kapalı: pivot 1→0 OutBack f0–10, Hold f10–160 (5 sn),
→1 InCubic f160–170. `ClipToast` (Shared/Ui) text+clip ref; LoginFlow.errorToast bağlı (toast varsa errorText kullanılmaz; Gin eski yol).
Prefab: kullanıcı `prefab.create` ile Shared/Ui/ErrorToast.prefab yaptı (bu oturumda MCP araç listesinde yoktu).
Splash v2 (Logo(3) klibi 16→40 kare; parçalar ayrı): Background(2) RenderGroup alpha 0→1 f0–12 (sahne fade); Base(5) RenderGroup
alpha 0→1 f0–6 + scale 2.5→1 OutExpo f0–14; Text(6) alpha f8–14 + scale 0→1.15 OutBack f8–20 →1 OutSine f26; Shadow(4) alpha f10–22
+ anchoredPos y −20→−7; Logo(3) scale 1.52→1.58→1.52 OutSine f20–34 (nefes). Logo RenderGroup kaldı, artık track'i yok.
Authored değerler = dinlenme hâli (Alpha 1, scale 1 / 1.52, pivot 1 0 toast için); kare 0 klipten gelir.
Invariantlar: toast yalnız pivot.x ile hareket eder (konum/boyut değişse de çalışır); splash sırası Base → Text → Shadow → nefes;
Shared'a oyun görseli kopyalanmaz. Doğrulama: sim captureEvery 6 — f6 çerçeve büyük/soluk, f12 yazı küçük, f18 overshoot, f24 oturdu;
toast anim.goto f0 offscreen, f5 x=10, f100 x=0, f166 çıkıyor.
Ek: çarpışma varyantı — Text(6) 2.5→1 InQuad f0–14 (büyükten), Base(5) 0.2→1 InQuad f0–14 (küçükten), ikisi de alpha f0–6; temas f14:
Logo(3) 1.52→1.64 OutQuad f14–18 → 1.52 OutBack f30 (darbe); Shadow f14–24. Doğrulama: f5 yazı dev/çerçeve nokta, f10 yaklaşıyor, f15 temas, f20 şişmiş.
Ek: Text(6) alpha track'i kaldırıldı (yalnız scale). Form/agreement girişi Splash klibine taşındı (çarpma anına kilit): LoginForm(7)'deki
**LoginFormIntro MovieClip silindi**; Splash'ta 7:LayoutBox anchoredPos `0 600`→`0 305` OutBack f14–26, 7:RenderGroup alpha f14–22,
Agreement(15) RenderGroup alpha f18–30. Invariant: Splash tek koreografi sahibi; LoginFlow yalnız SetActive yapar. Not: settings yavaş gelirse
form klip bittikten sonra aktive olur → son kare değerleriyle (görünür) direkt belirir. Doğrulama: f10 form yok, f20 butonlar kayarak gelmiş, f25 agreement.
