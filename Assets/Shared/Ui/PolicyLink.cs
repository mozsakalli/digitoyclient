// Agreement rich text'indeki <link=privacy|terms> parçalarına tıklama. Aynı GO'daki LayoutBox.LinkAt(e) link id'sini verir.
// URL önce Config.Settings (settings JSON), yoksa alan; OpenURL motor B4 (native shim) gelince açılır — şimdilik log.
using DigitoyEngine;

namespace Digitoy.Ui
{
    public sealed class PolicyLink : Component
    {
        public string privacyUrl = "https://bro3.com/privacy";
        public string termsUrl = "https://bro3.com/privacy/#terms";

        protected override void OnPointerClick(PointerEvent e)
        {
            if (e.dragged) return;
            var box = GetComponent<LayoutBox>();
            var id = box?.LinkAt(e);
            if (id == null) return;
            var s = Config.Settings;
            var url = id == "privacy" ? (s?.privacy_url ?? privacyUrl)
                    : id == "terms" ? (s?.terms_url ?? termsUrl)
                    : null;
            if (url == null) return;
            Debug.Log($"PolicyLink: {id} → {url} (OpenURL: engine gap B4)");
            e.Use();
        }
    }
}
