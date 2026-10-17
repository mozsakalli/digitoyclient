// Unity LoginController adaptasyonu (Guest alt kümesi). Yerleşim bilmez; sahnedeki GO'lar alanlardan bağlanır.
// Akış: Start → settings (force update / maintenance / policy) → LastAuthType varsa oto-login, yoksa form → Guest →
// progress (Auth/Loading/Ready) → hata: form geri + errorText → başarı → Game.Current.lobbyScene. FB/Apple butonları stub (Faz 5).
using System.Threading.Tasks;
using Digitoy.Ui;
using DigitoyEngine;
using M;

namespace Digitoy.Controllers
{
    public sealed class LoginFlow : Component
    {
        public GameObject loginForm;
        public GameObject agreement;
        public GameObject progressRoot;
        public ProgressRenderer progress;
        public LayoutBox versionText;
        public LayoutBox errorText;
        public Button guestButton;
        public Button facebookButton;
        public Button appleButton;
        public bool autoLoginLastType = true;
        public GameDefinition game;
        public NetworkSettings network;
        public string appVersion = "0.1.0";

        private const float StepSettings = 0.15f, StepAuth = 0.55f, StepLoad = 0.9f;
        private TweenHandle _progressTween;
        private bool _busy;

        protected override void Start()
        {
            AppBoot.Ensure(game, network, appVersion);
            if (versionText != null) versionText.Text = Platform.AppVersion;
            if (guestButton != null) guestButton.Clicked += (_, _) => LoginAsync(LoginType.Guest).Forget();
            if (facebookButton != null) facebookButton.Clicked += (_, _) => Debug.LogWarning("Facebook login: Faz 5");
            if (appleButton != null) appleButton.Clicked += (_, _) => Debug.LogWarning("Apple login: Faz 5");
            if (errorText != null) errorText.Text = "";
            ProgressAsync().Forget();
        }

        private async Task ProgressAsync()
        {
            ShowProgress();
            SetProgress(0f, 0f);
            var settingsTask = Config.LoadGameSettingsAsync(destroyCancellationToken);
            SetProgress(StepSettings, 1.5f);
            await settingsTask;
            await AnimateProgress(StepSettings, 0.1f);

            var settings = Config.Settings;
            if (settings != null)
            {
                if (settings.ForceUpdate())
                {
                    // Mağaza yönlendirme (OpenURL) motor B4 ile; şimdilik form kapalı, hata metni.
                    Debug.LogWarning("Force update required");
                    ShowError("error.force_update");
                    return;
                }
                if (settings.maintenance)
                {
                    Debug.LogWarning("Maintenance mode, retrying in 5s");
                    ShowError("error.maintenance");
                    await Wait.Seconds(5f, destroyCancellationToken);
                    ProgressAsync().Forget();
                    return;
                }
                // Politika değişim diyaloğu Faz 1 kabuk (Dialog); şimdilik log.
                if (settings.IsPrivacyChanged()) Debug.Log($"Privacy policy changed → v{settings.policy_version}");
            }

            LoginType? last = autoLoginLastType ? PlayerPrefsService.I.LastAuthType.Value : null;
            if (last == LoginType.Guest) await LoginAsync(LoginType.Guest);
            else ShowForm();
        }

        private async Task LoginAsync(LoginType type)
        {
            if (_busy) return;
            _busy = true;
            if (errorText != null) errorText.Text = "";
            ShowProgress();

            var authTask = Server.LoginAsync(type);
            SetProgress(StepAuth, 1.5f);
            var result = await authTask;
            if (!result.Ok)
            {
                _busy = false;
                Debug.LogWarning($"Login failed: {result.Error}");
                ShowForm();
                if (errorText != null) errorText.Text = $"error.{result.Error}";
                return;
            }

            await AnimateProgress(StepLoad, 0.3f);
            await AnimateProgress(1f, 0.2f);
            _busy = false;
            var ok = await SceneNav.GoToAsync(Game.Current.lobbyScene);
            if (!ok) ShowForm();
        }

        private void ShowError(string locKey)
        {
            loginForm?.SetActive(false);
            agreement?.SetActive(false);
            progressRoot?.SetActive(false);
            if (errorText != null) errorText.Text = locKey;
        }

        private void ShowForm()
        {
            loginForm?.SetActive(true);
            agreement?.SetActive(true);
            progressRoot?.SetActive(false);
        }

        private void ShowProgress()
        {
            loginForm?.SetActive(false);
            agreement?.SetActive(false);
            progressRoot?.SetActive(true);
        }

        private void SetProgress(float v, float dur)
        {
            if (progress == null) return;
            _progressTween.Cancel();
            if (dur <= 0f) { progress.Value = v; return; }
            _progressTween = progress.TweenField("Value", v, dur).Ease(Ease.OutCubic);
        }

        private async Task AnimateProgress(float v, float dur)
        {
            SetProgress(v, dur);
            if (dur > 0f) await Wait.Seconds(dur, destroyCancellationToken);
        }

        protected override void OnDisable() => _progressTween.Cancel();
    }
}
