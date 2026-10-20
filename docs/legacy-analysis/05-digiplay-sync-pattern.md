# Masa senkronizasyonu: eski motor deseni, Unity'nin kırılganlığı, yeni sözleşme (2026-10-20)

Kaynaklar: `C:\Work\digitoygames\digiplay-net\{Digibo,GinRummyExtra,YuzbirExtra}` (eski motor, yıllardır production),
`ginrummyextra.unity` (Unity Gin), `digitoyserver\DigitoyServer\Game\{Table,TablePluginBase,GinRummy,Okey}` + `Common\Models\Table.cs`
(yeni sunucu). Amaç: masa sahnesinde sunucu↔istemci haberleşmesinin **doğru kurgusu** — "her oyun elemanı her an modelden
yeniden kurulabilir, ama değişimi de algılayıp animasyon yapar" ilkesini yeni protokole taşımak. Kararlar `decisions.md 2026-10-20`.

---

## 1. Eski motor (digiplay-net) — kanıtlanmış desen

Çekirdek: `Digibo/model/TableModel.cs`, `Digibo/net/GameNetwork.cs:124-186`, `GinRummyExtra/model/GinTableModel.cs`,
`GinRummyExtra/table/CardLayer.cs`, `YuzbirExtra/Model/YuzbirTableModel.cs`, `YuzbirExtra/Table/StoneLayer*.cs`.

### 1.1 Protokol: iki giriş, keskin ayrım
- Mesajda `table` anahtarı var → **tam snapshot**: `TableModel.refresh(table, board)` (`TableModel.cs:44-75`). `raw` tamamen
  değişir, `mySeat` yeniden bulunur, oyuncunun **özel verisi** (`board`: kendi kartları/taşları) ayrı alanda gelir →
  `refreshBoard(board)` (`GinTableModel.cs:166-180`: `myCards = board.cards`, `HandSetVersion++`).
- Diğer her şey **parçalı**: `seat` (otur/kalk), `guest`, `chat`, kalanı `updateGame(msg)` → oyun modeli `action` switch'i ile yorumlar.
- İstek/cevap **yok**: kendi hamlen fire-and-forget gider, sunucu herkese (sana da) echo'lar. Reconnect'te sunucu tam `table` push'lar.

### 1.2 Model eli sahipleniyor
- `myCards` / `MyStones` **modelde**. Snapshot'ta değişir; parçalı mesajlarla yamalanır.
- Kendi echo'n **idempotent**: `ACTION_THROWCARD` ve `played == mySeat` ise kart eldeyse çıkarılır (`GinTableModel.cs:343-349`) —
  optimistic uygulanmış olsa da ikinci kez zarar vermez.
- Gin optimistic: `ThrowCard` anında `myCards`'tan çıkarır, ıskartaya iter, **`currentTurn = -500`** ile input'u paket beklemeden
  kilitler (`GinTableModel.cs:210-230`). Yüzbir: açma/işleme karmaşık olduğundan sunucu echo'su beklenir; yalnız timeout echo'su
  kendi hamlesini taşır (`YuzbirTableModel.cs:780-804`). **Bilinçli bir karar değildi** (kullanıcı notu) — iki oyunun sunucu
  davranışı aynı, farklı kodlanmış.

### 1.3 Sayaçlar
- `Version` her değişimde (`TableModel.cs:46, 81`); `RefreshCount` / `HandSetVersion` yalnız tam snapshot'ta. View, "sıfırla ve
  yeniden kur" kararını bu ikisine göre verir (`TableScene.cs:270-276`, `CardLayer.cs:66-72`).

