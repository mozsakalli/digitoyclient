# DigitoyServer Gin Rummy analysis

## Scope and line counts

| File | Lines |
|---|---:|
| `C:\work\digitoygames\digitoyserver\DigitoyServer\Game\GinRummy\GinRummyTablePlugin.cs` | 746 |
| `C:\work\digitoygames\digitoyserver\DigitoyServer\Game\GinRummy\GinRummyShuffle.cs` | 73 |
| `C:\work\digitoygames\digitoyserver\DigitoyServer\Game\Table.cs` | 256 |
| `C:\work\digitoygames\digitoyserver\DigitoyServer\Game\TablePluginBase.cs` | 138 |
| `C:\work\digitoygames\digitoyserver\DigitoyServer\Game\TableList.cs` | 128 |
| `C:\work\digitoygames\digitoyserver\Common\Models\Table.cs` | 363 |
| `C:\work\digitoygames\digitoyserver\Common\Ginrummy\RoundScorer.cs` | 624 |

---

## 1. Wire protocol

### Transport and framing

The gameplay transport is a binary WebSocket protocol:

- WebSocket gateway and link/delivery loop: `C:\work\digitoygames\digitoyserver\Docs\README.md:166`
- Gateway frames are delivered through Redis `gw:{gatewayId}` lists: `Docs\README.md:29`
- Responses are encoded once and may be compressed before being sent to multiple recipients: `Docs\README.md:284`
- Common payloads use the project codec with `[CodecName]` abbreviated field names; `Common/Encoded.cs` documents that there is no additional envelope.
- The client request is routed through `ClientRequest`, then to a table actor/plugin. Table routing, pointer handling, reconnect/release behavior, and endpoint-to-action conversion are in `DigitoyServer/Handler/Handler.Table.cs:12-99`.

### Core request/response models

`Common/Models/Table.cs:192-236`:

`GameActionRequest` payload fields:

- `GameId`
- `PlayerId`
- `TableId`
- `Bet`
- `Queries`: flag queries
- `Action`
- `Params`: `List<long>`; card IDs and finish-hand encoding are sent here
- `Source`
- `Item`
- `MinBet`
- `MaxBet`

`GameActionResult`:

- `Error`
- `TableState`
- `CallerEvent`

`TableState` fields relevant to Gin Rummy are defined at `Common/Models/Table.cs:157-185`:

- `TableId`
- `GameId`
- `TurnNo`
- `TurnOwner`
- `Phase`
- `SubPhase`
- `Players`
- `Spectators`
- `Deck` — hidden from normal client views
- `DeckCount`
- `DeckLastNo`
- `Discards`
- `Flags`
- `Bet`
- `Jackpot`
- `Action`
- `ServerTime`
- `TimeoutAt`
- `GameEndsAt`
- `PluginState`

`SeatPreview` fields, `Common/Models/Table.cs:92-137`:

- `PlayerId`
- `FacebookId`
- `Nick`
- `Stats`
- `Balance`
- `Hand`
- `LastMove`
- `Score`
- `Position`
- `EventResult`
- `Message`

### Gin Rummy action/message names

There are no separate Gin-specific wire classes for each move. Gin actions are `GameAction` enum values in `Common/Models/Table.cs:8-66`, carried in `GameActionRequest.Action`:

