# Gin Rummy Extra — Gameplay/Table mimarisi incelemesi

İnceleme kapsamı:

- `C:\work\digitoygames\ginrummyextra.unity\ARCHITECTURE_PLAN.md`
- `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Core\**`
- `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Table\Scripts\**`

---

## 1. Mimari harita

### Genel katmanlar

```text
Lobby / UI
   |
   v
PlayService
   |
   +--> online: Server / GinRummy.Table / M.GinRummy modelleri
   |
   +--> bot: BotGameManager
                |
                v
          BotGameState
          BotGameDeckState
          BotGameMeldTracker
          IBotStrategy
                |
                v
          TableContext
                |
        +-------+-------------------+
        |                           |
   FlowController              TableUIManager
   phases                      SeatManager / Seat
        |                           |
        +--> CardManager            |
             Hands / Piles / Cards  |
                                    v
                                  Unity view
```

### `TableContext`

`TableContext`, masa sahnesindeki neredeyse tüm runtime bağımlılıklarını tutan Unity `MonoBehaviour` konteyneridir:

- `deckPile`, `discardPile`
- `playerHand`, `opponentHand`
- `cardManager`
- `seatManager`
- `uiManager`
- `gameTable`
- butonlar, timer’lar, deadwood UI’si, jackpot, canvas vb.

Kaynak:  
`Assets/GinRummyExtra/Table/Scripts/Utilities/TableContext.cs:31-117`

Bu sınıf saf oyun state’i değildir; oyun state’i, UI state’i ve scene references aynı nesnede karışmaktadır. `UIState` enum’u da aynı dosyada yer alır:

`TableContext.cs:12-28`

Önemli sonuç: `TableContext` şu anda hem dependency container, hem görünüm modeli, hem de bazı oyun state’lerinin sahibi durumundadır.

### `GinGame`

`GinGame`, masa singleton/global erişim noktasıdır. Kodun birçok yerinde:

```csharp
GinGame.Context
GinGame.Manager
GinGame.Rules
```

kullanılıyor.

Örnekler:

- `CardExtensions.cs:10`
- `GameRules.cs:9`
- `BotGameManager.cs:106-125`

Bu global erişim, online ve bot taraflarının aynı UI altyapısını kullanmasını sağlıyor; ancak test edilebilirliği düşürüyor.

### `TableManager`

Online oyunun ana masa yöneticisidir:

- server’dan gelen masa/game state’ini alır,
- `FlowController` geçişlerini tetikler,
- oyuncu aksiyonlarını server’a gönderir,
- kartları `Hand`/`Pile` nesnelerine yansıtır,
- `SeatManager` ve UI’yı günceller.

Dosya boyutu:

- `TableManager.cs`: **725 satır**

`TableManager`, server event handling, oyun komutları, lifecycle, kart animasyonları ve UI koordinasyonunu aynı sınıfta topluyor.

### `BotGameManager`

Bot/offline yolunun ana yöneticisidir.

- `MonoBehaviour`, `ITableManager`
- `TableContext` referansı alır.
- `GinGame.Initialize(context, new GameRules())` çağırır.
- Kendi `FlowController` örneğini oluşturur.
- `BotGameState`, `BotGameDeckState`, `BotGameMeldTracker` oluşturur.
- Bot stratejisini (`IBotStrategy`) çalıştırır.
- Server state’i olmadan kart/turn/score/timer state’ini kendisi yönetir.

Kaynaklar:

- `BotGameManager.cs:11-33`
- `BotGameManager.cs:67-106`
- `BotGameManager.cs:111-140`

Dosya boyutu:

- `BotGameManager.cs`: **1.351 satır**

Bu, bot yolunun basit bir adapter değil, ikinci bir oyun motoru olduğunu gösteriyor.

### `FlowController` ve fazlar

`FlowController` UI/animasyon odaklı faz state machine’idir. Fazlar:

- `DealingPhase`
- `SlamPhase`
- `MeldingPhase`
- `GameOverPhase`
- `RoundOverPhase`
- `ExitPhase`
- `RushTimeoutPhase`

Kaynak:  
`FlowController.cs:11-25`

Her faz `IFlowPhase` üzerinden tamamlanma event’i yayınlar. `FlowController`, faz değişince `ClearAll()` çağırır:

