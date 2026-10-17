# Envanter — kaynak projeler (keşif: 2026-10-16)

Amaç: üç Unity istemcisini + core.upm'yi tek DigitoyEngine projesinde toplamadan önce "elimizde ne var"ı tek yerde
tutmak. Gin Rummy için ayrıntı [legacy-analysis/](legacy-analysis/) raporlarındadır; burada üç oyun yan yana.

## 1. Üç Unity istemcisi — aynı şablon, farklı olgunluk

| | ginrummyextra.unity (**prod**) | yuzbirextra.unity | okeyextra.unity |
|---|---|---|---|
| Oyun kodu | 309 dosya / ~36 K satır, `Assets/GinRummyExtra/` (Core, Shared, Table, Lobby, Login, Dialogs, Scenes, Dev, Bonuses) | 95 dosya / ~13.5 K satır, `Assets/YuzbirExtra/` (Core, Logic, Dialogs, Scenes/{Login,Lobby,Table}, Shared, Tests) | 41 dosya / ~3 K satır, `Assets/Okey/` (Core, Scenes, Table, Dialogs, Radio) + `Assets/Components` |
| Mimari | Düz `GinRummyExtra` ns, statik service locator (`GinGame.Context/Manager/Rules`), god-class'lar (`BotGameManager` 1351, `TableManager` 725, `CardExtensions` 729, `TableUIManager` 661, `TableHintController` 584, `PlayService` 581). Kendi `ARCHITECTURE_PLAN.md` (2267 sat.) aynı teşhisi koyuyor, uygulanmamış. | `GameService` tabanlı prefab'sız bootstrap (`Core/GameManager.cs`), `YuzbirExtra.Logic` asmdef (**saf C#, yalnız Digitoy.Common**: `MeldStateSnapshot`, `UsefulMeldPlanner`), EditMode testleri, `Docs/` (FEATURE_PLAYBOOK, SERVER_CONTRACTS, features/*) | Eski prefab-bootstrap `GameManager`, doküman yok, test yok — **prototip** |
| Sahneler | Entry, Login, Lobby (+LobbyScene1), Table, FX | Login, Lobby, Table (+HapticTest) | Login, Lobby, Table (+DebugLogin) |
| core.upm | `file:` yerel | git `3.3.6` | git `#dev2` (eski) |
| Ek Digitoy paketleri | minigames.upm, offers.upm, features.unity | uibox (yerel `unity/Box`), features | movieclip.upm, localization.upm, ui.upm |
| Animasyon | Animator ×42 + DOTween + Timeline | yalnız DOTween | DOTween + MovieClip |
| Varlık | 298 prefab, 405 png, 279 psd, 14 atlas, 5 OTF, 34 wav / 33 mp3, 1 mp4 | 88 prefab, 303 png, 2 TTF | küçük |
| 3. parti | DOTween Pro, Odin, SRDebugger, Addressables, MAX (+6 adapter), Firebase, Unity IAP, Adjust, Facebook, Apple Sign-In, Play Integrity, Native Share, Helpshift | DOTween Pro, Graphy, MAX, Unity IAP, Firebase, Adjust, Facebook, Apple Sign-In, Play Integrity | DOTween, Input System, Timeline, ParrelSync |

Ortak kabuk (Login → Lobby → Table; `Player/Table/Friends`; Dialogs: profil, store, günlük bonus, sosyal, chat, hediye,
masa filtresi, prompt; `RadioPlayer`; `TurnLight`) her projede **kopya**, paylaşılan kaynak değil. Kural kodu
istemcilerde dağınık: Gin'de **3 kopya** (online UI / bot / hint), Yüzbir'de Logic asmdef'te toplanmış, Okey'de yok.

### Kilit dosyalar
- Gin: `Assets/GinRummyExtra/Core/{GameManager*.cs,Table.cs,Player.cs,Definitions.cs}`, `Table/Scripts/{Managers,Views,Actions,BotGame,Hands}`,
  `Scenes/Login/LoginController.cs`, `Scenes/Lobby/Scripts/LobbyController.cs`, `Shared/Managers/{IAPManager,RewardsManager,SocialManager}.cs`, `Dialogs/*`.
- Yüzbir: `Core/GameManager.cs`, `Scenes/Table/Scripts/{TableManager,TableContext,TableUiManager,Board,YuzbirExtraTile,YuzbirTileManager}.cs`,
  `Scenes/Lobby/Scripts/{LobbyManager,LobbyContext}.cs`, `Logic/{MeldStateSnapshot,UsefulMeldPlanner}.cs`, `Docs/SERVER_CONTRACTS.md`.
- Okey: `Core/GameManager.cs`, `Table/{Board,GameContext,Seat,OkeyTile,OkeyTileManager}.cs`, `Assets/Components/{Tile,TileManager,Sorter}.cs`.

## 2. core.upm — Digitoy SDK (v3.3.11, ~200 dosya / ~40 K satır)
| Klasör | Satır | Port kararı |
|---|---:|---|
| `Runtime/UI` | 19 322 | **Port edilmez** (Canvas/TMP/Addressables/DOTween). Kavramlar (DialogBase, UIButton, UICounter, UILocale, UIParticleImage) motor karşılıklarıyla yeniden yazılır. |
| `Runtime/Components` | 9 718 | **Mantık port edilir**: PlayerService, LobbyService, TableService, MessagingService, Friends, Economy, Config, Auth, Gifts, Album, Features, Notifications. IAP/Ads/Adjust/Firebase/AppsFlyer → native köprü bekler. |
| `Runtime/Anim` | 2 966 | Port edilmez (DOTween sarmalayıcı + MovieClip runtime) → motor Tween/MovieClip. |
| `Runtime/Network` | 1 802 | **Adapte edilir** (decisions 2026-10-16): `Server` durum makinesi (Idle/Connecting/Healthy/Reconnecting/Dead/Killed, relogin, disconnect nedenleri), `ServerDirectory`, heartbeat (25 sn `Endpoint.Ping`; sunucu 60 sn'de düşürür) aynen kopyalanır; yalnız `BestWebSocketClient` içindeki Best HTTP soket katmanı motor `WebSocket` + `NativeAsync` ile yeniden yazılır. |
| `Runtime/Dev` | 1 739 | Port edilmez (editör RPC/`render.describe` bu rolü alır). |
| `Runtime/Helpers` | 1 423 | `Result`, `Service/ServiceRegistry`, `GameProperties` (→ `[Asset]`), `LocalCache` (→ PlayerPrefs). `JsonHelper` Unity JsonUtility'ye bağlı. |
| `Runtime/Localization` | 899 | Port edilmez → motor `LocalizationTable` + `@loc`; `LocalizationFormatter` regex'i motor Regex'inde zaten destekli. |
| `Runtime/DeviceKey` | 420 | Mantık port edilir (CRC + sürüm; Widevine/Android ID native köprü). |
| `Runtime/Extensions` | 433 | DTO dönüşümleri, port edilir. |

## 3. Sunucu — digitoyserver (tek .NET sunucu, üç oyun)
- Aktör modeli, Redis izdüşümü, PG write-behind. Plugin'ler: `DigitoyServer/Game/GinRummy/GinRummyTablePlugin.cs` (40 K),
  `Game/Okey/{OkeyTablePluginBase (35 K), YuzbirTablePlugin (26 K), OkeyShuffle}`. **Klasik Okey plugin'i yok** (taban sınıf var).
- `Common/` = `Digitoy.Common.dll` (`netstandard2.1;net8;net10`, `namespace M`, saf POCO, dış bağımlılık yok):
  `Models/{Auth,Config,Economy,Endpoint,Errors,Leaderboard,Message,Player,Table,Country}.cs`, `Compression/` (kendi
  Inflater/Deflater), `Ginrummy/{HandScore,HandSorter (122 K),RoundScorer,CardHelper,GinFlags}`, `Okey101/{YuzbirGame,OkeyCheck,Meld,
  RunLimiter,Okey101Flags,TableStateExtensions101/Okey}`.
- **2026-10-15 değişikliği:** `Common.Generators` Roslyn source generator — `ICodecModel` tipleri için `Serializer<T>` derleme
  zamanında üretilir; `Serializer.cs` artık `MakeGenericType`/`Expression` kullanmıyor (yalnız `CodecName` tip-adı
  araması `GetCustomAttribute<CodecNameAttribute>`). Legacy-analysis B1 blokeri büyük ölçüde kapandı; AOT'de doğrulanacak.
- Wire: binary WebSocket `ws://<gateway>/api/websocket/connect`; çerçeve `[requestId:long][endpoint:long][object]`, cevap
  `[requestId][object]`, `requestId==-1` push, `0` ping; Codec tipli paket akışı (`PacketType` NULL/TRUE/.../ZOBJ), nesneler
  `[CodecName]` kısa alan adlı dict, üst seviye deflate. Masa aksiyonu tek `GameActionRequest{Action, Params:List<long>}` →
  `GameActionResult{Error, TableState, CallerEvent}`; Gin `GameAction` 700-710; mesajlaşma 700-704 ayrı uç.
- Dokümanlar: `Docs/README.md` (mimari), `Docs/{Auth,Config,Deploy,GameplayEvents,DeviceKey2}.md`, `.ai/{CONTEXT,DECISIONS}.md`.
  Sunucu ekibi kuralı: plan→onay, tek dosya/tek iş.

## 4. digiplay-net — eski nesil (yalnız referans)
Xamarin / .NET Framework 4.7.2 dönemi: `Digiplay` (OpenTK/WebGL/Metal 2D-3D istemci motoru, XML sahne) + `Digibo` (hesap/
sosyal/ağ; **farklı** Codec + `GameNetwork` komut seti) + eski istemciler (GinRummyExtra 9.6 K, YuzbirExtra 13.2 K,
YuzbirExtraNew 13.6 K — kopya, OkeyExtra 8.1 K). `modern/` yarım kalmış modernizasyon. Sunucu yok.
**Karar: kod alınmaz.** Tek referans değeri: `OkeyExtra/table/OkeyCheck.cs` + `Sorter.cs` — klasik Okey kuralları
(Common'da klasik Okey yok; sunucu plugin'i yazılırken bakılır).

## 5. game_rd içindeki mevcut denemeler
- `Projects/*` (ginrummy, yuzbirextra, Sandbox): **motor playground'u — buradan kod/asset alınmaz** (karar 2026-10-16).
