# DigitoyClient — çok oyunlu istemci (DigitoyEngine)

Gin Rummy Extra, Yüzbir Okey Extra ve Okey Extra'nın (ileride daha fazlası) **tek DigitoyEngine projesi** olarak
yeniden yazımı. Üç Unity istemcisinin yerini alır; sunucu değişmez (`digitoyserver`).

| | |
|---|---|
| Motor / editör | `c:\work\digitoygames\game_rd` (DigitoyEngine.dll, DigitoyEditor.exe, aotcompiler, player) |
| Sunucu + paylaşılan kurallar | `c:\work\digitoygames\digitoyserver` → `Common/` = `Digitoy.Common.dll` (`namespace M`) |
| Kaynak istemciler (okunur, port edilir) | `ginrummyextra.unity` (**production**), `yuzbirextra.unity`, `okeyextra.unity`, `core.upm` (Digitoy SDK) |
| Eski nesil (yalnız referans) | `digiplay-net` (Xamarin dönemi Digiplay motoru + eski istemciler; kod alınmaz) |

Ürün hedefi: **oyun başına ayrı store/FB uygulaması**, tek kaynak ağacından **build varyantı** ile üretilir; sahne
görünümleri, tema ve içerik oyuna göre tamamen değiştirilebilir. İlk hedef oyun **Gin Rummy** (prod).

## Dokümanlar
- [docs/00-inventory.md](docs/00-inventory.md) — kaynak projelerin envanteri (2026-10-16 keşfi)
- [docs/01-architecture.md](docs/01-architecture.md) — hedef mimari, klasör sözleşmesi, varyant/tema modeli
- [docs/02-engine-gaps.md](docs/02-engine-gaps.md) — motor tarafında gereken işler ve öncelik
- [docs/03-roadmap.md](docs/03-roadmap.md) — fazlar, kabul kriterleri, açık sorular
- [docs/04-common-surface.md](docs/04-common-surface.md) — Common.dll'den çıkarılan istemci yüzeyi (uçlar, modeller, auth)
- [docs/decisions.md](docs/decisions.md) — karar günlüğü (en yeni üstte)
- [docs/legacy-analysis/](docs/legacy-analysis/) — Gin Rummy için 2026-10-14 ayrıntılı analizler (Unity gameplay,
  core.upm, sunucu, UI envanteri, port değerlendirmesi); `game_rd/Projects/ginrummy/.ai` kopyası
- [.github/copilot-instructions.md](.github/copilot-instructions.md) — AI çalışma kuralları

## Açma
DigitoyEditor ile bu kökü aç; editör `.vscode/mcp.json`, `digitoyclient.Game.csproj` ve `.gitignore`'u kendisi üretir.
Başlangıç sahnesi `Assets/Shared/Scenes/Entry.scene`. `Assets/Plugins/Digitoy.Common.dll` hazır alınır
(sürüm notu yanında). Faz 0 durumu: [docs/03-roadmap.md](docs/03-roadmap.md).