`FlowController.cs:27-44`

Ana UI fazları `TableContext.UIState` üzerinden ayrıca tutulur:

- `Entering`
- `WaitingPlayers`
- `GameStarting`
- `Dealing`
- `Playing`
- `Finishing`
- `SlamCard`
- `Melding`
- `RoundOver`
- `GameOver`
- `Exiting`

Sonuçta iki state machine vardır:

1. `FlowController._currentPhase`
2. `TableContext.currentUIState`

Bunların senkronizasyonu manuel yapılıyor ve yarış/race riski taşıyor.

### `Seat`

`Seat`, oyuncu koltuğunun view/controller sınıfıdır.

Sorumlulukları:

- oyuncu adı/avatar/level gösterimi,
- score paneli,
- ready state,
- timer,
- emoji/gift/sticker/chat,
- çeşitli DOTween animasyonları.

Dosya boyutu:

- `Seat.cs`: **565 satır**

Gameplay state’i doğrudan `Seat` içinde tutulmamalı; mevcut kodda view ve event response aynı sınıfta karışıyor.

### `Hand`, `PlayerHand`, `OpponentHand`

- `Hand`: kartların fiziksel/Unity koleksiyonunu yönetir.
- `PlayerHand`: oyuncu kartları, sorting, deadwood UI ve drag/drop akışına daha yakındır.
- `OpponentHand`: rakibin kart sayısı/görünümü.

Dosya boyutu:

- `PlayerHand.cs`: **559 satır**

`PlayerHand`, saf bir kart koleksiyonu olmaktan çok UI/controller davranışı içeriyor.

### Pile sınıfları

- `CardPile`
- `DeckPile`
- `DiscardPile`
- `MeldPile`

`DeckPile`, kullanıcı input’una ve `GinGame.Rules` kontrolüne doğrudan bağlıdır:

- `DeckPile.cs:160-204`
- `GinGame.Rules.CanAddHand()`
- `GinGame.Rules.CanPickFromDeck()`

Bu nedenle pile sınıfları yalnızca state taşıyıcısı değil, input ve gameplay koordinatörü de oluyor.

### `Card`, `CardExtensions`, `CardUtils`

`Card` ve türevleri Unity view nesneleridir.

`CardExtensions` adı misleading: kart hareketi, deal, discard, slam ve melding animasyonlarını içerir:

- `CardExtensions.cs:16-86` — hand/deal animasyonları
- `CardExtensions.cs:202-237` — discard
- `CardExtensions.cs:542-620` — player/opponent slam
- `CardExtensions.cs:700-729` — melding pile

Ayrıca `CardExtensions` doğrudan global context kullanır:

`CardExtensions.cs:10`

`CardUtils` ise kart numarası, isim, suit/rank ve kart listesi yardımcılarını içerir:

- `CardUtils.cs`: **264 satır**

### `GameRules`

`GameRules`, UI için action/button uygunluğunu ve bazı round sonu kontrollerini yapar:

- `Can...` metotları
- rush timeout discard
- knock/gin button state
- warning gösterimi

Örnek:

`Actions/GameRules.cs:45-77`

```csharp
if (finalDeadwoodScore == 0) return GameButtonState.Gin;
if (finalDeadwoodScore <= finishScore) return GameButtonState.Knock;
```

Bu sınıfın adına rağmen tam bir domain rules motoru değildir; `TableContext`, `PlayerHand` ve UI state’ine bağlıdır.

---

## 2. Server → state → view veri akışı

### Online akış

Yaklaşık akış:

```text
PlayService.Now/Create/Join
        |
        v
Server / Table service
        |
        v
TableManager event/callback'leri
        |
        +--> Table / player / hand / pile state
        |
        +--> FlowController phase
        |
        +--> CardManager / Hand / Pile
        |
        +--> SeatManager / TableUIManager
        |
        v
Unity card, seat, timer, button ve animation view'ları
```

`PlayService`, oyun kurallarını yürütmez. Lobby seçimi, bot eligibility, bet arama/oluşturma ve server play/join işlemleriyle ilgilenir.

Önemli metotlar:

- `PlayService.Now(...)`: `PlayService.cs:49`
- `PlayService.NowAsync(...)`: `PlayService.cs:65`
- bot feature kontrolü: `PlayService.cs:82-92`
- server play attempt: `PlayService.cs:360-367`
- `Create(...)`: `PlayService.cs:248`
- `Join(...)`: `PlayService.cs:313`
- bet hesaplama: `PlayService.cs:381-575`

Online tarafta authoritative state büyük ölçüde server’dan gelen `M.GinRummy`/`Table` modellerindedir. Client, bunu `TableContext` ve Unity nesnelerine materialize eder.

### Bot/offline akış

```text
BotGameManager.OnEnable
        |
        +--> GinGame.Initialize(context, GameRules)
        +--> new FlowController()
        +--> new BotGameState()
        +--> new BotGameDeckState()
        +--> new BotGameMeldTracker()
        |
        v
BotGameManager game loop
        |
        +--> Bot strategy
        +--> _playerCards
        +--> _deckState
        +--> _playerScore / _botScore
        +--> _finisherIndex / _finishIsGin
        +--> timers
        |
        v
FlowController
        |
        v
TableContext / CardManager / Hands / Piles / UI
```

Bot state’in ana parçaları:

- `BotGameState.cs:7-30`
- `BotGameDeckState.cs`: **60 satır**
- `BotGameMeldTracker.cs`: **100 satır**
- `BotGameManager.cs:67-105`

Bot kartı gerektiğinde callback ile hesaplanıyor:

`BotGameManager.cs:119-139`

```csharp
_tutorialState.ComputeDeckCardNo = () => { ... };
```

Bu, fiziksel deck state’inin yanında “on-demand hesaplanan sahte deck top card” modeli oluşturuyor.

---

## 3. Client-side Gin Rummy kuralları

## 3.1 Meld detection

Meld davranışı iki ayrı tarafta bulunuyor:

### Client card/UI yolu

`CardExtensions` melding görsel akışını yapıyor:

- `ToMeldingPile(...)`: `CardExtensions.cs:700`
- `PlayerSlamCard(...)`: `CardExtensions.cs:542`
- `OpponentSlamCard(...)`: `CardExtensions.cs:620`

Bu dosyada görünür bir saf meld solver yok; daha çok kartların meld pile’a taşınması ve animasyonu var.

### Bot yolu

Bot tarafında ayrı meld takip mekanizması var:

- `BotGameMeldTracker.cs`: **100 satır**
- `BotGameManager.cs` içinde meld/finish kararları ve kart listesi kontrolleri
- bot strategy dosyaları:
  - `HelpfulBotStrategy.cs`
  - `RealisticBotStrategy.cs`
  - `AdaptiveRealisticBotStrategy.cs`
  - `DummyBotStrategy.cs`

Buradaki meld hesapları UI kartlarından değil, botun kendi kart listeleri ve `BotGameDeckState` üzerinden yürütülüyor.

### Hint yolu

`TableHintController.cs`: **584 satır**

Meld, sorting, knock, gin ve discard hint’lerini yönetiyor. Hint sistemi de gameplay rules’ların bir kısmını tekrar yorumluyor:

- `TableHintController.cs:538-584`

---

## 3.2 Deadwood

Deadwood UI/action kararı:

`Actions/GameRules.cs:45-77`

Kullanılan alanlar:

- `LastHandScore`
- `DeadwoodScore`
- `FinalDeadwood`
- `CanGin`
- `CanKnock`

Kaynak:

`GameRules.cs:59-67`

Bu, online path’te `PlayerHand.LastHandScore` tarafından sağlanan server/client hesaplanmış skor modeline dayanıyor.

Bot path’te ise `BotGameManager` kendi skor/finish state’ini yönetiyor. Dolayısıyla deadwood’un tek bir ortak domain implementasyonu görünmüyor.

---

## 3.3 Knock / Gin / Undercut

Tanımlı analytics/event anahtarları:

`Core/Definitions.cs:45-52`

- `GinCount`
- `KnockCount`
- `UndercutCount`

Bot event’leri:

`Definitions.cs:90-106`

- `BotGameGin`
- `BotGameKnock`
- `BotGameUndercut`
- `BotGameWin`
- `BotGameLoss`
- `BotGameOutOfCards`
- `BotGameTimeOut`
- `BotGameNewRound`

