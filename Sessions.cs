using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;

namespace IGP_Soft;

internal sealed class ZoneLayout
{
    public int Zone { get; set; }
    public string Titulo { get; set; } = "";
    public string Url { get; set; } = "";
}

internal sealed class MonitorLayout
{
    public int Number { get; set; }
    public string Device { get; set; } = "";
    public bool Primary { get; set; }
    public bool Excluded { get; set; }
    public int Zones { get; set; } = 1;
    public int Cols { get; set; }
    public int Rows { get; set; }
    public List<ZoneSpec>? Layout { get; set; }
    public List<ZoneLayout> Links { get; set; } = new();
}

internal sealed class LayoutProfile
{
    public string Name { get; set; } = "";
    public string? Browser { get; set; }
    public DateTime Saved { get; set; }
    public List<MonitorLayout> Monitors { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public int LinkCount => Monitors.Where(m => !m.Excluded).Sum(m => m.Links.Count);
}

internal static class ProfileStore
{
    public const string LastSession = "Última sesión";

    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "IGP_Soft.perfiles.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<LayoutProfile> Items { get; private set; } = new();

    public static IEnumerable<LayoutProfile> Named =>
        Items.Where(p => !IsLastSession(p.Name)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);

    public static bool IsLastSession(string name) => string.Equals(name, LastSession, StringComparison.OrdinalIgnoreCase);

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            Items = JsonSerializer.Deserialize<List<LayoutProfile>>(File.ReadAllText(FilePath), Json) ?? new();
            Items.RemoveAll(p => string.IsNullOrWhiteSpace(p.Name));
        }
        catch { Items = new(); }
    }

    public static LayoutProfile? Get(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : Items.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static void Put(LayoutProfile profile)
    {
        profile.Name = profile.Name.Trim();
        Items.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        Items.Add(profile);
        Save();
    }

    public static void RewriteUrls(IReadOnlyDictionary<string, string> map)
    {
        bool changed = false;
        foreach (ZoneLayout z in Items.SelectMany(p => p.Monitors).SelectMany(m => m.Links))
            if (map.TryGetValue(z.Url, out string? to)) { z.Url = to; changed = true; }
        if (changed) Save();
    }

    public static void Remove(string name)
    {
        if (Items.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0) Save();
    }

    private static void Save()
    {
        try
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { }
    }
}

internal static class Startup
{
    public const string Nothing = "";

    public static string Profile
    {
        get => Config.Get("alIniciar") ?? Nothing;
        set => Config.Set("alIniciar", value);
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "IGP_Soft";

    public static bool WithWindows
    {
        get
        {
            try
            {
                using RegistryKey? k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(RunValue) is string s && s.Length > 0;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --inicio-windows");
                else k.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            catch { }
        }
    }
}
