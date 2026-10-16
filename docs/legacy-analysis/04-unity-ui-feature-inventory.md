## 1. Scenes

Six Unity scenes exist; `EditorBuildSettings.asset` contains no `path:` entries, so none are currently listed in the build settings:

- `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Scenes\Entry\EntryScene.unity` — application entry/bootstrap scene.
- `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Scenes\Login\LoginScene.unity` — login/account flow.
- `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Scenes\Lobby\LobbyScene.unity` — lobby, table selection, social and store UI.
- `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Scenes\TableScene.unity` — live Gin Rummy table/gameplay.
- `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Scenes\FX.unity` — reusable table/tutorial/claim effects and particle objects.
- `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Scenes\LobbyScene1.unity` — alternate/legacy lobby scene.

Source: `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Scenes\`; build settings: `C:\work\digitoygames\ginrummyextra.unity\ProjectSettings\EditorBuildSettings.asset`.

## 2. Feature inventory

There are 105 C# files under the requested `Dialogs`, `Lobby`, `Bonuses`, `Login`, and `Shared` areas. Main feature groups and representative files are:

- Daily bonus / claim rewards — `DailyBonusView.cs`, `DailyBonusDayItem.cs`, `ClaimView.cs`, `ClaimRewardPrompt.cs`
- Missions — `MissionsView.cs`, `MissionItemView.cs`, `MissionsButtonView.cs`
- Quests — `QuestsView.cs`, `QuestItemView.cs`, `QuestButtonView.cs`
- Season pass / loyalty / level progression — `SeasonPassView.cs`, `SeasonPassItemView.cs`, `LoyaltyView.cs`, `LoyaltyTierUpDialog.cs`, `LevelProgressView.cs`, `LevelTooltipDialog.cs`
- Shop / offers / products — `StoreView.cs`, `StoreItemView.cs`, `OfferDialog.cs`, `OfferCard.cs`, `OfferCatalog.cs`, `ProductIcon.cs`
- IAP and purchase animation — `IAPManager.cs`, `PurchaseAnimationDialog.cs`, `PurchaseAnimate.cs`
- Ads / watch-and-collect — `WatchAndCollectView.cs`, `WatchAndCollectItemView.cs`
- Leaderboards — `LeaderBoardTabView.cs`, `LeaderboardSocialTab.cs`, `LeaderBoardRow.cs`, `LeaderBoardStickyRow.cs`
- Friends / social — `FriendsTabView.cs`, `FriendsTabGroup.cs`, `FriendsItemView.cs`, `SocialManager.cs`
- Private tables — `CreatePrivateView.cs`, `JoinPrivateTableView.cs`, `PrivateTableCard.cs`, `PrivateTableStoreView.cs`
- Table list / lobby — `TableListView.cs`, `TableListItemView.cs`, `TableListItemSeatView.cs`, `LobbyManager.cs`, `LobbyCardsManager.cs`
- Lobby cards and play-now flow — `LobbyCard.cs`, `LobbyPlayNowCard.cs`, `PlayView.cs`, `PlayNowHintDialog.cs`, `LobbiesCardSlider.cs`
- Inbox / rewards / notifications — `InboxView.cs`, `InboxTab.cs`, `InboxLevelUpReward.cs`, `InboxLeaderboardReward.cs`, `InboxReferralReward.cs`, `NotificationBadge.cs`
- Referral / gifts — `Referral.cs`, `RewardItem.cs`, `RewardsTabView.cs`
- Jackpot — `JackpotTooltip.cs`
- Mini games — `MiniGamesDialog.cs`, with localized `Pick_MiniGame` and `Race_MiniGame` data
- Profile / avatar / nickname — `ProfileView.cs`, `ProfileAvatarPanelView.cs`, `ProfileAvatarItemView.cs`, `ProfileNickChange.cs`
- Account linking — `LinkAccountView.cs`
- Settings / privacy / account deletion-related flow — `PrivacyView.cs`, `PushNotificationsView.cs`, `RateView.cs`
- Support / help — `SupportTabView.cs`, `HelpShiftManager.cs`
- How-to-play / onboarding — `HowToPlayView.cs`, `LobbyOnboardingHintView.cs`, `LobbyUnlockHint.cs`
- Connection errors / loading — `ConnectionError.cs`, `ConnectionErrorView.cs`, `LoadingSpinner.cs`, `SpinnerLoader.cs`
- Table chat / PM / invite — `TableChatStore.cs`, `TableChatStoreItem.cs`, `PmView.cs`, `TableInviteView.cs`, `TableInviteItemView.cs`
- Table social UI — table gift, emoji/sticker/chat-related code under `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Table\Scripts\TableSocial\`
- Table sorting / score selection — `TableAutoSortButton.cs`, `AutoSortStore.cs`, `ScoreSelector.cs`
- Login / language selection — login scripts under `...\Assets\GinRummyExtra\Login\`; language selector assets under `...\Assets\GinRummyExtra\Scenes\Lobby\Components\LanguageSelect\`

## 3. Unity and third-party dependencies

Counts below are matching C# source lines/types, not unique files; broad Unity UI counts include many standard UI references.

| Dependency/API | Matches |
|---|---:|
| DOTween / `DG.Tweening` / `Sequence` | 307 |
| Odin Inspector attributes/types | 398 |
| UniTask / `async` | 273 |
| Addressables | 9 |
| TextMeshPro / TMP types | 54 |
| Unity UI types | 901 |
| ParticleSystem / Coffee UIParticle | 100 |
| Animator / AnimationClip | 39 |
| Adjust | 25 |
| Helpshift | 20 |
| Facebook SDK | 59 |
| Firebase | 1 |
| MaxSdk | 1 |
| Unity IAP | 1 |
| SRDebugger | 1 |
| CameraAnimator references | 23 |
| Spine | 0 |
| New Input System | 0 |
| RenderTexture | 0 |
| VideoPlayer | 0 |

Notable DOTween APIs found:

- `Sequence`
- `DOScale`, `DOFade`
- `DOAnchorPos`, `DOAnchorPosX`, `DOAnchorPosY`
- `DOLocalMove`, `DOLocalMoveY`
- `DOLocalRotate`, `DOLocalRotateQuaternion`
- `DOLocalJump`
- `DOShakePosition`, `DOShakeRotation`
- `DOVirtual`
- `DOSizeDelta`
- `DOColor`
- `DOFieldOfView`
- `DOFillAmount`
- `DOMove`
- `DOPath`, `DOLocalPath`
- `DOPunchScale`

Unity UI usage includes `Canvas`, `ScrollRect`, layout groups, `Mask`, `RectMask2D`, `Image`, `Image.fillAmount`, `Toggle`, `Slider`, and `TMP_InputField`.

Primary source scope: `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\**\*.cs`.

## 4. Asset inventory

The requested PowerShell recursive inventory covered:

- `Assets\GinRummyExtra`
- `Assets\UI`
- `Assets\Resources`
- `Assets\StreamingAssets`
- `Assets\CartoonVFX9X`
- `Assets\Epic Toon FX`

| Extension | Count | Size |
|---|---:|---:|
| `.png` | 225 | 76.57 MB |
| `.jpg` | 17 | 8.22 MB |
| `.jpeg` | 0 | 0 |
| `.psd` | 266 | 31.23 MB |
| `.spriteatlas` | 0 | 0 |
| `.ttf` | 0 | 0 |
| `.otf` | 0 | 0 |
| `.asset` | 97 | 0.28 MB |
| `.wav` | 30 | 47.32 MB |
| `.mp3` | 33 | 9.51 MB |
| `.ogg` | 0 | 0 |
| `.prefab` | 272 | 22.34 MB |
| `.anim` | 39 | 4.58 MB |
| `.controller` | 39 | 0.08 MB |
| `.shader` | 6 | 0.03 MB |
| `.mp4` | 1 | 0.33 MB |

The `.asset` count includes Unity assets and localization assets; it is not exclusively TMP assets.

## 5. Localization

Localization is stored as Unity YAML `.asset` files, not JSON/CSV:

`C:\work\digitoygames\ginrummyextra.unity\Assets\Resources\Locales\`

There are 27 locale asset files, including:

`Albums`, `AppError`, `ClaimBonus`, `Connection`, `DailyBonus`, `Debug`, `Errors`, `Formatters`, `GameItems`, `HowToPlay`, `Inbox`, `LobbyScene`, `LoginScene`, `Missions`, `Notifications`, `Pick_MiniGame`, `Profile`, `Quests`, `Race_MiniGame`, `Settings`, `Social`, `Store`, `TableList`, `TableScene`, `Tutorials`, and `WatchAndCollect`.

Entries use named language fields such as `English` and `French`; the inspected assets expose **2 languages**. Example: `C:\work\digitoygames\ginrummyextra.unity\Assets\Resources\Locales\Albums.asset:17-27`.

## 6. Hard-to-port items

- **Animated gifts:** `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Table\Scripts\TableSocial\TableGifts\Animated\AnimatedGift.cs` uses Unity object/component lifecycle, animation state and tween-driven gift presentation.
- **UIParticle:** `UIParticleSpawner.cs`, `UIParticleImageAnimator.cs`, and `UIParticleAttractorEvents.cs` depend on Coffee UI Particle behavior and particle emission/rendering. Source: `...\Assets\GinRummyExtra\Shared\Utils\`.
- **Custom UI mesh/border:** `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Scenes\UILineBorder.cs` derives from Unity UI rendering/mesh behavior and will require custom geometry generation.
- **Camera animation:** `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Table\Scripts\Utilities\CameraAnimator.cs` animates camera state/FOV and relies on Unity camera transforms plus DOTween.
- **Masks/stencil:** widespread `Mask`/`RectMask2D` use requires equivalent clipping, stencil, and nested-scroll behavior in the custom renderer.
- **Grayscale shader:** `C:\work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Shared\Utils\UIGrayscale.cs` controls a shader/material-based UI grayscale effect.
- **Custom shaders/materials:** six non-TextMesh-Pro shaders were found, including the loyalty holographic shaders:
  - `...\Assets\GinRummyExtra\Dialogs\Loyalty\Shader\HoloCard_BuiltIn.shader`
  - `...\Assets\GinRummyExtra\Dialogs\Loyalty\Shader\HoloCard_BuiltIn_Effect.shader`
- **Video:** one `.mp4` exists, although no `VideoPlayer` reference was found in the game C# scope; playback integration still needs replacement if used by prefabs.
- **3D/rendering assets:** prefabs, animation controllers, camera effects, particles and Unity materials require replacement by the target engine’s scene/render pipeline.
- **Scroll/pooling UI:** lobby cards, table lists, leaderboards, inbox and social lists use `ScrollRect`, layout groups, sliders and reusable item views; equivalent virtualization/pooling must be implemented rather than directly porting Unity layout behavior.