UI button kararı:

`GameRules.cs:64-67`

- deadwood `0` ise Gin
- deadwood knock threshold altındaysa Knock

Threshold:

- `Constants.KNOCK_THRESHOLD`
- `GameRules.cs:64`

Bot tarafında eşdeğer kararlar `BotGameManager` içinde dağınık durumda; dosyanın **1.351 satır** olması bu ayrışmanın ana göstergesi.

---

## 3.4 Big Gin

Kod aramasında Big Gin için açık ve merkezi bir domain metodu görülmüyor. Gin/Straight flag’leri bulunuyor:

- `GameRules.cs:64`
- `PlayService.cs:65-78` — `straight`, `joker`, `rush`, `noShuffle` query parametreleri
- `GameFeatureRules.cs:169-193` — gin feature kuralları

Ancak Big Gin’in:

- tek bir `IsBigGin(...)`,
- merkezi score calculator’ı,
- ortak online/bot implementation’ı

görünmüyor.

Muhtemel scoring/finish kararı server’dan gelen `LastHandScore` veya bot-side özel akışlarda dağılmış durumda.

---

## 3.5 Layoff

Layoff için ortak, açık bir saf domain metodu görünmüyor. Melding görseli vardır:

- `CardExtensions.cs:700-729`
- `MeldPile.cs`

Ancak rakibin meld’lerine layoff yapılabilirliğini hesaplayan bağımsız bir `CanLayoff`/`ApplyLayoff` implementation’ı tespit edilmedi.

Bu, online server’ın authoritative yaptığı veya `PlayerHand`/`TableManager` içine gömülü olduğu anlamına gelebilir; fakat client-side ortak kurala dönüştürülmemiştir.

---

## 3.6 Scoring

Online action UI’si aşağıdaki score alanlarına güvenir:

`GameRules.cs:59-67`

- `LastHandScore`
- `DeadwoodScore`
- `FinalDeadwood`
- `CanGin`
- `CanKnock`

Bot tarafında ayrı score state’i bulunur:

`BotGameManager.cs:93-105`

```csharp
private int _playerScore;
private int _botScore;
```

Bot game ayrıca:

- `_finisherIndex`
- `_finishIsGin`
- `tableScore`
- `jackpotAmount`

tutar.

`BotGameManager.cs:23-25`, `BotGameManager.cs:74-95`

Bu iki yol ortak `Scoring` domain servisi kullanmıyor.

---

## 3.7 Rush timeout

Rush timeout ayrı flow phase olarak modellenmiş:

- `RushTimeoutPhase.cs`
- `FlowController.cs:21`
- `FlowController.cs:355-368`

`GameRules` ayrıca timeout discard yapıyor:

`GameRules.cs:45-55`

```csharp
public int DiscardOnRushTimeout()
```

UI state geçişi:

`FlowController.cs:355-357`

```csharp
_ctx.currentUIState = UIState.SlamCard;
_ctx.uiManager.OnRushTimeout();
```

Bu örnekte timeout event’i doğrudan UI fazına bağlanıyor; domain’de “timeout olunca hangi action legal?” kararı ile görsel reaksiyon ayrışmamış.

---

## 3.8 Slam

Slam, UI/animation ağırlıklı ayrı bir faz:

- `SlamPhase.cs`
- `CardExtensions.PlayerSlamCard(...)`
- `CardExtensions.OpponentSlamCard(...)`

Kaynaklar:

- `CardExtensions.cs:542-620`
- `FlowController.cs:16-21`
- `DeckPile.cs:207-214`

Slam’ın gerçek gameplay sonucunun server state’iyle nasıl doğrulandığı online ve bot tarafında farklıdır.

---

## 3.9 Extra varyantları

Varyant flag’leri `PlayService` query’sinde mevcut:

`PlayService.cs:65-78`

- `straight`
- `joker`
- `rush`
- `noShuffle`
- `score`
- `lobbyId`

Feature/AB kuralları:

`GameFeatureRules.cs:146-345`

Özellikle feature ID’leri:

- `PlayWithBot`
- `HelpfulBot`
- `RealisticBot`
- `AdaptiveRealisticBot`
- `DummyBot`
- `KnockHint`
- `GinHint`
- `Rush` ile ilişkili lobby/query flag’leri

