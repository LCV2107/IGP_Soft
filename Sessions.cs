using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;

namespace IGP_Soft;

// ---------------------------------------------------------------------------
//  Perfiles de pantallas: qué enlace va en cada zona de cada monitor
// ---------------------------------------------------------------------------

internal sealed class ZoneLayout
{
    public int Zone { get; set; }
    public string Titulo { get; set; } = "";
    public string Url { get; set; } = "";
}

internal sealed class MonitorLayout
{
    public int Number { get; set; }               // 1, 2, 3… según el orden del panel
    public string Device { get; set; } = "";      // \\.\DISPLAY1
    public bool Primary { get; set; }
    public bool Excluded { get; set; }
    public int Zones { get; set; } = 1;
    public List<ZoneLayout> Links { get; set; } = new();
}

internal sealed class LayoutProfile
{
    public string Name { get; set; } = "";
    public string? Browser { get; set; }
    public DateTime Saved { get; set; }
    public List<MonitorLayout> Monitors { get; set; } = new();

    public int LinkCount => Monitors.Where(m => !m.Excluded).Sum(m => m.Links.Count);
}

/// <summary>Perfiles guardados en un JSON junto al ejecutable (portable).
/// «Última sesión» es un perfil más, que el panel actualiza solo.</summary>
internal static class ProfileStore
{
    public const string LastSession = "Última sesión";

    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "IGP_Soft.perfiles.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // tildes legibles en el archivo
    };

    public static List<LayoutProfile> Items { get; private set; } = new();

    /// <summary>Perfiles con nombre (sin «Última sesión»), en orden alfabético.</summary>
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

    /// <summary>Agrega o reemplaza (por nombre) y guarda.</summary>
    public static void Put(LayoutProfile profile)
    {
        profile.Name = profile.Name.Trim();
        Items.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        Items.Add(profile);
        Save();
    }

    public static void Remove(string name)
    {
        if (Items.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0) Save();
    }

    private static void Save()
    {
        try
        {
            // Escritura atómica: un corte de luz no deja el archivo a medias.
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* carpeta de solo lectura: los perfiles no se conservan */ }
    }
}

/// <summary>Qué se abre al arrancar el programa (se guarda en IGP_Soft.config.txt).</summary>
internal static class Startup
{
    public const string Nothing = "";

    /// <summary>"" = nada · «Última sesión» · nombre de un perfil.</summary>
    public static string Profile
    {
        get => Config.Get("alIniciar") ?? Nothing;
        set => Config.Set("alIniciar", value);
    }

    // ---- Iniciar con Windows (solo si el usuario lo activa: entrada en HKCU\...\Run) ----

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
            catch { /* sin permisos: queda como estaba */ }
        }
    }
}