| Action | Value | Direction | Payload |
|---|---:|---|---|
| `Pass` | 700 | client → server | no required params |
| `Ready` | 701 | client → server | no required params |
| `PickFromDeck` | 702 | client → server | `Params[0]` = selected/top card, validated against server deck |
| `PickFromDiscard` | 703 | client → server | `Params[0]` = discard card, validated against discard pile |
| `Discard` | 704 | client → server | `Params[0]` = card |
| `RequestFinish` | 705 | client → server | `Params` = client’s 11-card hand/order representation |
| `OpponentFinished` | 706 | server → opponent | no client payload beyond state/action |
| `SendFinish` | 707 | client → server | `Params` = defender’s hand representation |
| `NewGame` | 708 | state/action name | not dispatched as a client move in this plugin |
| `NewRound` | 709 | state/action name | server state transition |
| `DeckEmpty` | 710 | server state/action | deck exhaustion/restart |
| `PlayerJoined` | 1 | server push | updated `Players`/state |
| `PlayerLeft` | 2 | server push | updated seat/state |
| `Start` | 3 | server push | new deal/state |
| `Timeout` | 4 | server push | timeout delta; may include changed hand/turn |
| `Finish` | 5 | server push | round scoring/result state |
| `PlayerKicked` | 6 | server push | leave/forfeit state |
| `Refresh` | 7 | request/response | full state view |
| `GameTimeout` | 8 | server push | Rush game expiration |
| `PlayerUpdated` | 9 | server push | player metadata/stat update |
| `UpdatePlaytime` | 10 | server/economy event | playtime progression |

The actual dispatch table is `GinRummyTablePlugin.Dispatch`, `GinRummyTablePlugin.cs:76-91`. Only `Refresh`, `CreateTable`, `JoinTable`, `LeaveTable`, `Ready`, `SendFinish`, and the move actions are accepted.

### Joining a table

1. Client sends `TableCreate`, `TableJoin`, or `TablePlayNow`.
2. `Handler.TableReq` decodes `GameActionRequest`.
3. `Handler.Enter` writes `CurrentTableId` and forwards the request to the table queue: `Handler.Table.cs:12-31`.
4. `Handler.Act` maps endpoint to `CreateTable` or `JoinTable`, creates a seat, handles duplicate join as `Refresh`, and supports spectator conversion using `Params=[-1]`: `Handler.Table.cs:47-77`.
5. Plugin methods:
   - `OnCreateTable`: `GinRummyTablePlugin.cs:140-176`
   - `OnJoinTable`: approximately `GinRummyTablePlugin.cs:178-215`
6. Two-player tables transition to `Starting/ReadyCheck`; each player must send `Ready`.

Spectators are represented in `TableState.Spectators`; the handler explicitly supports “leave seat → watch” via `Params=[-1]`, `Handler.Table.cs:70-77`.

### Server push

Pushes are generally `GameActionResult`/`TableState` deltas, with `TableState.Action` indicating the event. Relevant state transitions are implemented in:

- Start/deal: `GinRummyTablePlugin.cs:260-350`
- Turn changes and move deltas: approximately `350-500`
- Finish protocol: approximately `500-640`
- Round/game transitions: approximately `620-746`

The server masks opponent hands in `FullView`, `GinRummyTablePlugin.cs:126-137`; the full opponent hand is exposed only in `RoundOver` or `GameOver`.

---

## 2. Gin Rummy rule implementation

### Configuration and variants

`GinRummyTablePlugin.cs:26-46` defines:

- Standard turn: 20 seconds
- Rush turn: 10 seconds
- Per-player extension: 20 seconds
- Finish grace: 2 seconds
- Round-over wait: 15 seconds
- Game-over wait: 22 seconds
- Rush game duration: 60 seconds
- Deck-empty restart: 3 seconds
- Maximum knock deadwood: 10
- Rake ratio: 10%
- Rush, Straight, Score, Lobby, and Joinable flags

Table creation validates the lobby and score target. Rush forces score target to zero; ordinary scores default if invalid: `GinRummyTablePlugin.cs:140-176`.

### Dealing

`Start` uses `GinDealer.DealHands`, then assigns:

- 10 cards to each player
- Remaining cards to `TableState.Deck`
- First/open discard card to the discard pile

The deal and initial state are handled in the plugin’s start/deal section, approximately `GinRummyTablePlugin.cs:260-350`.

### Drawing and discarding