### 1.4 View'da tek idempotent `Refresh()`
`CardLayer.Refresh()` (`CardLayer.cs:64-76`) / `StoneLayer.Refresh()` (`StoneLayer.cs:866-873`); üç uzlaştırma biçimi:
| Eleman | Yöntem | Kaynak |
|---|---|---|
| Kendi el | **id ile multiset uzlaştırma**: modelde olup ekranda olmayan → yarat; ekranda olup modelde olmayan → sil; var olan **yerinde kalır** | `CardLayer.cs:145-181`, `StoneLayer.Hand.cs:29-70` |
| Deste (gizli) | **sayı** ile: fazla sil, eksik ekle, en fazla N görsel | `CardLayer.cs:244-276`, `StoneLayer.BoardStones.cs:24-119` |
| Iskarta | **son N + id**; konum/derinlik her refresh'te yeniden yazılır | `CardLayer.cs:286-318` |
| Koltuk | doğrudan modelden idempotent | `Seat.cs:41-77` |
- **Diziliş view'da**: `Card.Order` / `Stone.Order` (`CardDragBehaviour.cs:93-111`, `StoneLayer.cs:763-794`). Üyelik sunucudan,
  sıra oyuncudan; reconcile var olan nesneye dokunmadığı için diziliş korunur. Gin sıralı eli yalnız meld/deadwood imzası
  değişince gönderir (`CheckAndSendOrderedCards`, `GinTableModel.cs:252-276`).

### 1.5 Animasyon niyeti ayrı kanal
Reconcile nihai durumu garanti eder; **kaynak/hedef** bilgisi aksiyon callback'lerinden gelir: `ctrl.pickFromBoard(turn)`,
`ctrl.throwCard(turn, card)`, `ctrl.pickFromPlayer(turn, card)` (`GinTableModel.cs:330-370` → `CardLayer.cs:664-709, 784-806`).
Animasyon yarıda kesilse de doğruluk bozulmaz.

### 1.6 Doğrulama → tam refresh
Görünen el ile model uyuşmazsa `validationError` loglanır, `TableScene.Refresh(true)` zorlanır (`CardLayer.cs:324-370`,
`StoneLayer.cs:130-160`).

