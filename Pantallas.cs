using System.Text.Json.Serialization;

namespace IGP_Soft;

internal sealed class Segment
{
    public int Index;
    public ZoneSpec Spec = new(0, 0);
    public Rectangle Area;
    public LinkDef? Link;
    public LaunchedWindow? Launched;
    public object? Pending;
    public readonly List<Pestana> Pestanas = new();
    public int Activa;
    public bool Disabled => Spec.Off;
    public bool Busy => Link is not null || Launched is not null || Pending is not null;
}

internal sealed class Pestana
{
    public required LinkDef Link;
    public LaunchedWindow? Win;
    public object? Pending;
}

internal sealed class Congelada
{
    public required LinkDef Link;
    public required LaunchedWindow Win;
    public required int Zona;
    public required Rectangle Area;
    public required bool Oculta;
    public bool EsPestana;
    public List<LinkDef>? Pestanas;
    public int Activa;
}

internal sealed class PantallaMonitor
{
    public PantallaMonitor(int number, MonitorInfo monitor)
    {
        Number = number;
        Monitor = monitor;
        Apply(GridLayout.Uniform(1, 1));
    }

    public int Number { get; set; }
    public MonitorInfo Monitor { get; }
    public string Id => Monitor.Id;
    public string Device => Monitor.DeviceName;
    public bool Excluded { get; set; }
    public Segment? Maximized { get; set; }
    public GridLayout Grid { get; private set; } = GridLayout.Uniform(1, 1);
    public int Cols => Grid.Cols;
    public int Rows => Grid.Rows;
    public List<Segment> Segments { get; } = new();
    public IEnumerable<Segment> Usable => Segments.Where(s => !s.Disabled);
    public bool Bloqueado { get; set; }
    public GridLayout? GrillaProyectada { get; set; }
    public List<Congelada> Congeladas { get; } = new();

    public void Apply(GridLayout layout)
    {
        Maximized = null;
        Grid = layout;
        Segments.Clear();
        var bounds = new Rectangle(Monitor.X, Monitor.Y, Monitor.Width, Monitor.Height);
        for (int i = 0; i < layout.Zones.Count; i++)
            Segments.Add(new Segment { Index = i, Spec = layout.Zones[i], Area = layout.AreaOf(layout.Zones[i], bounds) });
    }

    public int DisplayNumber(Segment seg) => Usable.TakeWhile(s => s != seg).Count() + 1;

    public static (int cols, int rows) LegacyGrid(int n) => n switch
    {
        2 => (2, 1),
        3 => (3, 1),
        4 => (2, 2),
        6 => (3, 2),
        8 => (4, 2),
        9 => (3, 3),
        _ => (1, 1),
    };
}

internal enum Nivel { Info, Aviso, Error }

internal sealed record Resultado(bool Ok, string? Codigo = null, string? Titulo = null, string? Mensaje = null, Nivel Nivel = Nivel.Info)
{
    public static Resultado Bien { get; } = new(true);

    public static Resultado Falla(string codigo, string? mensaje = null) => new(false, codigo, null, mensaje);
}

internal static class EstadosZona
{
    public const string Libre = "libre";
    public const string Abriendo = "abriendo";
    public const string EnPantalla = "en_pantalla";
    public const string Desactivada = "desactivada";
    public const string Borrador = "borrador";
}

internal sealed record EstadoZona(int Zona, string Estado, string? Titulo, string? Url, bool Adjunta, int X, int Y, int Ancho, int Alto,
    IReadOnlyList<LinkDef>? Pestanas = null, int Activa = 0)
{
    [JsonIgnore] public bool Web => PestanasWeb.EsWeb(Url) && Estado is EstadosZona.EnPantalla or EstadosZona.Borrador;
    [JsonIgnore] public int CantidadPestanas => Pestanas?.Count ?? (Url is null ? 0 : 1);

    [JsonIgnore] public bool Desactivada => Estado == EstadosZona.Desactivada;
    [JsonIgnore] public bool Ocupada => Estado is EstadosZona.Abriendo or EstadosZona.EnPantalla || Url is not null;
}