`Dispatch` routes `Pass`, `PickFromDeck`, `PickFromDiscard`, `Discard`, and `RequestFinish` to `OnMove`: `GinRummyTablePlugin.cs:76-91`.

The plugin tracks:

- `_pickedFromDiscard`
- `_pickedFromDeck`
- `_finishDiscard`
- `_turnStartedAt`

These are persisted in `SaveState`/`RestoreState`: `GinRummyTablePlugin.cs:108-124`.

The server checks turn ownership, pile membership, card ownership, and the required draw/discard sequence before changing the state. It does not simply trust the client’s card list.

### Knock, gin, and finish validation

The finish request sends an 11-card hand representation. The plugin verifies that the client’s cards are a permutation of the server-authoritative hand, then analyzes it through `HandScore`.

The plugin summary explicitly states:

- Knock allowed at deadwood ≤ 10
- Straight variant allows gin behavior
- Finishing has a 2-second defender-response period
- Missing defender response is automatically sorted by `HandSorter`

These behaviors are documented in the plugin header, `GinRummyTablePlugin.cs:9-22`, and implemented in the finish methods in approximately `500-640`.

The server therefore validates:

- Hand membership/permutation
- Meld structure and deadwood through `HandScore`
- Knock threshold
- Gin condition
- Finish timing
- Opponent hand submission

### Melds, layoff, scoring, gin, big gin, and undercut

Shared scoring code is in `Common/Ginrummy/RoundScorer.cs`:

- `RoundOutcome`: `RoundScorer.cs:7-16`
- `AnalyzeRush`: `81-106`
- Normal scoring entry point `Analyze`: `135-183`
- Result calculation: `185-290`
- Layoff calculation is used only when the knocker is not gin and layoff is enabled.
- Gin bonus: 25
- Undercut bonus: 25
- Big Gin bonus constant: 31, but marked “not used with 10-card hands”: `RoundScorer.cs:43-50`
- Joker deadwood value: 10
- Joker discard doubles defender deadwood: `RoundScorer.cs:138-151`, `RoundScorer.cs:215-229`

Outcomes:

- `Gin`
- `Undercut`
- `KnockWin`
- `BigGin`
- `Rush`

Normal scoring:

- Gin: knocker receives defender deadwood plus 25.
- Undercut: defender receives the deadwood difference plus 25.
- Knock win: knocker receives the post-layoff deadwood difference.
- Layoff reduces defender deadwood before comparison.
- A discarded joker doubles the defender’s post-layoff deadwood.

The plugin invokes `RoundScorer` during its finish path, approximately `GinRummyTablePlugin.cs:520-640`.

### Rush

Rush is implemented as a timed one-round variant:

- 60-second absolute game duration
- 10-second turns
- When the game timer expires, `RushTimeUp` invokes Rush scoring
- `RoundScorer.AnalyzeRush` compares deadwood:
  1. Lower deadwood
  2. Fewer deadwood cards
  3. Longer run
  4. Player 0 wins ties

Relevant locations:

- Rush constants: `GinRummyTablePlugin.cs:28-33`
- Timeout dispatch: `GinRummyTablePlugin.cs:94-105`
- Rush scoring: `RoundScorer.cs:81-106`
- Rush timeout handling: plugin’s `RushTimeUp` method, approximately `GinRummyTablePlugin.cs:430-500`

### Slam and “extra”

The server Gin plugin has no distinct `Slam` action or separate Slam scoring outcome. The Unity client contains Slam presentation/flow code, but the server protocol uses the ordinary draw/discard/finish actions.

Likewise, “Extra” is the Unity project/product name rather than a distinct server action. The server exposes flags and plugin state rather than a separate `Extra` command.

### Round/game end and target score

- Target score is extracted from the Gin flags: `GinRummyTablePlugin.cs:63-66`.
- After `Finish`, target reached → `GameOver` with a 22-second wait.
- Otherwise → `RoundOver` with a 15-second wait.
- Both eventually return to `ReadyCheck`/`Starting`.
- The plugin resets deck, discard, turn and per-round state in its reset methods, approximately `GinRummyTablePlugin.cs:700-746`.