Ancak “Extra” varyant kurallarının tek bir `GameRuleset` veya `VariantRules` abstraction’ında toplandığı görülmüyor.

---

## 4. Kod kalitesi bulguları

## 4.1 God class’lar

En belirgin dosyalar:

| Dosya | Satır |
|---|---:|
| `BotGameManager.cs` | 1.351 |
| `TableManager.cs` | 725 |
| `CardExtensions.cs` | 729 |
| `TableUIManager.cs` | 661 |
| `TableHintController.cs` | 584 |
| `PlayService.cs` | 581 |
| `PlayerHand.cs` | 559 |
| `Seat.cs` | 565 |
| `TableContext.cs` | 475 |
| `FlowController.cs` | 390 |
| `GameFeatureRules.cs` | 345 |
| `PuppetAutopilot.cs` | 261 |
| `BotProfileService.cs` | 235 |
| `GameManager.cs` | 235 |
| `Definitions.cs` | 244 |

`BotGameManager` tek başına:

- initialization,
- ready flow,
- turn flow,
- bot AI,
- deck state,
- scoring,
- timeout,
- connection lifecycle,
- app pause/resume,
- UI,
- debug,
- forced test actions

sorumluluklarını taşıyor.

Örnek başlangıç alanları:

`BotGameManager.cs:67-105`

---

## 4.2 View ve gameplay logic karışması

### `TableContext`

Unity `MonoBehaviour`, gameplay model ve UI references aynı sınıfta:

`TableContext.cs:31-117`

### `CardExtensions`

Kart kurallarıyla doğrudan ilişkili görünen isimlere rağmen esas olarak DOTween/view animasyonları yapıyor:

- `CardExtensions.cs:16-86`
- `CardExtensions.cs:202-237`
- `CardExtensions.cs:542-729`

Ayrıca global `TableContext` erişimi var:

`CardExtensions.cs:10`

### `DeckPile`

UI input handler ile gameplay rule validation aynı sınıfta:

`DeckPile.cs:160-204`

### `CardDefault`

Drag/drop sırasında rule çağrıları yapıyor:

`CardDefault.cs:152-192`

```csharp
GinGame.Rules.CanAddHand(this)
GinGame.Rules.CanRelease(this)
GinGame.Rules.NotifyCardReleased(this)
```

### `Seat`

Sosyal özellikler, oyuncu state’i, timer ve animasyonlar tek MonoBehaviour’da:

`Seat.cs:148-552`

---

## 4.3 Static/global/singleton state

Örnekler:

- `GinGame.Context`
- `GinGame.Manager`
- `GinGame.Rules`
- global using’ler

`Core/GlobalUsings.cs:1-4`

```csharp
global using static GinRummyExtra.GameManager;
global using Game = GinRummyExtra.GameManager;
```

`GameManager` yaşam döngüsü:

- `[RuntimeInitializeOnLoadMethod]`
- `DontDestroyOnLoad`

`GameManager.cs:56-84`

Bu yapı scene bağımlı gameplay kodunu global singleton’a bağlamaktadır.

`GameFeatureRules` da static mutable override dictionary kullanır:

`GameFeatureRules.cs:146-155`

```csharp
private static readonly Dictionary<string, bool> _overrides = new();
```

---

## 4.4 Magic numbers

Örnekler:

`BotGameManager.cs:19-32`

```csharp
lobbyId = 1
tableBet = 1000
tableScore = 10
jackpotAmount = 200
readyRemainingTime = 10f
turnRemainingTime = 25f
extraTurnTime = 25f
gameOverTime = 22f
roundOverTime = 18f
```

`BotGameManager.cs:103`

```csharp
private static readonly float DeckEmptyRestartTime = 3f;
```

`PlayService.cs:383-390`

```csharp
PrimaryMinRatio = 0.075m
PrimaryMaxRatio = 0.15m
...
CreateTargetRatio = 0.10m
```

Bu oranlar en azından isimlendirilmiş constant olarak ayrılmış; ancak lobby/variant kurallarıyla merkezi config ilişkisi yok.

---

## 4.5 Coroutine/async/DOTween state machine riski

Kod, mantıksal state geçişlerini hem async akışlarla hem de DOTween callback’leriyle yönetiyor.