### 1.7 Zayıflıklar (tekrarlanmayacak)
- Dev `switch` + elle tutulan onlarca geçici bayrak (`LastPickedStone`, `LastPlayerPickedBoardStone`, `StonesOpenAnimationPlaying`…).
- Bayat/sıra bozuk mesaj tespiti yok (sunucuda da seq yok).
- Tam refresh **tüm görselleri sıfırlar** (`StoneLayer.Reset()`), reconnect'te snap.
- `RefreshHand` yeni taş gelince otomatik sort → oyuncu dizilişini bozabiliyor (`StoneLayer.Hand.cs:86-89`'da itiraf edilmiş).

---

## 2. Unity Gin neden kart kaybediyor

`TableManager.cs` (ginrummyextra.unity): **model yok, ekran = gerçek**.
1. El yalnız `StartGame`'de sunucudan kurulur; `StartGame` önce `CardManager.Clear()`, sonra `Table.GetPlayer()==null` ise
   **return** (`TableManager.cs:201-218`). Gin sunucusunun `ResetSeat` deltası `Hand=[]` gönderir → cache'e boş el yazılır →
   sonraki faz geçişi/Refresh'te ekran boş.
2. `GameStarting`/`WaitingForPlayers` `CardManager.Clear()` çağırır (`FlowController.cs:65-110`); peşindeki rebuild atlanırsa el gider.
3. Refresh cevabı / push / DOTween deal callback'i arasında nesil bariyeri yok; reconnect kodu bu yarışı bir kez elle yamamış
   (`TableManager.cs:91-105`).
4. Optimistic el ile sunucu eli ıraksayınca (çekme deltası eli taşımaz, bkz. §3) sonraki Refresh "kart kaybı" gibi görünür.

Eski motorun §1.2–1.6 invariantlarının hiçbiri yok.

---

## 3. Yeni sunucu protokolü (digitoyserver) — yazılı olmayan sözleşme

Sunucu bir **delta/merge** protokolü konuşur (Orleans `ApplyAction` mirası); kurallar kodda örtük:

| Kural | Kaynak |
|---|---|
| `null` alan = **değişmedi**. `Hand=null` = değişmedi/maskeli (rakip eli her zaman null) | `TablePluginBase.cs:92-106 MaskHands` |
| `Hand=[]` = **gerçekten boş** (`ResetSeat`) | `GinRummyTablePlugin.cs:738-741` |
| `Players` kısmi sözlük → yalnız o koltuklar merge; ayrılma `LastMove=PlayerLeft/Kicked` ile; izleyici slotu silinir, koltuk kalır | `OkeyTablePluginBase.cs:304-312`, istemci `TableService.MergeSeats` |
| `Phase/SubPhase = None` = değişmedi (gerçek `None`'a dönüş yok) | `TableService.UpdateState` |
| Boş cevap / `TableId=0` = **Clear** (masadan çıktın) | `Table.cs:77`, `GinRummyTablePlugin.cs:207-210` |
| Çağıran, broadcast deltasını **cevap** olarak alır (aynı paket); `excludeCaller` yollarında cevap ayrı (`FullView`) | `TablePluginBase.cs:52-58` |
| **Kendi elim yalnız snapshot'ta gelir**: Join/Refresh (`FullView`), Start (alıcıya özel), Finish (Gin: iki el açık) | `GinRummyTablePlugin.cs:124-143, 302-365, 548-640`; `YuzbirTablePlugin.cs:101-135` |
| Çekme/atma/açma deltaları eli **taşımaz**: Gin Pick → `LastMove, DeckCount, DeckLastNo, TimeoutAt`; Discard → `Discards, TurnOwner, TurnNo`; Yüzbir draw → `DeckCount/DeckLastNo, TurnNo` | `GinRummyTablePlugin.cs:468-521`, `YuzbirTablePlugin.cs:148-214` |
| `TimeoutAt` taşıyan her delta `ServerTime` taşır (saat kayması) | `TablePluginBase.Stamp` |
| **Sıra numarası yok**. `TurnNo` Gin'de tur sayacı, Yüzbir'de bit alanı (pick status / gösterge / round / açma) | `Common/Okey101/TableStateExtensions101.cs:23-45` |
| Yüzbir board'u `PluginState`'te değil **`Discards` kanallarında**: `[0..3]` koltuk ıskartaları, `[4]` açma durumu, `[5]` perler, `[6]` skor satırları, `[21]` Refresh'te değişmiş satırlar, `[35]` son at/çek çifti. `[5]/[21]` Refresh'te yalnız sıra sahibi çekmişse eklenir | `YuzbirTablePlugin.cs:10-12, 137-145` |
| `PluginState` iki oyunda da istemciye gitmez (sunucu iç durumu) | plugin'lerde atama yok |
| Gin kart kimliği benzersiz: 0–51 kart, **52 = Joker1, 53 = Joker2** (eski sunucuda iki joker aynı numaraydı → eski `CardLayer` "duplicate joker" özel işlerdi; yeni istemcide el yaması küme mantığıyla idempotent) | `GinRummyShuffle.cs:11`, `Common/Ginrummy/HandSorter.cs:19-25, 164-174` |
| Reconnect: sunucu push'lamaz; istemci `Refresh` çağırır (`Server.LoadSessionAsync` yapıyor; yüklenirken push'lar düşürülür) | `Server.cs:397-404, 510-516` |

Eski protokolle fark: snapshot ayrımı örtük (tek güvenilir işaret **kendi `Hand`'im non-null ⇒ snapshot**), özel el ayrı alanda
değil `Players[me].Hand`'de ve sadece snapshot'ta; echo yerine istek/cevap (sıra: `NetworkClient.OnMessage` push'u ve TCS
devamını **inline** işler → sıra korunur; Unity'nin `Awaitable/MainThreadAsync` sıra bozulması burada yok).

---

## 4. Yeni istemci kurgusu — eski desen birebir, araya katman yok (uygulandı 2026-10-20, Adım 1–2)

```
TableService (Shared/Services)       = eski TableModel: kablo cache'i + merge (§3 kuralları) + koltuk/sıra/timer.
                                       Version (her delta), SnapshotCount (kendi Hand non-null geldi). Tek uygulama yolu Apply():
                                       push ve cevap aynı yoldan → Merge → türev kancası → View çağrısı.
                                       Kancalar (= eski refreshBoard/updateGame): OnSnapshot(), OnDelta(incoming, actorSeat), OnCleared().
                                       View'a: ITableView { Refresh(full), SeatChanged(seat), Cleared() } — TEK alan, doğrudan çağrı.
GinTableService : TableService         = eski GinTableModel: MyHand'in tek sahibi. OnSnapshot → replace (HandSetVersion++);
  (Games/GinRummy)                     OnDelta → Action switch: el yaması + IGinTableView.PickFromDeck/PickFromDiscard/Discard/Pass/
                                       Ready/DeckEmpty/Finishing(seat, card…) — anlık, paralel, kuyruk yok. Kendi hamle: optimistic +
                                       Pending kilidi + fire-and-forget Send; echo idempotent; hata → Refresh. Validate() → ihlalde Refresh.
                                       Kayıt: [ModuleInitializer] TableServices.Register(7, …); AppBoot TableServices.Create(gameId).
View (Faz 2)                           Masa sahnesi component'i IGinTableView'ı uygular; OnEnable'da View=this. Refresh(true) = tam
                                       idempotent uzlaştırma (el id-multiset, deste sayı, ıskarta son-N; eski CardLayer.Refresh);
                                       Refresh(false) = koltuk/buton/timer; hamle metotları = o elemanın küçük refresh'i + animasyon.
                                       Diziliş VIEW'da (eski Card.Order); snapshot'ta Clear() yok (id aynıysa yerinde).
                                       Alt component'ler servisi dinlemez; sahne referansla dağıtır (eski SEATME.Refresh()).
```

İnvariantlar:
1. Model her an sunucudan tek `Refresh` ile, view her an modelden tek `Refresh(true)` ile yeniden kurulabilir.
2. View asla modele yazmaz; kullanıcı girdisi → servis metodu (void) → optimistic + istek. Diziliş tek istisna: view'da yaşar, servis
   gerektiğinde parametre olarak alır (`RequestFinish(orderedHand)`).
3. Hiçbir yer cevabı **beklemez**: cevap = echo delta (sunucu `BroadcastAll` aynı paketi bana cevap, rakibe push atar), push ile
   aynı yoldan uygulanır. Hata/zaman aşımı → `Refresh` (eski fire-and-forget'ta olmayan bedava öz-iyileşme).
4. Kendi elimi etkileyen delta tutarsızsa (el 10/11 değil, tekrar var) → `Refresh`, asla Clear. Unity'nin 1–2 nolu hatası imkânsız.
5. Animasyon doğruluğun ön koşulu değil; view hamle çağrısını kaçırsa da `Refresh(true)` doğru sona varır. Kuyruk yok: rakip atarken
   biri kalkabilir, hepsi paralel yürür (eski `ctrl.throwCard` mantığı).
6. Servis↔view bağlama: delegate/event listesi yok; tek `View` alanı, sahne yoksa `null` → çağrı düşer (eski `Scene2D.currentScene as TableScene`).
7. Bilinçli olarak YOK: alan-bazlı değişim bayrakları (eski modelde de yoktu; "ne değişti" değil "ne oldu" anlamsal çağrı), ara
   `GameTable`/layout katmanı, event kuyruğu.

Açık işler:
- **Sunucu (digitoyserver):** `TableState.Seq` (monoton, masa başına; `Table.cs` iz için zaten `_seq` tutuyor) → istemci boşluk görünce
  `Refresh`. §3 sözleşmesinin Common'da yazılı hâle gelmesi (`TableState` doc-comment).
- **Test:** gerçek sunucudan kaydedilmiş delta akışı (`Tests/Fixtures/*.json`) → `GinTableService` → invariantlar; sunucu simülasyonu yazılmaz.
- Optimistic'in iki oyunda aynı olması bilinçli bir karar değil (eskide fark vardı); sorun çıkarsa oyun bazında bakılır.