### Timeouts, auto-play, and forfeits

`OnTimeout` handles:

- `Starting`: start the game
- `Playing`: turn timeout or Rush expiration
- `Finishing`: auto-finish
- `RoundOver`/`GameOver`: start next round or return to waiting

`GinRummyTablePlugin.cs:94-105`.

Timeout behavior is server-controlled. The header documents:

- 20-second normal turn / 10-second Rush turn
- One 20-second extension per player
- Timeout delta
- Final timeout results in kick/forfeit

Forfeits and leave settlement are documented in the header and implemented around the leave/settlement methods, `GinRummyTablePlugin.cs:16-24` and approximately `650-730`.

Bots are not implemented as a full Gin AI inside this plugin. Server-generated/system players can be represented as bot seats, and timeout logic can advance/finish games, but the rich bot strategies are in the Unity client’s tutorial/local bot code rather than this server plugin.

### Authority

The server is authoritative for:

- Deck order and dealing
- Turn owner
- Draw source
- Card ownership
- Discard legality
- Finish hand membership
- Meld/deadwood analysis
- Knock/gin/undercut scoring
- Timers and forfeits
- Scores, pot, rake, and economy settlement

The client supplies card ordering and requested actions, but the server validates them. It does not trust client-computed meld or score results.

---

## 3. Shuffle/deal fairness

`GinRummyShuffle.cs`:

- Builds a standard 52-card deck, optionally adding jokers 52 and 53: `GinRummyShuffle.cs:16-24`
- Uses `CryptoRng.Shared` from the plugin: `GinRummyTablePlugin.cs:47-49`
- Performs seven riffle passes: `GinRummyShuffle.cs:10,32-55`
- Each pass uses:
  - Random cut near the midpoint
  - Slight left/right bias
  - Random bursts of 1–3 cards
- Deals alternating cards to player 0 and player 1.
- Keeps the remaining cards as the stock.
- Retries up to five times until both hands are “playable”: at least one pair or adjacent same-suit card.
- If five attempts fail, accepts the last deal.

This is not a cryptographically uniform Fisher-Yates shuffle. It is deliberately shaped:

- Riffle simulation rather than uniform permutation
- Slight dealing bias
- “Playable hand” rejection sampling
- Optional jokers

The code comments explicitly describe “soft playability” and the five-attempt retry. Therefore it is not purely fair/random in the strict statistical sense; it intentionally avoids some poor opening hands. There is no explicit seed parameter. Randomness comes from the injected `IRng`, currently `CryptoRng.Shared`.

---

## 4. Table lifecycle

### `TableList`

`DigitoyServer/Game/TableList.cs`:

- Joinability is represented by `JoinableBit`, line 37.
- `Find` considers joinable tables, query flags, and bet range, lines 39-67.
- Non-bot seated players are counted for lobby/in-game statistics, lines 103-123.
- PlayNow selects among compatible joinable tables, otherwise creates a table through handler flow.

### `Table`

`DigitoyServer/Game/Table.cs`:

- Loads and owns persisted `TableState`.
- Rejects invalid/nonexistent table operations, lines 63-81.
- Calls plugin `OnAction`.
- Schedules plugin timer changes when `TimeoutAt` changes: lines 83-112.
- Maintains table members and spectators.
- Removes/release table pointers when a player leaves.
- Broadcasts state deltas and lobby updates.
- Bot seats are treated specially in previews and membership checks, approximately lines 190-255.

### `TablePluginBase`

`DigitoyServer/Game/TablePluginBase.cs`:

- Provides common action entry point: `OnAction`, lines 26-34.
- Provides error/reply/broadcast helpers.
- Provides economy settlement and player/stat update support.
- Provides spectator key/membership logic, approximately lines 95-138.
- Plugin state is saved/restored independently of the common `TableState`.

