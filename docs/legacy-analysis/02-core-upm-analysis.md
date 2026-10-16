# `core.upm` analysis report

## Scope and documentation

- Package: `com.digitoygames.core`, version `3.3.6`, Unity `2021.3` — `C:\work\digitoygames\core.upm\package.json:2-6`
- Package description: authentication, remote configuration, player state, economy events, storage, localization and UI — `package.json:7`
- Public API reference covers boot, server, player state, economy, messaging, lobby/table, UI, animation, editor tooling and extensions — `C:\work\digitoygames\core.upm\SDK-API-Reference.md:1-20`
- README examples cover initialization/login, remote config, player state, events, storage, friends, async APIs and real-time events — `C:\work\digitoygames\core.upm\README.md:1-20`
- No `Messaging` files exist directly under `Runtime\Messaging`; the implementation is under `Runtime\Components\Messaging`.

## 1. Runtime module summary

Line counts include `.cs` files recursively and include blank/comment lines.

| Runtime folder | Files | Lines | Summary |
|---|---:|---:|---|
| `Anim` | 15 | 2,966 | Animation abstraction over DOTween. `AnimFacade`, `AnimHandle`, `AnimSequence`, `AnimTimeline`, awaiters, extension methods and animation enums. Supports awaitable sequences and reusable animation commands. |
| `Build` | 1 | 33 | Build/platform utility code and build-time conditional helpers. |
| `Components` | 61 | 9,718 | Main service/component layer: `GameService`, `PlayerService`, `Config`, `Server`, `MessagingService`, friends, lobby/table, albums, ads, notifications, feature rules, remote assets and Unity-facing components. |
| `Dev` | 7 | 1,739 | Development commands, dev registry/panels, debug console/status UI, button attributes and development-only puppet tooling. |
| `DeviceKey` | 11 | 420 | Device identity generation and persistence. Platform-specific Android, iOS, WebGL, Unity and fallback implementations. |
| `Extensions` | 3 | 428 | Data/model extensions, reward/economy helpers, metadata helpers and low-level `M` codec/data helpers. |
| `Helpers` | 13 | 1,423 | General SDK helpers: service lifecycle, async/task helpers, time, collections, platform and utility functionality. |
| `InstantGames` | 4 | 222 | WebGL/instant-game provider abstraction and provider implementation. |
| `Localization` | 5 | 899 | Localization data/loading, locale management and runtime translation support. |
| `Messaging` | 0 | 0 | Empty directory. Actual implementation is `Runtime\Components\Messaging\MessagingService.cs`. |
| `Network` | 8 | 1,802 | WebSocket client, server connection/login orchestration, server directory, network delay and connectivity probing. |
| `Plugins` | 0 | 0 | No C# source files. |
| `UI` | 70 | 19,248 | Dialog/popup framework, UI components, navigation/state, addressable scene loading, localization widgets, avatars, tabs, sequencers, effects and safe-area/layout helpers. |

The package contains additional root-level Runtime source outside these folders, including `ServerLegacy.cs` and `TimeSync.cs`.

## 2. Network layer

### Transport

The runtime transport is WebSocket-based, not raw TCP or ordinary request/response HTTP.

- `INetworkClient` exposes `ConnectAsync`, `Disconnect`, `SendRequestAsync<T>`, `OnDisconnected` and `OnEvent` — `C:\work\digitoygames\core.upm\Runtime\Network\INetworkClient.cs:10-19`
- Concrete implementation: `BestWebSocketClient` — `C:\work\digitoygames\core.upm\Runtime\Network\BestWebSocketClient.cs`
- `InternetProbe` uses `UnityWebRequest`, but only for connectivity probing, not the game protocol — `C:\work\digitoygames\core.upm\Runtime\Network\InternetProbe.cs`
- `ServerDirectory` manages gateway/seed server discovery and selection — `C:\work\digitoygames\core.upm\Runtime\Network\ServerDirectory.cs`

### Connection and login

`Server` is the public connection/service façade and owns connection state and login orchestration.

Important state values include:

- `Idle`
- `Connecting`
- `Healthy`
- `Reconnecting`
- `Killed`

These are declared in `C:\work\digitoygames\core.upm\Runtime\Network\Server.cs:14-21`.

