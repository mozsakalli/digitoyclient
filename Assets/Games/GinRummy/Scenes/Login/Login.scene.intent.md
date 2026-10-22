# Login.scene — niyet günlüğü (append-only)

## 2026-10-16 — Unity LoginScene'den aktarım (KAYDEDİLDİ; sahne geçişi testi diskten okuduğu için)
Prompt: "LoginScene'e geçelim; Unity Gin login sahnesini birebir aktar."
Kaynak: `ginrummyextra.unity/Assets/GinRummyExtra/Scenes/Login/LoginScene.unity` (Canvas 1920×1080, width-match) YAML'dan
rect/anchor/sprite değerleri okunarak; Unity MCP kullanılmadı.
Yapı:
- Camera(1): CameraComponent + FitPerspectiveProjection fov 30, referenceResolution **1920 800** (= Unity width-match: her
  aspect'te genişlik 1920 birim; cover olduğundan yükseklik aspect'e göre 800+). Tüm değerler Unity px birimiyle 1:1.
- Background(2): LayoutBox fitScreen, sprite `Login/Sprites/background.jpg` Cover; **UI kökü** (anchor'lar yalnız parent kutu
  içinde çalışır — kökte transform'a düşer). + LoginFlow (Shared/Controllers) — ref'ler: loginForm, agreement, progressRoot,
  progress(Bar), versionText, errorText, guest/facebook/appleButton, game=Games/GinRummy/Game.asset, network=Shared/Settings.asset,
  autoLoginLastType=false (dev; prod'da true).
- Logo(3) anchor merkez, anchoredPos `0 -150`, scale 0.6 (Unity 220/0.66 → ekran ölçümüne göre düzeltildi). Çocuklar Unity
  değerleri, **y işareti ters** (motor y aşağı): CardsNSuites(4)+RenderGroup [Card_1..4, Suit_1..4 = Clubs/Diamonds/Hearts/Spades],
  Base, Neon, Gin, Rummy, Extra. Sprite'lar `Games/GinRummy/Scenes/Login/Sprites/` (Unity PNG, ~750 KB; SVG'ye geçiş ayrı iş).
- LoginForm(18): anchor alt-orta (`0.5 1`), pivot `0.5 0` (üst kenar), anchoredPos `0 -420`, 870×260. İçinde anchor sol-üst
  (`0 0`): PlatformButtons(19) 435,55 → FacebookButton(20) 535,45 (Apple(21) pasif, iOS'ta LoginFlow açar); Or(22) 435,128
  (Line1/Line2 ±100, OrText Bold 28); GuestButton(26) 435,202. Butonlar 420×90, radius 10, border `2 2 2 4` (alt koyu kenar),
  Primary 44, LayoutBox fill gradient (FB #4db4ff→#1a8cf5, Guest #8b45ff→#4b1c9e) + `Button` component (Shared/Ui).
- Agreement(27) alt-orta `0 -104` Regular 34; Progress(28) `0 -380` (LoginFlow gizler/gösterir) → Bar(29) ProgressRenderer
  RoundRect 400×40; VersionLabel(30) sağ-alt `-120 -70`; ErrorText(31) `0 -300` kırmızı (DialogStack gelene kadar).
Invariantlar:
- Referans çözünürlük 1920×800 ve Background = tek UI kökü; yeni öğe daima Background altına, Unity px değeriyle.
- Alt grup (form/agreement/progress/version) **alttan** anchor'lı, logo merkezden; üstten sabit px kullanma.
- Butonlar sprite değil kutu stili (token'a geçecek); FB/Apple tıklaması Faz 5'e kadar stub (log).
- Sahne kaydı: sahneler arası geçiş diskten okur → geçiş testi için hedef sahne kaydedilmiş olmalı. `scene_open` kaydedilmemiş
  sahneyi reddeder; reddedilince sonraki apply/save MEVCUT sahneye gider (bir kez yanlışlıkla Lobby içeriği Login'e yazıldı,
  temizlendi).
Doğrulama: iphone-14-pro-max landscape (932×430) f40 screenshot — Unity ekran görüntüsüyle oran kıyası (logo %31, CONNECT %59,
OR %67, GUEST %75, agreement %88). input_tap Guest (466,323) hit → progress → test-node Authenticated → PlayerState →
SceneNav Lobby.scene (replace) → "LOBBY (placeholder)".
Eksik (sonraki tur): FB ikonu, agreement link parçaları (Privacy/Terms bold+altçizgi+PolicyLink), turuncu bokeh parçacıklar
(Unity LogoParticle), logo intro MovieClip (Unity LogoAnimation DOTween zinciri), spinner, Play stop'ta Server.Disconnect.

## 2026-10-16 — İkon fontu: SVG glyph (KAYDEDİLDİ)
Prompt: "fonts/icons altındaki SVG'leri glyph olarak ekle; FB logosu buradan gelsin."
Yapı: Boş TTF yok — `Shared/Fonts/Digitoy-Primary.ttf.meta` `customGlyphs` (motor FontImporter Faz 1, 2026-10-16): 36 SVG,
PUA `U+E000 facebook`, `U+E001 apple` (Unity ile aynı kodlar), `U+E002..E023` alfabetik. Kod sabitleri `Shared/Ui/Icon.cs`
(`Icon.Facebook` …), meta ile birebir. FacebookButton metni loc `login.facebook_button` = `"\uE000  CONNECT"` (loc.set ile).
Invariantlar: yeni ikon = SVG'yi Icons/'a koy + meta'ya sıradaki PUA + Icon.cs'e sabit (üçü birlikte); ikonlar yalnız fill
(stroke/gradient yok sayılır); renk metin rengiyle gelir. Başka font ikon isterse aynı customGlyphs listesi o meta'ya kopyalanır.
Tuzak: loc.set/asset dosyası yazımı gecikmeli — hemen diskten okuyunca eski değer görünür.
Doğrulama: render_screenshot (330,220,280,70) — FB logosu + CONNECT, Unity ile aynı.
Hizalama (23:05): ikon glyph'leri baseline'a 1 em oturuyordu → büyük harften yukarı taşıyordu. Primary capHeight=700/1000 →
tüm customGlyphs `scale: 0.72`, `offsetY: -0.02` (metin kutusuyla aynı yükseklik). KURAL: ikon = cap-height ölçeği; başka
fonta taşınırsa o fontun capHeight/unitsPerEm oranı kullanılır; yuvarlak ikonlar optik için +0.04 alabilir.

## 2026-10-16 — Logo intro MovieClip + logo ölçeği (KAYDEDİLDİ)
Prompt: "logo MovieClip; logo Unity'dekinden bir tık büyük."
Yapı: Logo(3) scale 0.6 → **0.5** (Unity ekran ölçümü: Rummy genişliği ≈ %19). Logo(3)'e MovieClip: Fps 30, FrameCount 60,
Loop false, PlayOnStart true; Unity `LogoAnimation.PlayIntro` (DOTween, masterSequence timeScale 0.8 → süreler ×1.25) birebir:
Base scale 3→1 OutExpo (f0–15); Neon scale 0→1.1 OutBack (f4–15) →1 (f21), alpha 0→1 + 3 flicker (f21–43); Card_1..4/Suit_1..4
anchoredPos merkez→konum OutBack 15f + alpha 11f, 2f stagger; Rummy y −184→16 OutBounce (f4–22), scale 0→1.2 OutBack →1
OutElastic →1.05→1; Gin y −47→−147 OutBack + scale 0→1 (f10–25); Extra y 19→119 OutBack (f11–30). Fade için Card/Suit/Neon/
Gin/Rummy/Extra GO'larına RenderGroup eklendi (LayoutBox'ın alpha kanalı yok). Frame 0 = başlangıç pozları (Hold).
Tuzaklar: `comp.add` props'u (iç içe liste/ref) yazmıyor → `prop.set` ile; `Keys[].Value` daima 4 bileşen ("1 0 0 0");
`Tracks` tek prop.set ile liste olarak yazılabiliyor. Unity `sceneOverlay` fade ve `PlayOutro` (lobby'ye geçiş) alınmadı.
Invariantlar: logo koreografisi doc'ta (MovieClip), kodda Tween yok; yeni logo parçası = RenderGroup + track.
Doğrulama: anim.goto f8/f20/f59 screenshot — f59 statik sahneyle birebir.

## 2026-10-16 — Logo yukarı + bokeh parçacıkları (KAYDEDİLDİ)
Prompt: "logoyu biraz yukarı taşı; particle'ı ekle."
Yapı: Logo(3) anchoredPos `0 -150` → `0 -185`. LogoParticle(32) LayoutBox (anchor merkez, `0 -185`, Logo ile aynı konum),
Logo'nun ÖNÜNE (kardeş sırası: arkada çizilir). 3 ParticleSystem çocuk — Unity `LogoParticle.prefab` (6 emitter, UIParticle,
Additive) sadeleştirilmiş karşılığı, Additive, prewarm, loop, Box shape `~1000×260`, randomDirection 1, hız 0–20 u/s, noise:
Big(33) particle_2, 3/s, 20–70 px, alpha 0.07 tepe, #ffb060→#ff8a1a; Small(34) particle_1, 7/s, 3–12 px, alpha 0.6, #ffd9a0→#ff9a2a;
Glitter(35) particle_2, 4/s, 8–30 px, alpha 0.35. Alpha curve fade-in/out (0→tepe→0). Dokular `Shared/Ui/Sprites/particle_{1,2}.png`
(Unity Login/Textures, 128², ortak kullanım için Shared).
Invariantlar: parçacık yoğunluğu düşük (ekranda ~25–35 adet), logo daima önde (LogoParticle Logo'dan önce gelir); renkler sıcak
turuncu paleti, beyaz kullanılmaz; Additive.
Doğrulama: f90 screenshot — Unity ekranıyla doku kıyası (seyrek bokeh, yatay bant).
Fade (23:20): `alpha` Curve modunda eğri `min..max` aralığını ölçekler — min/max 0 bırakılınca eğri etkisiz (tam opak) çıkıyordu.
Düzeltme: eğri 0→1→1→0 (0.25/0.75 omuz), `alpha.max` = tepe: Big 0.25, Small 0.9, Glitter 0.7. KURAL: Curve modunda değeri
max'a yaz, eğri normalize.
Ayar turu (23:23–23:35): oran 5/11/6 adet/sn; boyut Big 30–110, Small 4–16, Glitter 12–45; bant 380 yüksek; hız 10–45 u/s,
noise 25–40. LogoParticle(32)'ye RenderGroup; Logo MovieClip'e 2 track: Scale 0→1 OutBack (f8–19) + Alpha 0→1 — Unity
`uiParticleParent` 0.2 s'de Rummy çarparken açılır. Invariant: parçacık kökü klibe bağlı, PlayOnStart öncesi görünmez (frame 0 scale 0).

## 2026-10-16 — Agreement linkleri (KAYDEDİLDİ)
Prompt: "agreement tamamla."
Yapı: LayoutBox inline rich text yok → Agreement(27) text boş (loc unbind), layout Horizontal, spacing 9, align/justify Center,
1100×60. Çocuklar sırayla: Txt1(36) "By logging in you accept our" | Privacy(37) "Privacy Policy" | Txt2(38) "and" | Terms(39)
"Terms of Use"; hepsi 34, textWrap false, gölge. Linkler Digitoy-Bold + `border 0 0 0 2` beyaz (alt çizgi) + BlocksRaycast +
`PolicyLink` (Shared/Ui; linkId privacy|terms, fallbackUrl; OpenURL motor B4 gelene kadar Debug.Log). URL'ler settings JSON
(privacy_url/terms_url) gelince Config'ten okunacak.
Invariantlar: cümle parçaları sıra = anlam; aralık Agreement.spacing (çocuklara margin yok); link = Bold + 2px alt border;
düz parça Regular. Parça metinleri loc'ta ayrı anahtarlar (çeviride cümle yapısı değişirse parçalar yeniden düzenlenir — C4 rich text gelince tek metne iner).
Doğrulama: render_screenshot (300,370,340,45) — tek satır, linkler kalın+alt çizgili; input_tap Privacy (480,393) ve Terms (585,394) hit, log'da PolicyLink.

## 2026-10-17 — Referans çözünürlük 1920×1080 + Unity boyutlarıyla hizalama (KAYDEDİLMEDİ, undo'lu)
Prompt: "camera referans çözünürlük ne olmalı; elemanlar Unity'den küçük."
Bulgu: `FitPerspectiveProjection.referenceResolution` davranışı **contain / min-ölçek** = Unity CanvasScaler `Expand`
(scale = min(W/refW, H/refH)); yukarıdaki "1920 800 = width-match/cover" notu YANLIŞTI. Unity LoginScene scaler 1920×1080 Expand
→ referans **1920 1080** birebir eş. Küçüklük kameradan değil eleman boyutlarından geliyordu.
Yapı: Camera(1) referenceResolution `1920 1080`. Logo(3) scale 0.66, anchoredPos `0 -220` (Unity 0.66 / 0,220); LogoParticle(32)
`0 -220`. LoginForm(18) 870×**290**, anchoredPos `0 -500` (Unity alt-anchor y=210 + 290 = üst kenar 500). PlatformButtons(19)
`435 56`, 1070×112; FacebookButton(20) `260 56`, AppleButton(21) `810 56`; Or(22) `435 153.5`; GuestButton(26) `435 251`.
Butonlar **520×112** (Unity prefab override). textSize 60 korundu (Unity 64/56 karışık).
Invariantlar: referans 1920×1080 tüm sahnelerde; Unity RectTransform değerleri 1:1, yalnız y işareti ters; buton 520×112.
Doğrulama: sim_run f90 (932×430) screenshot — Unity ekranıyla buton/logo oranları eş (buton ≈ ekran genişliğinin %22'si).
Not: Apple butonunda  glyph'i fontta yok ("?" çıkıyor) — ayrı iş. Lobby.scene kamerası hâlâ `1920 800`, sahne açılınca düzeltilecek.

## 2026-10-17 — Agreement rich text'e indi (KAYDEDİLMEDİ, undo'lu)
Prompt: "agreement → richtext; font isimlerini doğru oku."
Motor: rich text geldi (`<b> <i> <u> <color> <size> <font=ad> <link=id>`; `LayoutBox.LinkAt(e)`), `LayoutBox.font` artık **ad**
(`fonts_list`: regular/light/bold/primary), textSize artık **em** (TMP fontSize 1:1), JSON geldi.
Yapı: Agreement(27) tek LayoutBox: layout None, font `regular`, textSize 34, BlocksRaycast, textShadow #000000aa; `text@loc`
`login.agreement` = `By logging in you accept our <link=privacy><u><font=bold>Privacy Policy</font></u></link> and
<link=terms><u><font=bold>Terms of Use</font></u></link>`. + `PolicyLink` (Shared/Ui; `privacyUrl/termsUrl`, OnPointerClick →
`LinkAt(e)` → id → URL; OpenURL B4 gelene kadar log). Txt1/Privacy/Txt2/Terms (36–39) silindi; `login.txt1/privacy/txt2/terms`
anahtarları silinir (editör zaman aşımı — kontrol et).
Invariantlar: tek metin / tek loc anahtarı (çeviri cümle yapısını serbestçe kurar); link = `<link=id><u><font=bold>…`; gerçek Bold
yüz `<font=bold>` ile (SDF `<b>` değil); link tıklaması yalnız link glyph'inde (düz metin tıklaması sessiz).
Doğrulama: render_screenshot (300,385,340,40) — Regular gövde + Bold altı çizgili linkler tek satır; input_tap (487,404) privacy,
(565,404) terms log'da; (400,404) düz metin → log yok.
Açık: sahnedeki diğer `font` alanları hâlâ guid (eski biçim) — motor çözüyor ama adlara çevrilmeli (ayrı tur). textSize'lar
Unity'ye eşitlenmedi (Paket 1 onay bekliyor).

## 2026-10-17 — Parçacıklar: turuncu noktalar (KAYDEDİLMEDİ, undo'lu)
Prompt: "particle'larda Unity'de biraz daha turuncu noktalar var."
Bulgu (Unity LogoParticle.prefab, 5 aktif emitter): startColor doygun **#ffa200** (big α0.125, small_2, small_0), glitter #ffb900,
small_1 #ffcb7c; small_0/glitter **circle_2** (keskin dolu daire), small_1 **circle_1** — bizde hepsi yumuşak particle dokusu
ve soluk şeftali renklerdi.
Yapı: dokular `Shared/Ui/Sprites/circle_1.png` (64²) / `circle_2.png` (124²) eklendi. Big(33) #ffa200→#ff8a00, Small(34)
#ffa200→#ff9000, Glitter(35) #ffb900→#ffa200 + sprite circle_2. Yeni: Dots(36) circle_2, 10/s, 6–24 px, #ffa200→#ffb900;
Specks(37) circle_1, 10/s, 2–5 px, #ffcb7c→#ffb050. İkisi de Additive, prewarm, Box 1000×380, hız 10–40, noise 2/40,
alpha eğrisi 0→1→1→0 tepe 0.9.
Invariantlar: palet doygun turuncu (#ffa200 tabanı), beyaz yok; keskin nokta emitter'ları (Glitter/Dots/Specks) circle dokusu,
yumuşak bokeh (Big/Small) particle dokusu; toplam oran ~41/s.
Doğrulama: sim_run f90 + (200,20,300,200)×2 yakın plan — Unity ekranıyla doku kıyası.
Hareket turu (11:15): "kutuda hapis" hissi — Unity Rectangle emitter ~1900×450 birim, startSpeed≈0, hareket yalnız noise
(küçükler freq 2, büyükler 0.5). Yeni: Big Box `1900 450`, Small/Dots/Specks `1800 420`, Glitter `2000 500`; speed 0–12 (Big 0–8);
noise Big/Glitter 70@0.5, diğerleri 55@1.5; Small/Dots oranı 14/s (alan büyüdü). Invariant: hareket = yavaş geniş genlikli noise,
hız değil; emitter alanı ekran genişliğini aşar (kenar görünmez). Doğrulama: f200 ve f260 screenshot — tüm genişlikte dağılım,
2 s'de belirgin sürüklenme.
Hareket turu (11:15–11:17): "kutuda hapis" hissi. İlk deneme (kutuyu 1900×450'ye büyüt + yavaş noise) GERİ ALINDI — kullanıcı:
sorun kutu değil, lifetime ve hız aralığı dar. Uygulanan: lifetime 3–14 s (Glitter 3–10), speed Random 5–120 (Big 5–80,
Glitter 10–140), maxParticles Big/Glitter 80, Small/Dots/Specks 160; kutular 900–1100×380 aynı. Invariant: saçılma = geniş
ömür × hız aralığı (yavaş/kısa + hızlı/uzun karışımı), emitter alanı değil. Doğrulama: f200 screenshot — parçacıklar kutunun
dışına, ekranın altına kadar iniyor; Unity'den biraz daha dağınık (kullanıcı kararına bırakıldı).
Rötuş (11:17): kutu yüksekliği 380 → 260 (tüm emitter'lar), genişlikler 900/1000/1100 aynı — bant logo etrafında toplu, saçılma hız/ömürden.

## 2026-10-17 — İkon fontu ayrıldı: `icon` + ASCII harf + rich text (KAYDEDİLMEDİ, undo'lu)
Prompt: "boş icon.ttf yap, SVG'leri ona ekle, a b c kullan; ikonlu metinleri rich text'e çevir."
Yapı: `Shared/Fonts/Digitoy-Icon.ttf` (fontTools ile boş TTF: .notdef + space, upm 1000, cap 700). Meta `charset: 32`,
customGlyphs 36 SVG → **a–z, A–J** (a facebook, b apple, c add_friend … z search_friend, A smiley … J warning; Icon.cs ile birebir),
scale 0.72 / offsetY −0.02. `fonts_register` → ad **`icon`**. `Digitoy-Primary.ttf.meta` customGlyphs boşaltıldı (saf metin fontu).
`Shared/Ui/Icon.cs`: sabitler harf, `Icon.Tag(x)` = `<font=icon>x</font>`. Loc: `login.facebook_button` = `<font=icon>a</font>  CONNECT`,
`login.apple_button` = `<font=icon>b</font> Sign in with Apple`. Buton/OrText `font` alanları guid → ad (primary/bold).
Invariantlar: ikon = `<font=icon>HARF</font>` (PUA yok, `\uE0xx` yok); yeni ikon = SVG + Icon meta'ya sıradaki harf + Icon.cs sabiti;
Primary/Bold/Regular/Light'ta customGlyphs olmaz.
Tuzak: yeni .ttf'yi editör meta yazılmadan önce görürse kendi guid'ini verir; sonradan yazılan meta farklı guid taşırsa
`fonts_register` boş `font` bağlar (`fonts_list`'te `font: ""`). Çözüm: meta guid'ini `asset_info`'nun verdiğine eşitle, reimport,
yeniden register. Ayrıca font tablosu play başında yüklenir — register sonrası `play_stop` + `sim_run`.
Doğrulama: render_screenshot (250,228,435,50) — FB ve Apple logoları doğru, "?" yok.

## 2026-10-18 — Parçacık dokuları Gin'e taşındı
Prompt: "Gin'de kullandığımız particle imajlarını Shared'a koymuşsun." `Shared/Ui/Sprites/{particle_1,particle_2,circle_1,circle_2}.png` yalnız bu
sahnenin logo parçacıklarıydı → `git mv` ile `Games/GinRummy/Scenes/Login/Sprites/` altına (guid korundu, sahne değişmedi; asset_validate temiz).
Kullanılmayan `Login_Spinner, UI_ButtonShadow, UI_rect_16_Border/Fill` silindi; `Shared/Ui/Sprites` klasörü kalktı.
Invariant: Shared/ altında oyuna özgü görsel durmaz; ortak görsel gerekirse Shared prefab'ının kendi klasöründe.

## 2026-10-18 — Form butonları fade-in (KAYDEDİLMEDİ, undo'lu)
Prompt: "Login'de butonlar fade-in ile gelsin, aralarında minik delay; Or da."
Yapı: FacebookButton(20), AppleButton(21, pasif), Or(22, Line1/Line2/OrText'i kapsar), GuestButton(26) → RenderGroup Alpha 0 (authored).
LoginForm(18) MovieClip **LoginFormIntro**: Fps 30, 21 kare, PlayOnStart, Loop kapalı; Alpha 0→1 OutQuad 10 kare, 5 kare kademeli:
Facebook+Apple f0–10, Or f5–15, Guest f10–20 (önceki key Hold). LoginFlow formu SetActive edince oynar (Yüzbir ile aynı; kod yok).
Invariant: form giriş koreografisi doc'ta; yeni buton = RenderGroup + track, sıra üstten alta 5 kare arayla.
Doğrulama: anim.goto f7 (Connect belirgin, Or soluk, Guest yok) / f20 (hepsi tam).
Ek (aynı gün): butonlara (20/21/26, Or hariç) Transform Scale 0.9→1 OutBack, alpha ile aynı pencerede; authored scale 0.9 (kare 0).
Tuzak: `scene_apply` id ile mevcut GO'yu güncellerken `active` verilmezse true'ya döndü (AppleButton) → go_setActive ile geri alındı.
Ek: Agreement(27)'a RenderGroup; LoginFormIntro 31 kare, Agreement alpha 0→1 OutQuad f15–30 (Guest'ten 5 kare sonra başlar, en son biter).
Authored RenderGroup Alpha/scale değerleri 1 (edit modda görünür kalsın; kare 0 klipten gelir). Doğrulama: f12 agreement yok, f30 tam.
Ek: tüm sahne fade-in (Unity sceneOverlay karşılığı) — Background(2) köküne RenderGroup (authored 1), Logo MovieClip'e 30. track
`2:RenderGroup:0` Alpha 0→1 OutQuad f0–15. Kök RenderGroup tüm çocukları (logo/parçacık/form) kapsar; bilinçli. Doğrulama: sim f4 karanlık, f12 neredeyse tam.

## 2026-10-20 — Logo parçacıkları Unity prefab'ına birebir (KAYDEDİLMEDİ, undo'lu)
Prompt: "loginscene particle unity versiyona hiç benzemiyor, aynısını yapalım."
Bulgu (Unity `Login/Prefabs/LogoParticle.prefab`, UIParticle `Scale3D=180` → 1 birim = 180 px; 5 aktif emitter): önceki turlarda
kaçırılan asıl fark **alpha eğrisi tek fade değil, ömür boyu 3–4 kez yanıp sönen "twinkle"**; hız ≈ 0 (hareket yalnız noise), ömür
sabit 8 s (glitter 6), bant 180 px, renk sabit (random/gradient yok), maxParticles 50.
Yapı: LogoParticle(32) altındaki 5 emitter Unity adlarına çevrildi ve değerler birebir yazıldı:
big_particles(33) particle_2, 5/s, 9–180 px, #ffa100 **tepe α0.125**, kutu 720×180, noise 18@0.5, alpha 0,1,0,1,0,1,1,0 (t .13/.22/.30/.42/.51/.89), rotZ 0–360;
small_0(34) circle_2, 10/s, 9–36, #ffa100, 900×180, noise 36@2, alpha 0,1,0,1,0,1,0 (t .13/.44/.58/.74/.84);
glitter(35) circle_2, 10/s, 18–90, #ffb900, 1080×180, ömür 6, noise 18@0.5, alpha 0,1,1,0,1,0,1,0 (t .13/.54/.65/.75/.81/.89);
small_2(36) particle_1, 5/s, 9–36, #ffa100, 900×180, noise 36@2, small_0 eğrisi, **rotZ Random −180..360 °/s**;
small_1(37) circle_1, 10/s, 1.8–5.4 px, #ffca7b, 900×180, noise 36@2, small_0 eğrisi.
Ortak: lifetime Constant, speed Random 0–15 (Unity Rectangle +Z yönü 2D'de anlamsız → randomDirection 1), Additive, prewarm, maxParticles 50.
Invariantlar: parıltı = çok anahtarlı alpha twinkle (tek fade'e dönme); hareket = noise, hız ~0; ömür sabit; renkler Unity sabitleri;
tepe alpha `alpha.max`'ta, eğri normalize (Curve kuralı). Kaynak gerçek: Unity prefab; değişiklik gerekirse önce prefab değeriyle kıyasla.
Doğrulama: sim_run f150/f350 (932×430) + (140,30,660,190)×2 yakın plan — hale + bokeh daireler + ince noktalar Unity ekranıyla eş doku.
Noise turu (15:10): kullanıcı "noise bizde daha az hareket ettiriyor". f60/f120 kıyası: 18/36 ile konumlar neredeyse sabit → motor
`noise.strength` Unity gibi hız (birim/s) değil, genlik gibi davranıyor. ×4 uygulandı: big/glitter **72**, small_0/1/2 **144**
(frequency aynı 0.5 / 2). Doğrulama: f60/f120 — 1 s'de belirgin sürüklenme, bant hafif yayvan (Unity ile eş). Invariant: noise
Unity değeri ×4 (strength ≈ px genlik); tekrar ayarlanacaksa oran korunur (big/glitter : small = 1 : 2).
Noise turu 2 (15:13): "Unity'de daha çok yayılıyor" → ×6: big/glitter **108**, small_0/1/2 **216** (oran 1:2 korundu, frequency aynı).
Doğrulama: f200 — parçacıklar 180 px bandın dışına yayılıyor, dağılım Unity ekranıyla eş. Invariant güncellendi: noise = Unity ×6.
Yatay yayılım (15:15): "Unity yatayda daha çok açılıyor" → noise değil (izotropik, dikeyi de şişirir) kutu genişliği ×1.4:
big **1000**×180, small_0/1/2 **1300**×180, glitter **1500**×180. Doğrulama: f200 — bant ekranın ~%20–80'ine yayılıyor, dikey ince.
Invariant: yatay/dikey oran kutudan (genişlik Unity ×1.4, yükseklik 180), sürüklenme noise'dan.

## 2026-10-20 — Intro: parçacık scale-up yerine burst (KAYDEDİLMEDİ, undo'lu)
Prompt: "intro movieclip'te particle'lar scale-up ile gelmek yerine başta bir burst ile saçılıp sonra normal devam edebilir mi."
Yapı: Logo(3) MovieClip'te `32:Transform:0 Scale` track'i SİLİNDİ (LogoParticle authored scale 1); `32:RenderGroup:0 Alpha` track
Hold 0 → f8 0 → **f12 1 OutQuad** (kısa fade; sabit bant burst anında arkada belirir). Yeni emitter **burst(39)** (32 altı, listede son):
Circle r 150 / thickness 0.3 (logo dış hattından doğar), randomDirection 0, `bursts [t0 ×56]`, rate 0, looping/prewarm kapalı,
**startDelay 0.27 s** (= f8, Rummy çarpma anı), speed Random 600–1400, `velocity.drag 3`, lifetime 0.6–1.2, size 8–40, circle_2,
renk Random #ffb900/#ffa100, alpha Curve 1→0.8→0, Additive, maxParticles 60.
Not: Unity'de burst yok (orada uiParticleParent 0.2 s'de sadece açılır) — bilinçli sapma. İlk deneme (r 40, 36 adet, 400–1000)
logonun arkasında kaldığı için görünmedi; parçacıklar logonun ARKASINDA çizildiğinden burst logo dış hattından doğmalı.
Invariantlar: parçacık kökü scale'lenmez (burst saçılması koreografinin kendisi); burst zamanlaması startDelay = Logo klibi f8
(klip fps/kare değişirse startDelay güncellenir); sabit bant alpha ile girer; burst tek seferlik, loop yok.
Doğrulama: sim_run fps30 captureEvery 4 — f8 bant yok, f12 radyal saçılma + bant beliriyor, f16 yayılmış, f24 sönmüş, f28 sabit bant.
**GERİ ALINDI (15:26):** kullanıcı burst denemesini beğenmedi, undo ile tamamen kaldırıldı — burst(39) yok, Logo klibinde
`32:Transform:0 Scale 0→1 OutBack f8–19` + `32:RenderGroup:0 Alpha 0→1 f8–19` track'leri eski haliyle duruyor. Yukarıdaki burst
bölümü yalnız kayıt içindir; geçerli davranış: parçacık kökü scale-up + fade ile açılır (Unity ile aynı mantık, burst yok).
Üstteki parçacık turu (Unity birebir emitter'lar + noise ×6 + kutu ×1.4) KAYDEDİLDİ.
