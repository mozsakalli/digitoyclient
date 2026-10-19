using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using DigitoyEditor;
using DigitoyEngine;
using DigitoyEngine.Editor;

// AppLovin MAX mediation listesi icin ozel cizici (editor uzanti noktasi: DigitoyEditor FieldDrawers, Unity CustomPropertyDrawer
// modeli). Network adi/surumu ELLE YAZILMAZ: AppLovin'in Unity Integration Manager'inin besledigi resmi JSON'dan
// (unity.applovin.com/max/1.0/integration_manager_info) katalog cekilir, Library/AppLovin/networks.json'a onbellenir (cevrimdisi
// calisir), satirlar "Google AdMob — android 25.5.0.0 / ios 13.11.0.0" olarak gorunur; Add ▾ ile eklenir, Update ile guncellenir.
// Artefakt adi (hook'un yazdigi `<x>-adapter`) DownloadUrl'den turetilir. sdkVersionAndroid/Ios alanlari da katalogla karsilastirilir.
public static class AppLovinCatalog
{
    public sealed class Network
    {
        public string Key, DisplayName, Artifact, Android, Ios;
    }

    public static readonly List<Network> Networks = new();
    public static string SdkAndroid = "", SdkIos = "";
    public static string Status { get; private set; } = "catalog not loaded";
    public static bool Loading { get; private set; }
    static bool _triedCache;

    const string Url = "https://unity.applovin.com/max/1.0/integration_manager_info?plugin_version=8.3.1";

    public static Network Find(string artifact)
    {
        foreach (var n in Networks) if (string.Equals(n.Artifact, artifact, StringComparison.OrdinalIgnoreCase)) return n;
        return null;
    }

    // Ilk dokunusta onbellekten yukle; 1 gunden eskiyse (ya da yoksa) arka planda tazele.
    public static void Ensure(string libraryPath)
    {
        if (_triedCache || string.IsNullOrEmpty(libraryPath)) return;
        _triedCache = true;
        string path = CachePath(libraryPath);
        bool stale = true;
        if (File.Exists(path))
        {
            try
            {
                Parse(File.ReadAllText(path));
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                Status = Networks.Count + " networks (cache " + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm") + ")";
                stale = age.TotalHours > 24;
            }
            catch (Exception e) { Status = "cache unreadable: " + e.Message; }
        }
        if (stale) Refresh(libraryPath);
    }

    public static void Refresh(string libraryPath)
    {
        if (Loading) return;
        Loading = true;
        Status = Networks.Count > 0 ? Status + " — refreshing…" : "downloading catalog…";
        _ = FetchAsync(libraryPath);
    }

    static async Task FetchAsync(string libraryPath)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            string json = await http.GetStringAsync(Url);
            Parse(json);
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath(libraryPath)));
            File.WriteAllText(CachePath(libraryPath), json);
            Status = Networks.Count + " networks (updated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")";
        }
        catch (Exception e)
        {
            Status = (Networks.Count > 0 ? Networks.Count + " networks (cache) — " : "") + "refresh failed: " + e.Message;
        }
        finally { Loading = false; }
    }

    static string CachePath(string libraryPath) => Path.Combine(libraryPath, "AppLovin", "networks.json");

    static void Parse(string json)
    {
        var j = Json.Parse(json);
        var list = new List<Network>();
        var med = j["MediatedNetworks"];
        for (int i = 0; i < (med?.Count ?? 0); i++)
        {
            var n = med[i];
            var lv = n["LatestVersions"];
            string android = lv?.GetString("Android", "") ?? "", ios = lv?.GetString("Ios", "") ?? "";
            string artifact = ArtifactOf(n.GetString("DownloadUrl", ""), n.GetString("Name", ""));
            if (artifact.Length == 0) continue;
            list.Add(new Network { Key = n.GetString("Name", ""), DisplayName = n.GetString("DisplayName", artifact), Artifact = artifact, Android = android, Ios = ios });
        }
        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        Networks.Clear();
        Networks.AddRange(list);
        var sdk = j["AppLovinMax"]?["LatestVersions"];
        SdkAndroid = sdk?.GetString("Android", "") ?? "";
        SdkIos = sdk?.GetString("Ios", "") ?? "";
    }

    // ".../com/applovin/mediation/<artifact>-adapter/..." -> "<artifact>"; yoksa Name'den (ADMOB_NETWORK -> admob) kaba tahmin.
    static string ArtifactOf(string url, string name)
    {
        const string marker = "/com/applovin/mediation/";
        int i = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            string rest = url.Substring(i + marker.Length);
            int slash = rest.IndexOf('/');
            if (slash > 0) rest = rest.Substring(0, slash);
            if (rest.EndsWith("-adapter", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(0, rest.Length - 8);
            return rest.ToLowerInvariant();
        }
        return name.Replace("_NETWORK", "").Replace("_MEDIATE", "").Replace("_BIDDING", "").ToLowerInvariant();
    }
}

[CustomFieldDrawer(typeof(AppLovin.Config), "mediation")]
public sealed class AppLovinMediationDrawer : FieldDrawer
{
    string[] _addItems = Array.Empty<string>();
    readonly List<AppLovinCatalog.Network> _addable = new();

