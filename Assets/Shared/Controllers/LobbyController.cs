// Lobi yer tutucusu (Faz 1 lobisi gelene kadar): Play Now → TablePlayNow → masa sahnesi. Oyun adı bilmez; Game.Current'tan okur.
using DigitoyEngine;
using Digitoy.Ui;

namespace Digitoy
{
    public class LobbyController : Component
    {
        public Button playNowButton;
        public LayoutBox statusText;

        bool _busy;

        protected override void OnEnable()
        {
            if (playNowButton != null) playNowButton.Clicked += OnPlayNow;
            // Oturum yüklenirken masa bulunduysa (reconnect) doğrudan masaya
            if (World.HasTable && !string.IsNullOrEmpty(Game.Current?.tableScene))
                SceneNav.GoToAsync(Game.Current.tableScene).Forget();
        }

        protected override void OnDisable()
        {
            if (playNowButton != null) playNowButton.Clicked -= OnPlayNow;
        }

        void OnPlayNow(Button b, PointerEvent e) => PlayNowAsync().Forget();

        async System.Threading.Tasks.Task PlayNowAsync()
        {
            if (_busy) return;
            _busy = true;
            if (statusText != null) statusText.Text = "Searching…";
            var r = await World.PlayNowAsync();
            _busy = false;
            if (!r.Ok)
            {
                if (statusText != null) statusText.Text = "Error: " + r.Error;
                return;
            }
            await SceneNav.GoToAsync(Game.Current.tableScene);
        }
    }
}
