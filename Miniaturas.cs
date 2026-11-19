namespace IGP_Soft;

internal enum TipoEnlace { Web, Imagen, Video, Documento, Ventana, Otro }

internal static class TiposEnlace
{
    public static TipoEnlace De(string url)
    {
        if (WindowManager.IsWindowSpec(url)) return TipoEnlace.Ventana;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return TipoEnlace.Web;
        if (Documentos.Nombre(url) is not null) return TipoEnlace.Documento;
        return Thumbnails.KindOf(url) switch
        {
            MediaKind.Image => TipoEnlace.Imagen,
            MediaKind.Video => TipoEnlace.Video,
            _ => TipoEnlace.Otro,
        };
    }

    public static string Icono(TipoEnlace t) => t switch
    {
        TipoEnlace.Web => "🌐",
        TipoEnlace.Imagen => "🖼",
        TipoEnlace.Video => "🎬",
        TipoEnlace.Documento => "📄",
        TipoEnlace.Ventana => "▣",
        _ => "◆",
    };

    public static string Grupo(TipoEnlace t) => t switch
    {
        TipoEnlace.Web => "Páginas web",
        TipoEnlace.Imagen => "Imágenes",
        TipoEnlace.Video => "Videos",
        TipoEnlace.Documento => "Documentos",
        TipoEnlace.Ventana => "Ventanas",
        _ => "Otros",
    };

    public static Color Color(TipoEnlace t) => t switch
    {
        TipoEnlace.Web => System.Drawing.Color.FromArgb(37, 99, 235),
        TipoEnlace.Imagen => System.Drawing.Color.FromArgb(5, 150, 105),
        TipoEnlace.Video => System.Drawing.Color.FromArgb(219, 39, 119),
        TipoEnlace.Documento => System.Drawing.Color.FromArgb(217, 119, 6),
        TipoEnlace.Ventana => System.Drawing.Color.FromArgb(71, 85, 105),
        _ => System.Drawing.Color.FromArgb(100, 116, 139),
    };
}

internal static class Miniaturas
{
    public static readonly Size Tamano = new(128, 80);

    private static readonly Dictionary<string, Image?> Hechas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Pedidas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Cerrojo = new();
    private static SynchronizationContext? _ui;

    public static event Action? Lista;

    public static void Iniciar() => _ui ??= SynchronizationContext.Current;

    private static string? Archivo(string url)
    {
        if (WindowManager.IsWindowSpec(url) || url.Contains("://", StringComparison.Ordinal)) return null;
        try
        {
            string ruta = AssetStore.Resolve(url);
            return File.Exists(ruta) ? ruta : null;
        }
        catch { return null; }
    }

    public static Image? De(string url)
    {
        if (Archivo(url) is not { } ruta) return null;
        TipoEnlace tipo = TiposEnlace.De(url);
        if (tipo is not (TipoEnlace.Imagen or TipoEnlace.Video or TipoEnlace.Documento)) return null;
        lock (Cerrojo)
        {
            if (Hechas.TryGetValue(ruta, out Image? lista)) return lista;
            if (!Pedidas.Add(ruta)) return null;
        }
        _ui ??= SynchronizationContext.Current;
        Task.Run(() =>
        {
            Image? img = null;
            try
            {
                using Image? src = Thumbnails.Best(ruta, 256);
                if (src is not null) img = Thumbnails.Compose(src, ruta, Tamano);
            }
            catch { }
            lock (Cerrojo) Hechas[ruta] = img;
            if (img is null) return;
            if (_ui is { } ui) ui.Post(_ => Lista?.Invoke(), null);
            else Lista?.Invoke();
        });
        return null;
    }
}

internal static class DibujoPerfil
{
    public const int Alto = 168;
    private const int Separacion = 14;
    private const int Pie = 22;

    private static (int Cols, int Rows) GrillaDe(MonitorLayout m) =>
        m.Cols > 0 && m.Rows > 0 ? (m.Cols, m.Rows) : PantallaMonitor.LegacyGrid(m.Zones);

