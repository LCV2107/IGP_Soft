using System.Text.Json;

namespace IGP_Soft;

internal static class GaleriaCarpetas
{
    public const string Archivo = "IGP_Soft.galeria.json";
    public const int MaxNombre = 60;

    private static string? _ruta;
    private static Datos? _datos;

    public static string Ruta
    {
        get => _ruta ?? Path.Combine(AppPaths.DataDir, Archivo);
        set
        {
            _ruta = value;
            _datos = null;
        }
    }

    private sealed class Datos
    {
        public List<string> Carpetas { get; set; } = new();
        public Dictionary<string, string> Archivos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Datos D => _datos ??= Leer();

    private static Datos Leer()
    {
        try
        {
            if (File.Exists(Ruta) && JsonSerializer.Deserialize<Datos>(File.ReadAllText(Ruta)) is { } d)
            {
                d.Archivos = new Dictionary<string, string>(d.Archivos ?? new(), StringComparer.OrdinalIgnoreCase);
                d.Carpetas = (d.Carpetas ?? new()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach (string k in d.Archivos.Where(kv => !d.Carpetas.Contains(kv.Value, StringComparer.CurrentCultureIgnoreCase)).Select(kv => kv.Key).ToList())
                    d.Archivos.Remove(k);
                return d;
            }
        }
        catch { }
        return new Datos();
    }

    private static void Guardar()
    {
        try
        {
            string tmp = Ruta + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(D, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, Ruta, overwrite: true);
        }
        catch { }
    }

    public static void Recargar() => _datos = null;

    public static IReadOnlyList<string> Carpetas => D.Carpetas.OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static string? CarpetaDe(string rel) => D.Archivos.TryGetValue(Clave(rel), out string? c) ? c : null;

    private static string Clave(string rel) => rel.Replace('/', '\\');

    private static string? Existente(string nombre) => D.Carpetas.FirstOrDefault(c => c.Equals(nombre, StringComparison.CurrentCultureIgnoreCase));

    public static string? Validar(string nombre, string? actual = null)
    {
        nombre = nombre.Trim();
        if (nombre.Length == 0) return "Escribe un nombre para la carpeta.";
        if (nombre.Length > MaxNombre) return $"El nombre puede tener hasta {MaxNombre} caracteres.";
        if (Existente(nombre) is { } e && !e.Equals(actual, StringComparison.CurrentCultureIgnoreCase)) return $"Ya existe la carpeta «{e}».";
        return null;
    }

    public static bool Crear(string nombre)
    {
        if (Validar(nombre) is not null) return false;
        D.Carpetas.Add(nombre.Trim());
        Guardar();
        return true;
    }

    public static bool Renombrar(string actual, string nuevo)
    {
        if (Existente(actual) is not { } viejo || Validar(nuevo, viejo) is not null) return false;
        nuevo = nuevo.Trim();
        D.Carpetas[D.Carpetas.IndexOf(viejo)] = nuevo;
        foreach (string k in D.Archivos.Where(kv => kv.Value == viejo).Select(kv => kv.Key).ToList()) D.Archivos[k] = nuevo;
        Guardar();
        return true;
    }

    public static void Eliminar(string nombre)
    {
        if (Existente(nombre) is not { } c) return;
        D.Carpetas.Remove(c);
        foreach (string k in D.Archivos.Where(kv => kv.Value == c).Select(kv => kv.Key).ToList()) D.Archivos.Remove(k);
        Guardar();
    }

    public static void Mover(IEnumerable<string> rels, string? carpeta)
    {
        string? destino = carpeta is null ? null : Existente(carpeta);
        if (carpeta is not null && destino is null) return;
        foreach (string rel in rels)
        {
            if (destino is null) D.Archivos.Remove(Clave(rel));
            else D.Archivos[Clave(rel)] = destino;
        }
        Guardar();
    }

    public static void Limpiar(IEnumerable<string> existentes)
    {
        var vivos = existentes.Select(Clave).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> muertos = D.Archivos.Keys.Where(k => !vivos.Contains(k)).ToList();
        if (muertos.Count == 0) return;
        foreach (string k in muertos) D.Archivos.Remove(k);
        Guardar();
    }

    public static void Renombrados(IReadOnlyDictionary<string, string> mapa)
    {
        bool cambio = false;
        foreach ((string de, string a) in mapa)
            if (D.Archivos.Remove(Clave(de), out string? c))
            {
                D.Archivos[Clave(a)] = c;
                cambio = true;
            }
        if (cambio) Guardar();
    }

    public static int Cantidad(string carpeta) => D.Archivos.Count(kv => kv.Value.Equals(carpeta, StringComparison.CurrentCultureIgnoreCase));
}