Login progress includes an authentication/socket phase (`LoginStep.Auth`) — `Server.cs:24-27`.

The `Server` API handles:

- Initial connection and authentication
- Login/relogin
- Server/gateway selection
- Connection-state transitions
- Endpoint request dispatch
- Push/event handling
- Disconnect reasons, including unauthorized/kicked/another-login terminal states

`ClientAuthParams` carries the authentication parameters used during the login handshake — `C:\work\digitoygames\core.upm\Runtime\Network\ClientAuthParams.cs`.

### Framing and serialization

The network protocol is endpoint-based:

```csharp
Task<T?> SendRequestAsync<T>(
    int endpoint,
    object? request,
    CancellationToken cancellationToken = default)
```

— `C:\work\digitoygames\core.upm\Runtime\Network\INetworkClient.cs:15-17`.

The public API exposes integer endpoint IDs and typed response deserialization rather than URL-per-request HTTP calls. The package depends on Unity’s Newtonsoft JSON package:

```json
"com.unity.nuget.newtonsoft-json": "3.0.2"
```

— `C:\work\digitoygames\core.upm\package.json:17-22`.

The SDK also contains the `M` model/codec namespace and low-level codec helpers documented under “Low-level codec (`M`)” — `SDK-API-Reference.md` around the Extension Methods section.

The available source indicates:

- WebSocket frames are converted into endpoint/request or event messages.
- Requests are correlated with typed responses.
- Server-originated messages are delivered through `OnEvent` and then routed into services.
- JSON/Newtonsoft is used for SDK data/config/model handling; the transport should not be assumed to be plain REST JSON based solely on the HTTP probe or package dependency.

For an exact wire-compatible port, inspect the complete implementation of `BestWebSocketClient.cs`, `Server.cs`, and the `M` codec/endpoint definitions together with the server protocol. The key client contract is the endpoint integer plus serialized request object and typed response.

### Request/response versus push

`INetworkClient` explicitly separates:

- `SendRequestAsync<T>` for correlated request/response calls.
- `OnEvent` for unsolicited server push.
- `OnDisconnected` for transport/lifecycle notifications.

`BestWebSocketClient` publishes connection state before opening the socket so synchronous callbacks can safely observe the new connection — `C:\work\digitoygames\core.upm\Runtime\Network\BestWebSocketClient.cs` comment near its connection setup.

`Server` routes push events into higher-level services. For example, server events involving a player preview can result in `MessagingService.HandleServerEvent(...)` — `C:\work\digitoygames\core.upm\Runtime\Network\Server.cs` and `C:\work\digitoygames\core.upm\Runtime\Components\Messaging\MessagingService.cs`.

### Reconnection

The connection state machine includes explicit `Reconnecting`. `ConnectAsync` accepts a `relogin` flag, allowing reconnection to repeat authentication rather than merely reopening the socket — `INetworkClient.cs:15`.

Disconnect reasons distinguish recoverable connection loss from terminal conditions such as logout, unauthorized access, kicking, another login or deadline expiry. The port must preserve these distinctions because they affect whether credentials are reused and whether the client returns to a healthy state.

### Authentication and DeviceKey

`DeviceKey` is an internal static identity provider:

- `DeviceKey.Value` lazily computes and caches the key.
- It selects version 0 or version 1 calculation.
- `CRC(string src)` appends a short checksum.
- Unsupported platforms return `"FALLBACK"`.

— `C:\work\digitoygames\core.upm\Runtime\DeviceKey\DeviceKey.cs:1-35`.

Platform implementations are split into:

- `DeviceKey.Android.cs`
- `DeviceKey.iOS.cs`
- `DeviceKey.Unity.cs`
- `DeviceKey.Web.cs`
- `PersistentDeviceKey.*`
- `KeyChain.cs`

The persistent implementation uses platform storage/keychain mechanisms. A non-Unity port must supply equivalent stable-device identity and secure persistence, otherwise login/device association behavior will differ.

## 3. Messaging/event bus

Implementation:

- `C:\work\digitoygames\core.upm\Runtime\Components\Messaging\MessagingService.cs`
- Public API documented under `MessagingService` and `InboxService` — `C:\work\digitoygames\core.upm\SDK-API-Reference.md` in the Messaging section.