Örnekler:

- `FlowController` içinde `_cameraTransitionTween`
- `CardExtensions` içinde çok sayıda `Sequence`
- `Seat` içinde çok sayıda tween
- `BotGameManager` içinde `Awaitable`, `CancellationTokenSource`, `.Forget()`
- `TableHintController` içinde gecikmeli `.Forget()`

Kaynaklar:

- `FlowController.cs:25`
- `Seat.cs:176-552`
- `BotGameManager.cs:1219-1261`
- `TableHintController.cs:216-219`
- `TableHintController.cs:467-475`

Riskler:

- phase değiştikten sonra eski tween callback’inin çalışması,
- scene disable/destroy sonrası async continuation,
- `.Forget()` çağrılarında exception kaybı,
- aynı action’ın server callback + animation callback tarafından iki kez tetiklenmesi,
- timer/phase ve UI state’in ayrışması.

`BotGameManager` içinde event abonelikleri de lifecycle’a bağlıdır:

`BotGameManager.cs:130-156`

---

## 4.6 Race-prone async

Özellikle:

`PlayService.cs:87-90`

```csharp
OnResult(...).Forget();
return result;
```

`OnResult` async olarak UI/dialog transition yaparken public metot hemen result döndürüyor.

`PlayService.cs:119-122`, `PlayService.cs:140-164`

PlayNow birden fazla lobby aralığını sırayla deneyip her başarısız/başarılı sonucu async UI’ya aktarabiliyor.

`BotGameManager.OnEnabledAsync()` de Unity lifecycle’dan bağımsız async olarak çalışıyor:

`BotGameManager.cs:147-180`

Bu akışta object disable/destroy, reconnect veya hot-switch yaşanırsa continuation’ların eski context’e yazma ihtimali var.

---

## 4.7 Duplicated code

En önemli tekrarlar:

1. Online rules/state:
   - server’dan gelen hand/score/finish bilgileri
   - `GameRules` ve `PlayerHand`

2. Bot rules/state:
   - `BotGameManager`
   - bot strategy’leri
   - `BotGameMeldTracker`

3. Meld/deadwood/finish kararları:
   - `PlayerHand`/`GameRules`
   - `BotGameManager`
   - `IBotStrategy` implementasyonları
   - `TableHintController`

4. Phase/UI transition:
   - `FlowController`
   - `TableManager`
   - `BotGameManager`

5. Card movement:
   - `CardExtensions`
   - `CardManager`
   - pile sınıfları
   - hand sınıfları

Bu yapı online ve bot’un aynı gameplay modelini paylaşmadığını gösteriyor.

---

## 4.8 Dead code / eski kod

`SeatTimerOld.cs` hâlâ projede:

- **119 satır**
- `SeatTimer.cs` ile paralel timer implementation’ı

`SeatTimerOld.cs:9`

```csharp
public class SeatTimerOld : MonoBehaviour
```

Kod içindeki açık TODO/HACK örneği:

`Definitions.cs:10`

```csharp
//todo: global using Digitoy and M namespaces
```

`Definitions.cs:37`

```csharp
// StarterQuest ... Core'a taşınmalı mı?
```

`Definitions.cs:98`

```csharp
public const string BotGameLeave = "bot_game_quit"; // TODO: eklenecek
```

`CardDragHelper.cs:24`

```csharp
// if (Time.unscaledTime - _lastDragEndTime < Constants.DRAG_COOLDOWN)
```

`GameFeatureRules.cs:5`

```csharp
// using GinRummyExtra.Core;
```

---

## 5. Online vs offline: tek model mi, iki model mi?

Sonuç: **Pratikte iki ayrı oyun-state modeli var.**

### Online

- Authoritative state server/`M.GinRummy` modellerinden gelir.
- `TableManager` server event’lerini işler.
- Hand/score/turn/finish state’i server mesajlarıyla güncellenir.
- Client çoğunlukla projection/view görevi görür.
- `PlayService` server’a play/create/join istekleri gönderir.

### Offline/bot

- `BotGameManager` state’in sahibidir.
- `_playerCards`
- `_deckState`
- `_playerScore`
- `_botScore`
- `_finisherIndex`
- `_finishIsGin`
- `_passedCount`
- timer state’leri