internal sealed record EstadoMonitor(
    string Id, string Dispositivo, int Numero, int X, int Y, int Ancho, int Alto, bool Principal, int Miembros, bool Omitido,
    int Columnas, int Filas, IReadOnlyList<ZoneSpec> Grilla, int? Ampliada, IReadOnlyList<EstadoZona> Zonas, bool Bloqueado = false)
{
    [JsonIgnore] public bool Combinado => Miembros > 1;

    public GridLayout Layout() => GridLayout.FromSaved(Columnas, Filas, Grilla);

    [JsonIgnore] public IEnumerable<EstadoZona> Usables => Zonas.Where(z => !z.Desactivada);

    public int NumeroVisible(EstadoZona zona) => Usables.TakeWhile(z => z.Zona != zona.Zona).Count() + 1;
}

internal sealed record EstadoPantallas(
    long Seq, string Equipo, string? Navegador, IReadOnlyList<string> Navegadores, bool OmitirPrincipal, bool NumeracionPropia,
    IReadOnlyList<EstadoMonitor> Monitores, IReadOnlyList<LinkDef> Enlaces, IReadOnlyList<LayoutProfile> Perfiles)
{
    public static EstadoPantallas Vacio { get; } = new(0, "", null, Array.Empty<string>(), false, false,
        Array.Empty<EstadoMonitor>(), Array.Empty<LinkDef>(), Array.Empty<LayoutProfile>());

    public static EstadoMonitor MonitorDesde(PantallaMonitor p) => new(
        p.Id, p.Device, p.Number, p.Monitor.X, p.Monitor.Y, p.Monitor.Width, p.Monitor.Height, p.Monitor.IsPrimary,
        p.Monitor.Members?.Count ?? 1, p.Excluded, p.Cols, p.Rows, p.Grid.Zones.ToList(),
        p.Maximized?.Index,
        p.Segments.Select(s => new EstadoZona(s.Index, EstadoDe(p, s), s.Link?.Titulo, s.Link?.Url, s.Launched is { Attached: true, AlCerrar: null },
            s.Area.X, s.Area.Y, s.Area.Width, s.Area.Height,
            s.Pestanas.Count > 0 ? s.Pestanas.Select(t => t.Link).ToList() : null, s.Activa)).ToList(), p.Bloqueado);

    private static string EstadoDe(PantallaMonitor p, Segment s) =>
        s.Disabled ? EstadosZona.Desactivada
        : p.Bloqueado && s.Link is not null && s.Launched is null && s.Pending is null ? EstadosZona.Borrador
        : s.Pending is not null ? EstadosZona.Abriendo
        : s.Launched is not null ? EstadosZona.EnPantalla
        : EstadosZona.Libre;

    [JsonIgnore] public int EnVivo => Monitores.Sum(m => m.Zonas.Count(z => z.Estado == EstadosZona.EnPantalla));

    [JsonIgnore] public int ZonasUsables => Monitores.Where(m => !m.Omitido).Sum(m => m.Usables.Count());

    [JsonIgnore] public bool HayAbierto => Monitores.Any(m => m.Zonas.Any(z => z.Estado is EstadosZona.EnPantalla or EstadosZona.Abriendo));

    [JsonIgnore] public bool HayBloqueados => Monitores.Any(m => m.Bloqueado);

    [JsonIgnore] public bool TodosBloqueados => Monitores.Any(m => !m.Omitido) && Monitores.Where(m => !m.Omitido).All(m => m.Bloqueado);

    public bool EstaAbierto(string url) => Monitores.Any(m => m.Zonas.Any(z => string.Equals(z.Url, url, StringComparison.OrdinalIgnoreCase) ||
        z.Pestanas?.Any(p => string.Equals(p.Url, url, StringComparison.OrdinalIgnoreCase)) == true));

    public EstadoMonitor? Monitor(string id) => Monitores.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public LayoutProfile ComoPerfil(string nombre) => new()
    {
        Name = nombre,
        Browser = Navegador,
        Saved = DateTime.Now,
        Monitors = Monitores.Select(m => new MonitorLayout
        {
            Number = m.Numero,
            Device = m.Dispositivo,
            Hardware = m.Id,
            Primary = m.Principal,
            Excluded = m.Omitido,
            Zones = m.Zonas.Count,
            Cols = m.Columnas,
            Rows = m.Filas,
            Layout = m.Layout().IsUniform ? null : m.Grilla.ToList(),
            Links = m.Zonas.Where(z => z.Url is not null)
                           .Select(z => new ZoneLayout { Zone = z.Zona, Titulo = z.Titulo ?? "", Url = z.Url!, Pestanas = z.Pestanas?.ToList(), Activa = z.Activa })
                           .ToList(),
        }).ToList(),
    };
}

