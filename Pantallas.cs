namespace IGP_Soft;

internal sealed class Segment
{
    public int Index;
    public ZoneSpec Spec = new(0, 0);
    public Rectangle Area;
    public LinkDef? Link;
    public LaunchedWindow? Launched;
    public object? Pending;
    public bool Disabled => Spec.Off;
    public bool Busy => Link is not null || Launched is not null || Pending is not null;
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
    public string Id => Monitor.DeviceName;
    public bool Excluded { get; set; }
    public Segment? Maximized { get; set; }
    public GridLayout Grid { get; private set; } = GridLayout.Uniform(1, 1);
    public int Cols => Grid.Cols;
    public int Rows => Grid.Rows;
    public List<Segment> Segments { get; } = new();
    public IEnumerable<Segment> Usable => Segments.Where(s => !s.Disabled);

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

    public static Resultado Falla(string codigo) => new(false, codigo);
}

internal static class EstadosZona
{
    public const string Libre = "libre";
    public const string Abriendo = "abriendo";
    public const string EnPantalla = "en_pantalla";
    public const string Desactivada = "desactivada";
}

internal sealed record EstadoZona(int Zona, string Estado, string? Titulo, string? Url, int X, int Y, int Ancho, int Alto);

internal sealed record EstadoMonitor(
    string Id, int Numero, int X, int Y, int Ancho, int Alto, bool Principal, int Miembros, bool Omitido,
    int Columnas, int Filas, IReadOnlyList<ZoneSpec> Grilla, int? Ampliada, IReadOnlyList<EstadoZona> Zonas);

internal sealed record EstadoPantallas(long Seq, string? Navegador, bool OmitirPrincipal, IReadOnlyList<EstadoMonitor> Monitores)
{
    public static EstadoPantallas Desde(long seq, string? navegador, bool omitirPrincipal, IEnumerable<PantallaMonitor> pantallas) =>
        new(seq, navegador, omitirPrincipal, pantallas.Select(p => new EstadoMonitor(
            p.Id, p.Number, p.Monitor.X, p.Monitor.Y, p.Monitor.Width, p.Monitor.Height, p.Monitor.IsPrimary,
            p.Monitor.Members?.Count ?? 1, p.Excluded, p.Cols, p.Rows, p.Grid.Zones.ToList(),
            p.Maximized?.Index,
            p.Segments.Select(s => new EstadoZona(s.Index, EstadoDe(s), s.Link?.Titulo, s.Link?.Url,
                s.Area.X, s.Area.Y, s.Area.Width, s.Area.Height)).ToList())).ToList());

    private static string EstadoDe(Segment s) =>
        s.Disabled ? EstadosZona.Desactivada
        : s.Pending is not null ? EstadosZona.Abriendo
        : s.Launched is not null ? EstadosZona.EnPantalla
        : EstadosZona.Libre;
}

internal interface IPantallas
{
    EstadoPantallas Estado { get; }

    event Action<EstadoPantallas>? Cambio;

    Task<Resultado> Abrir(string monitor, int zona, LinkDef enlace);
    Task<Resultado> Cerrar(string monitor, int zona);
    Task<Resultado> CambiarGrilla(string monitor, GridLayout grilla);
    Task<Resultado> AplicarPerfil(LayoutProfile perfil);
    Task<Resultado> AbrirEnOrden();
    Task<Resultado> CerrarTodo();
    Task<Resultado> Identificar();
    Task<List<OpenWindow>> VentanasAbiertas();
    Task<byte[]?> VistaPrevia(string monitor, int ancho);
}