Bot yolu server event’lerini taklit eden bir domain command stream kullanmıyor; doğrudan aynı Unity nesnelerine ve `FlowController`’a yazıyor.

Ortak kalanlar:

- `TableContext`
- `GinGame`
- `GameRules`
- `FlowController`
- `Hand`/`Pile`/`Card`
- UI manager’ları

Ortak kalmayan gameplay state:

- online authoritative table modeli,
- offline `BotGameState` + private fields,
- online scoring/finish validation,
- bot scoring/meld/AI kararları.

Dolayısıyla mevcut yapı “tek state modeline sahip client” değil; **aynı view altyapısını kullanan iki farklı oyun motoru** görünümündedir.

---

## 6. Masa tarafından tüketilen message/event türleri

`Definitions.cs` içinde açık event adları:

### Genel/server/lobby

- `TableJoin`
- `TableLeave`
- `CreatePrivateTableEvent`
- `PrivateMessageSend`
- `TableChatSend`
- `TableEmojiSend`
- `TableStickerSend`

`Definitions.cs:75-106`

### Bot game

- `BotGameStart`
- `BotGameWin`
- `BotGameGin`
- `BotGameKnock`
- `BotGameLoss`
- `BotGameLeave`
- `BotGameBackground`
- `BotGameForeground`
- `BotGameOutOfCards`
- `BotGameTimeOut`
- `BotGameUndercut`
- `BotGameJackpot`
- `BotGameNewRound`

`Definitions.cs:90-106`

### Connection/application event’leri

Kodda kullanılan event örnekleri:

- `Server.OnDisconnected`
- `Server.OnReconnecting`
- `Server.OnConnected`
- `GameManager.OnAppPaused`
- `GameManager.OnAppResumed`
- `Player.OnUpdate`

Kaynaklar:

- `GameManager.cs:41-42`
- `GameManager.cs:106-119`
- `BotGameManager.cs:137-156`
- `BotGameManager.cs:1226-1261`

### `PlayService` ne yapıyor?

`PlayService`, masa içi turn/meld/scoring motoru değildir. Şunları yapar:

1. Play Now giriş noktası.
2. Bot feature eligibility kontrolü.
3. Coin/bet validation.
4. Uygun lobby’leri seçme.
5. Bet aralıkları üzerinde arama.
6. Server’a play/create/join çağrısı.
7. Loading/result dialog yönetimi.
8. Insufficient coins durumunda store açma.

Kaynaklar:

- `PlayService.cs:12-44`
- `PlayService.cs:49-164`
- `PlayService.cs:248-336`
- `PlayService.cs:342-367`
- `PlayService.cs:381-575`

---

## 7. Önerilen “clean core” tasarımı

Unity’den bağımsız bir `GinRummy.Domain` assembly’si çıkarılmalı.

### 7.1 `Card`

```csharp
public readonly record struct Card(
    Suit Suit,
    Rank Rank,
    int Id);
```

İçermeli:

- suit/rank
- point value
- joker/straight uyumluluğu
- serialization ID

Unity `Sprite`, `Transform`, `MonoBehaviour` içermemeli.

### 7.2 `Hand`

```csharp
public sealed class Hand
{
    IReadOnlyList<Card> Cards { get; }
    Hand Add(Card card);
    Hand Remove(Card card);
}
```

UI sorting ve drag/drop içermemeli.

### 7.3 `MeldSolver`

```csharp
MeldAnalysis Analyze(IReadOnlyList<Card> cards, RuleSet rules);
```

Üretmeli:

- tüm legal sets
- tüm legal runs
- optimal meld combination
- deadwood cards
- deadwood score
- gin/knock eligibility

Bu, şu anda dağılmış olan:

- `CardExtensions`
- `PlayerHand`
- `GameRules`
- `BotGameMeldTracker`
- bot strategies

içindeki rule parçalarını birleştirmeli.

### 7.4 `Scoring`

```csharp
RoundScore Calculate(
    RoundResult result,
    RuleSet rules);
```

Desteklemeli:

- gin
- big gin
- knock
- undercut
- deadwood difference
- gin bonus
- undercut bonus
- layoff
- jackpot/extra modifiers
- rush/slam sonucu

### 7.5 `TurnStateMachine`

