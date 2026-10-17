// Sahne geçişi: yeni sahne aktif sahnenin yerine yüklenir (replace). Geçiş efekti Faz 1 sonu.
using System.Threading.Tasks;
using DigitoyEngine;

namespace Digitoy
{
    public static class SceneNav
    {
        public static async Task<bool> GoToAsync(string sceneKey)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                Debug.LogError("SceneNav: boş sahne anahtarı");
                return false;
            }
            var scene = Scene.Active;
            var op = SceneLoader.LoadAsync(sceneKey, scene.Assets, scene.Catalog, null, replace: true);
            var ok = await op.Task;
            if (!ok)
            {
                Debug.LogError($"SceneNav: {sceneKey} yüklenemedi: {op.Error}");
                return false;
            }
            Debug.Log($"SceneNav: {sceneKey} root={op.Root?.name}");
            return true;
        }
    }
}