    public override float Measure(FieldDrawerContext c)
    {
        AppLovinCatalog.Ensure(c.LibraryPath);
        var list = c.Value as List<AppLovin.Adapter>;
        return RowH * (2 + (list?.Count ?? 0));
    }

    public override bool Draw(FieldDrawerContext c, ref float y, float right)
    {
        AppLovinCatalog.Ensure(c.LibraryPath);
        var list = c.Value as List<AppLovin.Adapter>;
        if (list == null) { c.SetValue(list = new List<AppLovin.Adapter>()); }
        bool changed = false;
        float vx = c.ValueX;

        // Baslik: etiket + katalog durumu + Refresh
        EditorUi.Label(new Rect(c.LabelX, y, Math.Max(20, c.LabelW - c.Indent * 12), 18), "Mediation [" + list.Count + "]");
        EditorUi.Text(new Rect(vx, y, Math.Max(20, right - vx - 70), 18), AppLovinCatalog.Status, small: true, color: EditorUi.DimColor);
        if (Gui.Button(new Rect(right - 64, y, 64, 18), AppLovinCatalog.Loading ? "…" : "Refresh") && !AppLovinCatalog.Loading)
            AppLovinCatalog.Refresh(c.LibraryPath);
        y += RowH;

        // Satirlar: ad — surumler; guncel degilse Update; sil
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            var cat = AppLovinCatalog.Find(a.network);
            string name = !string.IsNullOrEmpty(a.displayName) ? a.displayName : cat?.DisplayName ?? a.network;
            bool outdated = cat != null && (cat.Android != a.android || cat.Ios != a.ios);
            string text = name + "  —  android " + a.android + "  /  ios " + a.ios + (cat == null ? "  (not in catalog)" : "");
            EditorUi.Text(new Rect(vx, y, Math.Max(20, right - vx - 130), 18), text, color: cat == null ? EditorUi.WarnColor : EditorUi.LabelColor);
            if (outdated && Gui.Button(new Rect(right - 124, y, 92, 18), "Update"))
            {
                a.android = cat.Android; a.ios = cat.Ios; a.displayName = cat.DisplayName;
                changed = true;
            }
            if (Gui.Button(new Rect(right - 26, y, 26, 18), "x"))
            {
                list.RemoveAt(i);
                changed = true;
                y += RowH;
                break;
            }
            y += RowH;
        }

        // Ekle: katalogdan secim (zaten ekli olanlar listede yok)
        _addable.Clear();
        foreach (var n in AppLovinCatalog.Networks)
        {
            bool present = false;
            foreach (var a in list) if (string.Equals(a.network, n.Artifact, StringComparison.OrdinalIgnoreCase)) { present = true; break; }
            if (!present) _addable.Add(n);
        }
        if (_addItems.Length != _addable.Count + 1) _addItems = new string[_addable.Count + 1];
        _addItems[0] = AppLovinCatalog.Networks.Count == 0 ? "(no catalog — Refresh)" : "Add network…";
        for (int i = 0; i < _addable.Count; i++)
            _addItems[i + 1] = _addable[i].DisplayName + "  (android " + _addable[i].Android + " / ios " + _addable[i].Ios + ")";
        int pick = Gui.ComboBox(new Rect(vx, y, Math.Min(420, Math.Max(120, right - vx)), 18), 0, _addItems);
        if (pick > 0 && pick <= _addable.Count)
        {
            var n = _addable[pick - 1];
            list.Add(new AppLovin.Adapter { network = n.Artifact, displayName = n.DisplayName, android = n.Android, ios = n.Ios });
            changed = true;
        }
        y += RowH;
        return changed;
    }
}

// sdkVersionAndroid / sdkVersionIos: deger + katalogdaki guncel surumle karsilastirma (Update).
public abstract class AppLovinSdkVersionDrawer : FieldDrawer
{
    protected abstract string Latest { get; }

    public override float Measure(FieldDrawerContext c) { AppLovinCatalog.Ensure(c.LibraryPath); return RowH; }

    public override bool Draw(FieldDrawerContext c, ref float y, float right)
    {
        AppLovinCatalog.Ensure(c.LibraryPath);
        string cur = c.Value as string ?? "";
        EditorUi.Label(new Rect(c.LabelX, y, Math.Max(20, c.LabelW - c.Indent * 12), 18), c.Field.Name);
        bool outdated = Latest.Length > 0 && Latest != cur;
        EditorUi.Text(new Rect(c.ValueX, y, Math.Max(20, right - c.ValueX - 130), 18), cur + (outdated ? "   (latest " + Latest + ")" : ""),
            color: outdated ? EditorUi.WarnColor : EditorUi.LabelColor);
        bool changed = false;
        if (outdated && Gui.Button(new Rect(right - 124, y, 124, 18), "Update to " + Latest)) { c.SetValue(Latest); changed = true; }
        y += RowH;
        return changed;
    }
}

[CustomFieldDrawer(typeof(AppLovin.Config), "sdkVersionAndroid")]
public sealed class AppLovinSdkAndroidDrawer : AppLovinSdkVersionDrawer { protected override string Latest => AppLovinCatalog.SdkAndroid; }

[CustomFieldDrawer(typeof(AppLovin.Config), "sdkVersionIos")]
public sealed class AppLovinSdkIosDrawer : AppLovinSdkVersionDrawer { protected override string Latest => AppLovinCatalog.SdkIos; }
