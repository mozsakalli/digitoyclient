# digitoyclient — AI çalışma kuralları (DigitoyEngine projesi)

Bu klasör bir DigitoyEditor oyun projesidir. Editör açıkken MCP sunucusu `digitoy-editor` (.vscode/mcp.json) üzerinden
sahne/kod/sim araçları kullanılır; `rpc_describe` tüm araçların şemasını verir.

## Döngü
- `view_device(name)` → `sim_run(frames, screenshot=true)` → görüntüyü incele → `input_tap/input_drag` → `render_screenshot`.
- Yeni component tipi kullanmadan ÖNCE `comp_schema`. Kod: `asset_write` + `code_build` (hatalar satır/kolonla döner).
- MOTOR API'Sİ: motor kaynağı bu projede YOKTUR ve proje dışına çıkma. `engine_api` kullan: `engine_api()` tip listesi
  (filter ile daralt), `engine_api(type="Component")` üyeler + override kancaları, `engine_api(type="Transform")`
  tween extension'ları dahil. Sık kullanılanlar: Component (Awake/Start/Update + OnPointerClick/Down/Up/DragStart/Drag(PointerEvent e),
  GetComponent<T>, gameObject, transform, enabled), PointerEvent (target, screenPos, screenTotal, dragged, Use()),
  LayoutBox (kutu/metin/stil; Text/Font/TextSize/Fill/... property'leri), Tween extension'ları (tr.TweenMove/Scale/Rotation,
  c.Tween("path", to, dur).Ease(Ease.OutBack)), Easing, MovieClip, Scene.Pointer. Lifecycle override'ları `protected override`.
- ZAMANLI GÖRSEL DEĞİŞİM (basınca küçül, fade, kay, pop): kendi `Update` sayacı/ease döngüsü YAZMA; `Tween*` kullan
  (sıfır alloc, destroy-güvenli, yarıda kesilince mevcut değerden devam). `TweenHandle` alanda tut, yeni tween'den önce
  `.Cancel()`, `OnDisable`'da iptal + taban değeri geri yaz. Elle döngü yalnız tek ease'e sığmayan parametrik eğriler
  (sönümlü shake vb.) için. Aynı kanala (örn. localScale) iki component yazmasın; feedback'i tek sahipte topla.
- KOORDİNAT: input ve screenshot (scale=1) MANTIKSAL nokta, sol-üst orijin, y aşağı; screenshot pikseli == tıklama koordinatı.
  Yön `ProjectSettings/PlayerSettings.asset` orientation'dan; sim ona sadık.

## Konvansiyonlar
- Sahne = kaynak (YAML); runtime durumu sahneye sızmaz, oyun durumu düz C# serviste yaşar.
- SAHNE/PREFAB YAML'INI ASLA DOĞRUDAN DÜZENLEME (asset_write dahil). Sahne değişikliği yalnız editör araçlarıyla:
  `prop_set` (value skaler olabilir: 72, "OutBack", "0 10 0", "#ff0000ff"), `transform_set`, `comp_add/remove`,
  `scene_addGo/removeGo/reparent`. Böylece undo'lu kalır, canlı sahneye anında yansır, kaydetme kararı kullanıcıda olur.
  Bir araç çalışmazsa dosyayı düzenleyerek atlatma; sorunu bildir.
- ÇİZİM SIRASI (2D): önce Renderer.SortingOrder (büyük üstte), eşitse hiyerarşi/liste sırası (sonraki üstte). "X'i Y'nin
  arkasına al" = `scene_reparent(id, parent, insertBefore=Y)` ile Y'nin önüne taşı (aynı parent'ta yalnız sıralama)
  ya da SortingOrder düşür. Pozisyon değiştirmez. "Altına/üstüne" belirsizse (ekranda mi, katmanda mı) kullanıcıya sor.
  Yapısal araçları (`scene_addGo/reparent/removeGo`) PARALEL çağırma: varış sırası belirsizdir, kardeş sırası karışır.
  Sıra önemliyse ardışık çağır ya da `scene_addGo(..., insertBefore=Y)` ile yeri açıkça ver.
- PLAY TAZELİĞİ: play sırasında yapılan doc değişiklikleri oyun sahnesine OTOMATİK yansımaz; `sim_run` bayat sahneyi
  algılayıp frame 0'dan yeniden başlatır (yanıtta `restarted`), `input_*` de öyle. Zamana bağlı şeyleri (MovieClip, Motion)
  kıyaslarken aynı frame sayısında screenshot al; `render_screenshot` yanıtında `stale: true` görürsen `sim_run` çağır.
- Her sahne işinin sonunda `<sahne>.intent.md` (sahnenin yanında, APPEND-ONLY): prompt + yapı + INVARIANTLAR + doğrulama.
  Sahneye dokunmadan ÖNCE oku. Sahne KAYDETME kararı kullanıcınındır (undo'lu bırak, söyle).
- Görsel doğrulamayı screenshot ile kendin yap; nihai his kullanıcıda. Tasarım iletişimi isimli sözlükle
  (motion preset, token) — ease/piksel konuşma.
- Kod yorumları Türkçe, UI metinleri oyunun diline göre. Scriptler `Assets/**/*.cs` (Editor/ hariç), lifecycle override'ları `protected override`.


# DigitoyClient — AI çalışma kuralları (DigitoyEngine projesi)

Çok oyunlu DigitoyEngine istemcisi (Gin Rummy, Yüzbir, Okey, …). Mimari ve kararlar: [docs/01-architecture.md](../docs/01-architecture.md),
yol haritası [docs/03-roadmap.md](../docs/03-roadmap.md), motor eksikleri [docs/02-engine-gaps.md](../docs/02-engine-gaps.md),
kaynak envanteri [docs/00-inventory.md](../docs/00-inventory.md). Yeni işe başlamadan ilgili bölümü oku; oturum sonunda
yeni kararları `docs/decisions.md`'ye ekle (kronolojik, en yeni üstte).

## ÇALIŞMA DİSİPLİNİ (KESİN KURAL)
- **Kod yazmadan ÖNCE plan sun ve onay bekle.** Ne değişecek, hangi dosyalar, ne silinecek, risk ne — kısa madde listesi;
  kullanıcı "başla/onay" demeden tek satır kod yazma. Planın dışına çıkan karar gerekirse dur, tekrar sor.
- Türkçe, kısa. Mevcut davranış ile öneri açıkça ayrılsın; onaylanmamış plan uygulanmış gibi anlatılmasın.

## Sınırlar
- **Motor kaynağı bu projede yok**, proje dışına çıkma. Motor API'si için `engine_api()` (tip listesi) /
  `engine_api(type="X")` (üyeler + override kancaları + tween extension'ları). Motor eksiği bulursan atlatma yazma;
  `docs/02-engine-gaps.md`'ye madde ekle ve bildir (motor işi game_rd oturumunda yapılır).
- **Kurallar `Digitoy.Common` (`M.*`)**: istemcide kural kopyası yazılmaz (ipucu/bot/UI uygunluğu dahil). Common'da eksik
  varsa digitoyserver tarafına iş olarak not et.
- Unity kaynak projeleri (`ginrummyextra.unity`, `yuzbirextra.unity`, `okeyextra.unity`, `core.upm`) **yalnız okunur**
  — "ne yapıyor"u öğrenmek için; MonoBehaviour/DOTween/callback dokusu çevrilmez, yeniden yazılır.
- `Assets/Shared/` oyun-bağımsızdır: oyun adı/`if (game == …)` geçmez; oyun farkı varyant referansı ya da
  `Assets/Games/<Oyun>/` içinde ayrı sahne/prefab ile çözülür. Oyunlar birbirine bağımlı olamaz.
- Hot path sıfır alloc; statik tip cache yasak (ALC reload); motor tek-thread (ContinueWith/Task.Run yok,
  `await` + frame pump).

## Döngü (editör açıkken MCP `digitoy-editor`)
- `view_device(name)` → `sim_run(frames, screenshot=true)` → görüntüyü incele → `input_tap/input_drag` → `render_screenshot`.
- Yeni component tipi kullanmadan ÖNCE `comp_schema`. Kod: `asset_write` + `code_build` (hatalar satır/kolonla döner).
  Asset bilgisi için `asset_list/asset_info` — boyut öğrenmek için kod yazıp çalıştırma.
- KOORDİNAT: input ve screenshot (scale=1) MANTIKSAL nokta, sol-üst orijin, y aşağı; screenshot pikseli == tıklama
  koordinatı. Yön aktif varyant/PlayerSettings orientation'dan.

## Konvansiyonlar
- Sahne = kaynak (YAML); runtime durumu sahneye sızmaz, oyun durumu düz C# serviste yaşar (`Shared/Services`, `GameTable`).
- SAHNE/PREFAB YAML'INI ASLA DOĞRUDAN DÜZENLEME (asset_write dahil). TOPLU iş `scene_apply(objects)` / `edit_batch(ops)`
  (tek undo, atomik); küçük rötuş `prop_set` (skaler: 72, "OutBack", "0 10 0", "#ff0000ff"), `transform_set`,
  `comp_add/remove`, `scene_addGo/removeGo/reparent`. Araç çalışmazsa dosyayı düzenleyerek atlatma; bildir.
- ANİMASYON SINIRI: doc'ta yazılabilen koreografi (intro, deal, meld, round-over, giriş/çıkış, idle) = **MovieClip**
  (scrub/undo'lu, frame 0 authored); Tween yalnız runtime'da hesaplanan tepkisel hareket; Tween'in Start'ta authored
  değeri ezmesi yasak.
- ÇİZİM SIRASI (2D): SortingOrder (büyük üstte), eşitse liste sırası (sonraki üstte); sıralama `scene_reparent(..., insertBefore)`.
- PLAY TAZELİĞİ: play sırasında doc değişirse oyun sahnesi bayat; `sim_run`/`input_*` yeniden başlatır (`restarted`),
  `render_screenshot` `stale: true` dönerse `sim_run` çağır. Zamana bağlı şeyleri aynı frame sayısında kıyasla.
- Her sahne işinin sonunda `<sahne>.intent.md` (sahnenin yanında, APPEND-ONLY): prompt + yapı + INVARIANTLAR + doğrulama.
  Sahneye dokunmadan ÖNCE oku. Sahne KAYDETME kararı kullanıcınındır (undo'lu bırak, söyle).
- Tema: kabuk prefab'ları renk/kenar/font'u tema token'ı ile alır, sabit değer yazmaz.
- Görsel doğrulamayı screenshot ile kendin yap; nihai his kullanıcıda. Kullanıcı "görsel bakacağım" dediyse screenshot
  turu yapma. Tasarım iletişimi isimli sözlükle (motion preset, token) — ease/piksel konuşma.
- Kod yorumları Türkçe, UI metinleri localization tablosunda (`@loc`). Scriptler `Assets/**/*.cs` (Editor/ hariç),
  lifecycle override'ları `protected override`.