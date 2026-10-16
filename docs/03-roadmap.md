# Yol haritası — DigitoyClient

Durum 2026-10-16, taslak. Fazlar sıralı; her fazın sonunda **kabul kriteri** gerçek sunucuya karşı doğrulanır
(sunucu simülasyonu yazılmaz). İlk oyun **Gin Rummy** (✅ kullanıcı). Süre tahmini verilmedi — her faz başında
game_rd/digitoyclient oturumunda ayrı plan + onay.

## Faz 0 — İskelet + dikey dilim (UI yok)
- `digitoyclient/` proje kökü: `Assets/{Shared,Games/GinRummy,Plugins}`, `ProjectSettings`, editörle açılış
  (csproj/mcp.json/gitignore üretimi), git init.
- `Digitoy.Common.dll` (generator'lı, netstandard2.1) `Assets/Plugins/` + sürüm notu; **AOT doğrulaması** (02 §A3).
- `Shared/Services`: `Server` (bağlantı durum makinesi, Codec çerçeve, heartbeat, reconnect), `Auth` (device login),
  `Session`, `PlayerService`, `LobbyService`, `TableService` — core.upm mantığı, Unity'siz.
- `Config`/`ServerDirectory` için JSON kararı (02 §B2) — motor JSON okuyucu ya da sunucuda Codec ucu.
- Varyant (02 §A1/A2) motor işi başlar; Faz 0 sonunda en az `GameVariant` asset + `Variant.Current`.
- game_rd `Projects/yuzbirextra` tohumları (fontlar, fx, Button/Motion) `Shared/`a taşınır; `Projects/ginrummy` ve
  `Projects/yuzbirextra` silinir (kullanıcı onayıyla).
- **Kabul:** editör Play'de konsol/`scene.dump` ile: gerçek dev sunucuya login → `TablePlayNow` → bir tur
  `PickFromDeck`/`Discard` → `TableState` decode edildi. Hiçbir UI yok.

## Faz 1 — Ortak kabuk (Shared)
- Entry (varyant yükler, splash) → Login (device + "sosyal" butonları stub) → Lobby (Play Now, masa listesi `ScrollView`,
  profil başlığı, bakiye, ayarlar).
- `DialogStack` + Dialog taban prefab'ı + MovieClip aç/kapa; ilk dialoglar: Prompt, Settings, Profile, Nick (TextField).
- Tema: `Shared/Theme/Default.theme` token'ları; Gin teması override örneği.
- Motor: TextField (02 §B1), safe-area (B3), küçük native yardımcılar (B4); C6 çevirici kararı; C7 locale içe aktarımı.
- **Kabul:** Gin varyantı Android cihazda açılır, login olur, lobide masa listesi görünür, Play Now masaya götürür
  (masa sahnesi boş). iOS/wasm build'i en az açılır.

## Faz 2 — Gin Rummy masası
- `GinTable` (TableState projeksiyonu; tek state machine), kart atlası, `Hand/DeckPile/DiscardPile/MeldZone/Seat/Timer`
  component'leri; deal/draw/discard/knock/gin/round-over koreografileri MovieClip; drag/drop + ipucu Common `HandScore`.
- Finish payload sözleşmesi (11 kartın **sıralı** listesi), Rush varyantı (yalnız sunucu zamanı), Slam (istemci sunumu).
- Round-over / game-over dialogları, rematch, chat/emoji (TextField), masa ayarları.
- **Kabul:** iki gerçek istemci (biri Unity prod) aynı masada tam oyun oynar; skorlar sunucuyla birebir;
  reconnect sonrası masa doğru geri yüklenir. Ekip görsel kabulü (screenshot turu + cihaz).

## Faz 3 — Yüzbir Okey
- `Shared/Board` (ya da `Games/OkeyCommon`): ıstaka, taş, sürükleme, gösterge, orta deste — Okey ile paylaşılacak
  şekilde; `YuzbirTable` + Common `YuzbirGame/OkeyCheck/Meld`; `yuzbirextra.unity` `Logic` asmdef'i (MeldStateSnapshot,
  UsefulMeldPlanner) **saf C# olduğu için doğrudan alınır**.
- Yüzbir varyantı, teması, lobi kartları.
- **Kabul:** Faz 2 ile aynı ölçüt (Unity yuzbir istemcisiyle karşılıklı oyun).

## Faz 4 — Okey (klasik)
- Sunucu tarafı: `OkeyTablePluginBase` üstüne klasik Okey plugin'i **yok** → digitoyserver'da ayrı iş
  (referans: `digiplay-net/OkeyExtra/table/OkeyCheck.cs`). Common'a `Okey` kural klasörü.
- İstemci: Faz 3 board'u + `OkeyTable`; farklar (gösterge/okey, çift açma, bitiş puanı) oyun klasöründe.
- **Kabul:** sunucu plugin'iyle uçtan uca oyun.

## Faz 5 — Mağaza sürümü (native köprüler + meta)
- Native plugin iskeleti → IAP → reklam → Facebook/Apple login → push → Play Integrity/DeviceKey → Adjust.
- Meta özellikler (öncelik ürün kararı): Store/Offers → günlük bonus/misyon → sosyal (arkadaş/hediye/inbox) →
  leaderboard/sezon → mini oyunlar (`Module.LoadAsync` ile ayrı pak — 🔶).
- **Kabul:** Gin varyantı store'a yüklenebilir build; prod sunucuya karşı satın alma + reklam ödülü çalışır.

## Faz 6+ — Yeni oyunlar
Yeni oyun = `Games/<Oyun>/` + `Variant.asset` + sunucu plugin'i; Shared'a dokunmadan çıkabilmesi hedef.

## Açık sorular (kullanıcı/ekip)
1. Hedef platform sırası: Android → iOS → wasm? (native köprü önceliği, Faz 1 kabulü)
2. `Config`/`ServerDirectory` JSON mu Codec mi (sunucu ekibi)?
3. Gin bot/offline oyun ilk sürümde var mı? (🔶 yok)
4. Faz 5 meta özelliklerinden ilk sürümde zorunlu olanlar (mağaza/reklam dahil mi)?
5. Unity prefab çevirici (02 §C6) yatırımı yapılacak mı?
6. Helpshift/Firebase (messaging dışı) kapsam dışı mı?
7. Yüzbir Unity istemcisi production'a çıkacak mı, yoksa doğrudan bu projeden mi çıkacak? (Faz 3 önceliğini etkiler)
