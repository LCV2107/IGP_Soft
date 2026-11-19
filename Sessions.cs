using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;

namespace IGP_Soft;

internal sealed class ZoneLayout
{
    public int Zone { get; set; }
    public string Titulo { get; set; } = "";
    public string Url { get; set; } = "";
    public List<LinkDef>? Pestanas { get; set; }
    public int Activa { get; set; }
}

internal sealed class MonitorLayout
{
    public int Number { get; set; }
    public string Device { get; set; } = "";
    public string? Hardware { get; set; }
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
    public string? Carpeta { get; set; }
    public DateTime Saved { get; set; }
    public List<MonitorLayout> Monitors { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public int LinkCount => Monitors.Where(m => !m.Excluded).Sum(m => m.Links.Count);
}

internal static class ProfileStore
{
    public const string LastSession = "Última sesión";

    public static readonly string FilePath = Path.Combine(AppPaths.DataDir, "IGP_Soft.perfiles.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<LayoutProfile> Items { get; private set; } = new();

    public static Action<List<LayoutProfile>>? Remote { get; set; }

    public static void SetFromRemote(IEnumerable<LayoutProfile> items) => Items = items.ToList();

    public static void ReplaceAll(IEnumerable<LayoutProfile> items)
    {
        Items = items.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        Save();
    }

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
        if (IsLastSession(profile.Name)) profile.Carpeta = null;
        else profile.Carpeta ??= Get(profile.Name)?.Carpeta;
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

    public static void Guardar() => Save();

    private static void Save()
    {
        if (Remote is not null)
        {
            Remote(Items.ToList());
            return;
        }
        try
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { }
    }
}

internal static class CarpetasPerfiles
{
    public const int MaxNombre = 60;
    private const string Clave = "perfiles.carpetas";

    private static List<string>? _propias;

    public static Action<List<string>> Persistir { get; set; } =
        l => Config.Set(Clave, JsonSerializer.Serialize(l, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

    private static List<string> Propias => _propias ??= Leer();

    private static List<string> Leer()
    {
        try
        {
            if (Config.Get(Clave) is { Length: > 0 } v && JsonSerializer.Deserialize<List<string>>(v) is { } l)
                return Limpias(l);
        }
        catch { }
        return new();
    }

    public static void Fijar(IEnumerable<string> carpetas) => _propias = Limpias(carpetas);

    private static List<string> Limpias(IEnumerable<string?> l) =>
        l.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

    private static void GuardarPropias() => Persistir(Propias.ToList());

    public static IReadOnlyList<string> Todas => Limpias(Propias.Concat(ProfileStore.Items.Where(p => !ProfileStore.IsLastSession(p.Name)).Select(p => p.Carpeta)))
        .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();

    private static string? Existente(string nombre) =>
        Todas.FirstOrDefault(c => c.Equals(nombre.Trim(), StringComparison.CurrentCultureIgnoreCase));

    private static bool Igual(string? a, string? b) => a is not null && b is not null && a.Equals(b, StringComparison.CurrentCultureIgnoreCase);

    public static string? Validar(string nombre, string? actual = null)
    {
        nombre = nombre.Trim();
        if (nombre.Length == 0) return "Escribe un nombre para la carpeta.";
        if (nombre.Length > MaxNombre) return $"El nombre puede tener hasta {MaxNombre} caracteres.";
        if (Existente(nombre) is { } e && !Igual(e, actual)) return $"Ya existe la carpeta «{e}».";
        return null;
    }

    public static bool Crear(string nombre)
    {
        if (Validar(nombre) is not null) return false;
        Propias.Add(nombre.Trim());
        GuardarPropias();
        return true;
    }

    public static bool Renombrar(string actual, string nuevo)
    {
        if (Existente(actual) is not { } viejo || Validar(nuevo, viejo) is not null) return false;
        nuevo = nuevo.Trim();
        Propias.RemoveAll(c => Igual(c, viejo));
        Propias.Add(nuevo);
        GuardarPropias();
        foreach (LayoutProfile p in ProfileStore.Items.Where(p => Igual(p.Carpeta, viejo))) p.Carpeta = nuevo;
        ProfileStore.Guardar();
        return true;
    }

    public static void Eliminar(string nombre)
    {
        if (Existente(nombre) is not { } c) return;
        Propias.RemoveAll(x => Igual(x, c));
        GuardarPropias();
        bool cambio = false;
        foreach (LayoutProfile p in ProfileStore.Items.Where(p => Igual(p.Carpeta, c)))
        {
            p.Carpeta = null;
            cambio = true;
        }
        if (cambio) ProfileStore.Guardar();
    }

    public static void Mover(IEnumerable<string> perfiles, string? carpeta)
    {
        string? destino = carpeta is null ? null : Existente(carpeta);
        if (carpeta is not null && destino is null) return;
        if (destino is not null && !Propias.Any(c => Igual(c, destino)))
        {
            Propias.Add(destino);
            GuardarPropias();
        }
        bool cambio = false;
        foreach (string nombre in perfiles)
            if (ProfileStore.Get(nombre) is { } p && !ProfileStore.IsLastSession(p.Name) && !string.Equals(p.Carpeta, destino, StringComparison.CurrentCulture))
            {
                p.Carpeta = destino;
                cambio = true;
            }
        if (cambio) ProfileStore.Guardar();
    }

    public static int Cantidad(string carpeta) =>
        ProfileStore.Items.Count(p => !ProfileStore.IsLastSession(p.Name) && Igual(p.Carpeta, carpeta));
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