internal sealed record VentanaAbierta(string Proceso, string Titulo, string Spec);

internal sealed record MonitorFisico(string Id, int X, int Y, int Ancho, int Alto, bool Principal)
{
    public MonitorInfo ComoMonitor() => new(Id, X, Y, Ancho, Alto, Principal) { HardwareId = Id };
}

internal static class Permisos
{
    public static bool EsSeguro(string url, string assetsRoot)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (WindowManager.IsWindowSpec(url)) return true;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? u))
            return u.Scheme is "http" or "https" && !u.IsUnc && u.Host.Length > 0;
        if (!AssetStore.IsAssetPath(url) || !TiposPermitidos.Extension(url)) return false;
        string rel = url.Replace('/', '\\');
        if (rel.Split('\\').Any(p => p == "..")) return false;
        try
        {
            string root = Path.GetFullPath(assetsRoot).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(root.TrimEnd('\\'))!, rel));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

internal interface IPantallas
{
    EstadoPantallas Estado { get; }

    event Action<EstadoPantallas>? Cambio;
    event Action<Resultado>? Aviso;

    Task<Resultado> Abrir(string monitor, int zona, LinkDef enlace);
    Task<Resultado> Cerrar(string monitor, int zona);
    Task<Resultado> CambiarGrilla(string monitor, GridLayout grilla);
    Task<Resultado> DividirTodos(int columnas, int filas);
    Task<Resultado> AlternarOmitir(string monitor);
    Task<Resultado> OmitirPrincipal(bool omitir);
    Task<Resultado> CerrarMonitor(string monitor);
    Task<Resultado> IntercambiarNumeros(string a, string b);
    Task<Resultado> NumeracionOriginal();
    Task<Resultado> Combinar(List<List<string>> grupos);
    Task<Resultado> Separar(string monitor);
    Task<Resultado> Bloquear(string? monitor, bool bloquear);
    Task<Resultado> AbrirPestana(string monitor, int zona, LinkDef enlace);
    Task<Resultado> ActivarPestana(string monitor, int zona, int indice);
    Task<Resultado> CerrarPestana(string monitor, int zona, int indice);
    Task<Resultado> Ampliar(string monitor, int zona);
    Task<Resultado> Restaurar(string? monitor);
    Task<Resultado> AmpliarSiguiente(string monitor, int paso);
    Task<Resultado> AplicarPerfil(LayoutProfile perfil);
    Task<Resultado> AbrirEnOrden();
    Task<Resultado> CerrarTodo();
    Task<Resultado> Identificar();
    Task<Resultado> RecargarMonitores();
    Task<Resultado> RecargarTodo();
    Task<Resultado> ElegirNavegador(string nombre);
    Task<Resultado> GuardarEnlaces(List<LinkDef> enlaces);
    Task<Resultado> GuardarPerfiles(List<LayoutProfile> perfiles);
    Task<List<VentanaAbierta>> VentanasAbiertas();
    Task<List<MonitorFisico>> MonitoresFisicos();
    Task<byte[]?> VistaPrevia(string monitor, int ancho);
}