The flow is:

1. WebSocket push arrives at `INetworkClient.OnEvent`.
2. `Server` routes the event to the appropriate service.
3. `MessagingService.HandleServerEvent(...)` interprets the server event.
4. Service state/events are raised to subscribers, including support-message and friendship/message notifications.

`MessagingService` is a `Service` subclass, not a separate generic event-bus package. Subscriber lifetime must follow the package rule that runtime event subscriptions have explicit unsubscribe lifecycles — `C:\work\digitoygames\core.upm\AGENTS.md`.

The Gin Rummy client references `MessagingService` seven times by the simple-token count, making it one of the more relevant core services.

## 4. UI framework

### Dialogs/popups

The dialog system is centered on:

- `Dialog`
- `DialogService`
- `UIManager`
- `UIState`

`DialogService` uses Addressables — `C:\work\digitoygames\core.upm\Runtime\UI\Core\Dialog\DialogService.cs`.

The API reference describes dialog opening/closing and UI components under “UI and Dialogs” — `SDK-API-Reference.md` UI section. The framework supports prefab/addressable-backed dialogs, lifecycle callbacks, modal/pop-up behavior and dialog stack/state management.

### Navigation and scenes

`UI.Core.Utils.LoadSceneAsync(string sceneName)` uses Addressables scene loading — `C:\work\digitoygames\core.upm\Runtime\UI\Core\Utils.cs`.

Consequently, scene navigation is not a pure engine-independent abstraction: it depends on Unity Addressables and Unity scene APIs.

### Localization

Localization exists both as a service/module and as UI components:

- Runtime localization module: `Runtime\Localization`
- UI binding component: `UILocale`
- Localized text components use TextMeshPro.

`UILocale` is among the most frequently referenced core types in the client, with 98 matching occurrences in the usage scan.

### Animation

Animation is implemented through DOTween:

- `AnimFacade`
- `AnimHandle`
- `AnimSequence`
- `AnimTimeline`
- `AnimAwaiter`
- UI sequencer steps

Runtime files directly import `DG.Tweening`, including `Anim\Engine\*.cs` and UI sequencer files. DOTween is therefore a real runtime dependency, although it is not listed in this package’s `package.json` dependencies and is likely supplied by the consuming Unity project or another package.

### Safe area/layout

Safe-area and device/layout helpers are contained in UI components/utilities rather than the network/service layer. They depend on Unity `RectTransform`, `Screen`, canvas and device APIs. They must be rewritten or replaced in a custom engine.

## 5. Third-party dependencies

### Declared in `package.json`

- Newtonsoft JSON — `com.unity.nuget.newtonsoft-json: 3.0.2`
- Apple iOS support — `com.unity.ads.ios-support: 1.0.1`
- Unity UIElements — `com.unity.modules.uielements: 1.0.0`
- Addressables — `com.unity.addressables: 1.21.0`
- TextMeshPro — `com.unity.textmeshpro: 3.0.0`

`C:\work\digitoygames\core.upm\package.json:16-23`

### Observed source dependencies

- `Newtonsoft.Json`, `Newtonsoft.Json.Linq`: runtime/editor config, ads and SDK tooling.
- `UnityEngine.AddressableAssets`: remote content, albums, UI scene/dialog loading.
- `TMPro`: buttons, toggles, labels, text, tabs, localization and counters.
- `DG.Tweening`: animation engine and UI sequencer.
- `UnityEngine.Networking`: connectivity probes/config/avatar loading.
- Apple Auth/editor references in editor assembly definitions.
- Unity Addressables editor references.
- No meaningful runtime `Cysharp`/UniTask or `Sirenix` usage was found in the targeted source scan.
- DOTween is used but is not declared in `package.json`; consuming projects must provide it.

Editor assembly references include Newtonsoft, `Digitoy`, Apple Auth editor support and Addressables editor support — `C:\work\digitoygames\core.upm\Editor\Digitoy.Editor.asmdef`.

## 6. What Gin Rummy actually uses

Client scan scope:

`C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\**\*.cs`

The scan found 216 C# files and 741 matching core-related tokens. Most-referenced core type/token counts:

| Rank | Core type/token | Count |
|---:|---|---:|
| 1 | `Anim` | 216 |
| 2 | `Dialog` | 178 |
| 3 | `UILocale` | 98 |
| 4 | `Server` | 90 |
| 5 | `UIManager` | 40 |
| 6 | `UIState` | 39 |
| 7 | `UIButton` | 34 |
| 8 | `UICounter` | 29 |
| 9 | `Config` | 29 |
| 10 | `UIParticleImage` | 20 |
| 11 | `UIAvatar` | 19 |
| 12 | `UIActions` | 15 |
| 13 | `Localization` | 11 |
| 14 | `UIParticle`/related particle tokens | 9 each |
| 15 | `UINickName` | 8 |
| 16 | `UISequencer` | 7 |
| 17 | `MessagingService` | 7 |
| 18 | `DeviceKey` | 4 |

Most relevant client areas:

- `Core\GameManager*.cs`: SDK/game boot and service initialization.
- `Scenes\Login`: login and server connection.
- `Scenes\Lobby`, `Lobby`, `Table`: `Server`, lobby/table services and state.
- `Dialogs`: `Dialog`, UI framework, localization, animations, rewards and messaging.
- `Shared\Utils`: notification badges and service/event integration.
- `Editor\GameDebugWindow.cs`: unusually high count because it references many debug/core APIs.

The client is therefore heavily dependent on the UI/animation surface and the `Server`/service layer, not just the low-level network client.

## 7. Unity-coupling assessment

### Relatively portable / pure C# candidates

These are the best candidates for reuse in a custom engine, after replacing Unity scheduling/storage primitives where necessary:

- Network protocol abstractions:
  - `INetworkClient`
  - request/response endpoint model
  - connection state and error enums
  - portions of `ClientAuthParams`
- Server/service domain logic:
  - endpoint request construction
  - response/model mapping
  - player, friends, economy, leaderboard and messaging domain models
- `M` models and low-level codec helpers.
- Most `Extensions` data/reward/metadata helpers.
- `ServerDirectory` selection logic, if Unity-specific persistence/JSON calls are isolated.
- Parts of `MessagingService` event routing.
- Device-key checksum/version logic; platform key storage must be replaced.

### Moderately coupled

These contain portable logic but rely on Unity runtime facilities:

- `Config`: UnityWebRequest and Unity asset/runtime loading.
- `PlayerService`, `LobbyService`, `TableService`, `MessagingService`: service lifecycle and Unity async/event integration.
- `Localization`: data logic is portable, but asset loading, resources and UI bindings are Unity-specific.
- `Anim`: API concepts are portable, but implementation requires DOTween or a replacement tween scheduler.
- `InstantGames`: provider contracts are portable; WebGL bridge implementation is platform-specific.
- `Dev`: registry/debug logic is portable, but panels, Unity editor and runtime UI are not.

### Deeply Unity-bound

These require substantial rewriting for a custom engine:

- `UI` components: `MonoBehaviour`, `GameObject`, `Transform`, `RectTransform`, Canvas, Unity events and serialized fields.
- `DialogService` and addressable-backed dialogs.
- `UIManager`, `UIState`, scene loading and Addressables.
- TextMeshPro-dependent controls such as `UIButton`, `UIToggle`, `UILocale`, `UICounter`, `UINickName`.
- UI sequencer and particle/effect components.
- DOTween-based animation bindings.
- Avatar/remote asset loading.
- Safe-area and screen/device layout code.
- Android/iOS/WebGL device-key implementations and keychain/platform storage.
- Editor tooling under `Editor\`, which depends on UnityEditor, Addressables editor APIs, serialized objects and inspector windows.

### Porting priority

For a server-compatible custom-engine client, port in this order:

1. `M` models/codecs and endpoint definitions.
2. `INetworkClient`/`BestWebSocketClient` protocol behavior.
3. `Server` authentication, reconnect and state machine.
4. `DeviceKey` semantics and persistent identity.
5. `MessagingService` push dispatch.
6. Required domain services (`PlayerService`, lobby/table/messaging).
7. Replace `Anim`, dialogs and UI with engine-native equivalents.
8. Omit or separately recreate `Editor`, `Dev`, Addressables and Unity-only components.