```csharp
GameState Apply(GameCommand command);
```

Komutlar:

- `StartRound`
- `Deal`
- `DrawFromDeck`
- `DrawFromDiscard`
- `Discard`
- `Knock`
- `Gin`
- `Layoff`
- `ResolveRound`
- `RushTimeout`
- `Slam`
- `OutOfCards`

Event’ler:

- `CardDrawn`
- `CardDiscarded`
- `MeldsRevealed`
- `RoundFinished`
- `ScoreChanged`
- `TurnChanged`

Online ve bot aynı state machine’i kullanmalı.

### 7.6 `RuleSet`

```csharp
public sealed record RuleSet(
    bool Straight,
    bool Joker,
    bool Rush,
    bool NoShuffle,
    int KnockThreshold,
    int GinBonus,
    int BigGinBonus,
    bool AllowLayoff);
```

Şu anda `PlayService` query flag’lerinde dağılmış olan varyantlar domain’e açıkça taşınmalı:

`PlayService.cs:65-78`

### 7.7 Online adapter

```text
Server message
   -> MessageTranslator
   -> Domain command/event
   -> GameState
   -> Unity Presenter
```

`TableManager` yalnızca adapter/orchestrator olmalı.

### 7.8 Bot adapter

```text
GameState
   -> IBotStrategy.Decide(state)
   -> GameCommand
   -> TurnStateMachine
   -> Unity Presenter
```

`BotGameManager`:

- AI kararlarını kendi içinde uygulamamalı,
- scoring/meld hesaplamamalı,
- yalnızca bot strategy + command scheduling yapmalı.

### 7.9 Unity presentation katmanı

Aşağıdakiler domain’den ayrılmalı:

- `CardExtensions` → `CardViewAnimator`
- `TableContext` → `TableViewRefs` veya dependency container
- `Seat` → `SeatPresenter` + `SeatView`
- `FlowController` → `PresentationFlowController`
- `TableUIManager` → domain event subscriber
- `GameRules` → `GameActionPresenter` veya domain `ActionAvailability`

---

## 8. Yeniden yazılması gereken ana bölümler

### Tamamen ayrıştırılması gerekenler

1. `BotGameManager.cs`
   - 1.351 satırdan orchestration sınıfına indirilmeli.

2. `TableManager.cs`
   - server adapter, domain state reducer ve UI presenter ayrılmalı.

3. `PlayerHand.cs`
   - kart koleksiyonu ile UI/drag/sorting ayrılmalı.

4. `CardExtensions.cs`
   - gameplay isimleri korunarak yalnızca view animation katmanına taşınmalı.

5. `GameRules.cs`
   - UI state’inden bağımsız `ActionAvailability` + domain `RuleSet` ayrımı yapılmalı.

6. `BotGameMeldTracker` ve bot strategy’leri
   - ortak `MeldSolver` kullanmalı.

7. `FlowController`
   - gameplay state transition ile camera/UI animation transition ayrılmalı.

### Korunabilecek/adapter olarak kullanılabilecekler

- `CardUtils`, dönüşüm adapter’ı olarak
- `CardManager`, view instantiation katmanı olarak
- `SeatManager`, saf presenter olarak
- pile sınıfları, view projection olarak
- `PlayService`, matchmaking/lobby service olarak

---

## Sonuç

Mevcut client’ta ortak UI/scene altyapısı olsa da ortak bir Gin Rummy domain modeli bulunmuyor. Online yol server state’ine, offline yol ise `BotGameManager` içindeki ikinci bir state/rule implementation’ına dayanıyor.

En yüksek öncelikli teknik borçlar:

1. Meld/deadwood/scoring/finish kurallarını saf C# core’a taşımak.
2. Online ve bot’u aynı `GameState` + `GameCommand` modeline geçirmek.
3. `BotGameManager` ve `TableManager` sorumluluklarını bölmek.
4. `TableContext`/`GinGame` global erişimini kaldırmak veya adapter ile sınırlamak.
5. DOTween/async callback’lerini domain state transition’larından ayırmak.
6. `SeatTimerOld.cs` gibi eski implementation’ları silmek.
7. Layoff, Big Gin, undercut ve Extra varyantları için tek ve açık bir `RuleSet`/`Scoring` API’si oluşturmak.