### Gin-specific lifecycle

- Create: one seat, `Waiting`, joinable.
- Join: second seat, `Starting/ReadyCheck`, 10-second start timer.
- Both Ready: immediate start.
- Disconnect/offline: handler routes leave through the table actor; current table pointer is released.
- Reconnect: repeated table join request for an already seated player becomes `Refresh`; state is rebuilt from persisted table state.
- Spectate: `Params=[-1]` converts a seat action to spectator behavior.
- Private tables are represented through table flags/lobby IDs and the normal create/join flow; there is no separate Gin wire protocol.

---

## 5. Unity client duplication and divergences

Client path:

`C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Table\Scripts`

Duplicated rule logic includes:

- `Utilities/CardUtils.cs:83` — `CalculateDeadwood`
- `Utilities/CardUtils.cs:89` — `FindAllMelds`
- `Utilities/CardUtils.cs:189` — `CanLayoffOnMelds`
- `Actions/GameRules.cs:45-67` — discard/knock/gin UI decisions
- `BotGame/BotGameMeldTracker.cs:17-91` — meld detection and animation tracking
- `BotGame/*BotStrategy.cs` — local bot card selection and knock behavior
- `Flow/Phases/RushTimeoutPhase.cs`
- `Flow/Phases/SlamPhase.cs`
- `Hands/PlayerHand.cs` — client-side action sequencing and presentation

Main divergence:

- The Unity client calculates melds, deadwood, hints, bot decisions, and UI eligibility locally.
- The server independently uses `HandScore`, `HandSorter`, and `RoundScorer`.
- Client logic is therefore advisory/presentation logic; it is not authoritative.
- The server supports server-side Rush timing and scoring, while several Slam/Rush classes in Unity are flow/UI-specific.
- The server’s `BigGin` constant exists but is explicitly unused for normal 10-card hands; client code should be checked for any UI path that presents Big Gin as available.
- Client card order is significant for animation/finish payloads, but server validates that the submitted cards are an actual permutation of the authoritative hand.

The two implementations should be regression-tested against the same hands, especially:

- Joker handling
- Layoff selection
- Straight variant
- Rush tie-breakers
- Knock threshold
- Big Gin visibility
- Finish hand encoding

---

## 6. Code quality notes

`GinRummyTablePlugin.cs` is 746 lines and combines many responsibilities:

- Wire dispatch
- Table lifecycle
- Turn state machine
- Dealing
- Card validation
- Finish protocol
- Timeout handling
- Rush logic
- Scoring orchestration
- Economy/pot/rake settlement
- Player statistics/events
- State serialization
- View masking

Positive aspects:

- Clear state-machine phases.
- Explicit persisted plugin state through `SaveState`/`RestoreState`.
- Server-side validation and masked views.
- Shared scoring logic extracted to `Common/Ginrummy/RoundScorer.cs`.
- Constants and protocol actions are relatively easy to locate.
- Timer behavior is centralized in `OnTimeout`.

Risks/maintenance issues:

- The plugin is still large enough that unrelated concerns are tightly coupled.
- Economy settlement is interleaved with game-state transitions.
- Several transient fields (`_pickedFromDiscard`, `_pickedFromDeck`, `_finishDiscard`, `_extend`) must remain synchronized with `TableState`; persistence bugs could create invalid turn states.
- The client sends a serialized hand/order for finish, creating a relatively complex trust boundary even though the server validates it.
- Rush, standard play, timeout, and forfeit paths share state fields but have materially different scoring and lifecycle semantics.
- Big Gin is represented in shared scoring constants but is not active for the current 10-card deal.
- The shuffle deliberately biases playability, which should be documented as a product rule rather than described simply as “fair shuffle.”
- Local Unity implementations duplicate core rule calculations, creating a future divergence risk between `CardUtils`/client bots and `HandScore`/`RoundScorer` on the server.