    public static void Dibujar(Graphics g, Rectangle area, LayoutProfile p)
    {
        List<MonitorLayout> monitores = p.Monitors.OrderBy(m => m.Number).ToList();
        if (monitores.Count == 0)
        {
            TextRenderer.DrawText(g, "Este perfil no tiene monitores guardados.", Theme.Small, area, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        IReadOnlyDictionary<int, Image> capturas = CapturasPerfiles.De(p.Name);
        bool sinCapturas = capturas.Count == 0;
        int nota = sinCapturas ? 18 : 0;
        var proporciones = monitores.Select(m => capturas.TryGetValue(m.Number, out Image? i) && !m.Excluded && i.Height > 0
            ? i.Width / (double)i.Height : 16 / 9.0).ToList();
        double altoMonitor = area.Height - Pie - nota;
        double huecos = (monitores.Count - 1) * Separacion;
        double suma = proporciones.Sum() * altoMonitor;
        if (suma + huecos > area.Width) altoMonitor *= Math.Max(0.1, (area.Width - huecos) / suma);
        int alto = Math.Max(20, (int)altoMonitor);
        int x = area.X;
        for (int k = 0; k < monitores.Count; k++)
        {
            MonitorLayout m = monitores[k];
            int ancho = Math.Max(30, (int)(alto * proporciones[k]));
            var marco = new Rectangle(x, area.Y, ancho, alto);
            if (!m.Excluded && capturas.TryGetValue(m.Number, out Image? img)) Captura(g, marco, img);
            else Monitor(g, marco, m);
            string etiqueta = $"Monitor {m.Number}" + (m.Primary ? " · principal" : "") + (m.Excluded ? " · omitido" : "");
            TextRenderer.DrawText(g, etiqueta, Theme.Small, new Rectangle(x, marco.Bottom + 3, ancho, Pie - 3), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            x += ancho + Separacion;
        }
        if (sinCapturas)
            TextRenderer.DrawText(g, ProfileStore.IsLastSession(p.Name)
                    ? "Esquema de lo que se abriría. La «Última sesión» se guarda sola y no lleva captura."
                    : "Esquema de lo que se abriría: este perfil no tiene captura. «Reemplazar con lo actual» la toma.",
                Theme.Small, new Rectangle(area.X, area.Bottom - nota, area.Width, nota), Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void Captura(Graphics g, Rectangle marco, Image img)
    {
        using (var fondo = new SolidBrush(Color.Black)) g.FillRectangle(fondo, marco);
        var interp = g.InterpolationMode;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, marco);
        g.InterpolationMode = interp;
        using var borde = new Pen(Color.FromArgb(30, 41, 59), 2);
        g.DrawRectangle(borde, marco);
    }

    private static void Monitor(Graphics g, Rectangle marco, MonitorLayout m)
    {
        Theme.FillRound(g, Color.FromArgb(30, 41, 59), marco, 6);
        if (m.Excluded)
        {
            TextRenderer.DrawText(g, "Omitido", Theme.Small, marco, Color.FromArgb(148, 163, 184),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        (int cols, int rows) = GrillaDe(m);
        GridLayout grilla = GridLayout.FromSaved(cols, rows, m.Layout);
        var dentro = Rectangle.Inflate(marco, -4, -4);
        float cw = dentro.Width / (float)grilla.Cols, rh = dentro.Height / (float)grilla.Rows;
        for (int i = 0; i < grilla.Zones.Count; i++)
        {
            ZoneSpec z = grilla.Zones[i];
            var r = Rectangle.Round(new RectangleF(dentro.X + z.C * cw + 1.5f, dentro.Y + z.R * rh + 1.5f, z.CS * cw - 3, z.RS * rh - 3));
            ZoneLayout? zl = m.Links.FirstOrDefault(l => l.Zone == i);
            Zona(g, r, z.Off, zl);
        }
    }

    private static void Zona(Graphics g, Rectangle r, bool off, ZoneLayout? z)
    {
        if (off)
        {
            Theme.FillRound(g, Color.FromArgb(15, 23, 42), r, 4);
            return;
        }
        if (z is null || string.IsNullOrEmpty(z.Url))
        {
            Theme.FillRound(g, Color.FromArgb(51, 65, 85), r, 4);
            return;
        }
        TipoEnlace tipo = TiposEnlace.De(z.Url);
        Image? mini = Miniaturas.De(z.Url);
        if (mini is not null)
        {
            using var fondo = new SolidBrush(Color.Black);
            g.FillRectangle(fondo, r);
            using Region antes = g.Clip;
            g.SetClip(r, System.Drawing.Drawing2D.CombineMode.Intersect);
            g.DrawImage(mini, Ajustar(mini.Size, r));
            g.Clip = antes;
        }
        else
        {
            Color c = TiposEnlace.Color(tipo);
            Theme.FillRound(g, Color.FromArgb(70, c), r, 4);
        }
        int pestanas = z.Pestanas?.Count ?? 0;
        string titulo = (string.IsNullOrWhiteSpace(z.Titulo) ? z.Url : z.Titulo) + (pestanas > 1 ? $"  +{pestanas - 1}" : "");
        if (r.Height >= 34 && r.Width >= 40)
        {
            var banda = new Rectangle(r.X, r.Bottom - 18, r.Width, 18);
            using (var b = new SolidBrush(Color.FromArgb(190, 15, 23, 42))) g.FillRectangle(b, banda);
            TextRenderer.DrawText(g, TiposEnlace.Icono(tipo) + " " + titulo, Theme.Small, Rectangle.Inflate(banda, -3, 0), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        else
        {
            TextRenderer.DrawText(g, TiposEnlace.Icono(tipo), Theme.Small, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    public static Rectangle Ajustar(Size img, Rectangle caja)
    {
        if (img.Width <= 0 || img.Height <= 0) return caja;
        float k = Math.Max(caja.Width / (float)img.Width, caja.Height / (float)img.Height);
        int w = (int)(img.Width * k), h = (int)(img.Height * k);
        return new Rectangle(caja.X + (caja.Width - w) / 2, caja.Y + (caja.Height - h) / 2, w, h);
    }
}

internal static class CapturasPerfiles
{
    public const int Ancho = 640;
    public const string NombreCarpeta = "IGP_Soft.perfiles.capturas";

    private static readonly Dictionary<string, Dictionary<int, Image>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string Carpeta { get; set; } = Path.Combine(AppPaths.DataDir, NombreCarpeta);

    private static string DirDe(string perfil)
    {
        string limpio = new(perfil.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '.' ? '_' : c).Take(40).ToArray());
        byte[] h = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(perfil.Trim().ToLowerInvariant()));
        return Path.Combine(Carpeta, $"{limpio}-{Convert.ToHexString(h, 0, 4).ToLowerInvariant()}");
    }

    private static void Soltar(string perfil)
    {
        if (!Cache.Remove(perfil.Trim(), out var viejas)) return;
        foreach (Image i in viejas.Values) i.Dispose();
    }

    public static void Guardar(string perfil, IEnumerable<(int Numero, byte[] Jpeg)> capturas)
    {
        Soltar(perfil);
        string dir = DirDe(perfil);
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            foreach ((int n, byte[] jpeg) in capturas)
                if (jpeg.Length > 0) File.WriteAllBytes(Path.Combine(dir, $"{n}.jpg"), jpeg);
        }
        catch { }
    }

    public static void Borrar(string perfil)
    {
        Soltar(perfil);
        try
        {
            string dir = DirDe(perfil);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { }
    }

    public static IReadOnlyDictionary<int, Image> De(string perfil)
    {
        if (Cache.TryGetValue(perfil.Trim(), out var hechas)) return hechas;
        var leidas = new Dictionary<int, Image>();
        try
        {
            string dir = DirDe(perfil);
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir, "*.jpg"))
                {
                    if (!int.TryParse(Path.GetFileNameWithoutExtension(f), out int n)) continue;
                    using var ms = new MemoryStream(File.ReadAllBytes(f));
                    using Image img = Image.FromStream(ms);
                    leidas[n] = new Bitmap(img);
                }
        }
        catch { }
        Cache[perfil.Trim()] = leidas;
        return leidas;
    }

    public static bool Tiene(string perfil) => De(perfil).Count > 0;
}
