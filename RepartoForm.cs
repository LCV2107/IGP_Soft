namespace IGP_Soft;

internal sealed record Reparto(string Monitor, GridLayout Grilla, IReadOnlyList<(int Zona, LinkDef Elemento)> Asignaciones, bool AgregarALista);

internal static class Repartos
{
    public static readonly IReadOnlyList<(int Cols, int Rows)> Uniformes = new[]
    {
        (1, 1), (2, 1), (3, 1), (2, 2), (3, 2), (4, 2), (3, 3), (4, 3), (4, 4),
    };

    public static HashSet<int> Ocupadas(GridLayout actual, IReadOnlyCollection<int> ocupadas, GridLayout destino) =>
        destino.SameAs(actual)
            ? ocupadas.Where(i => i < destino.Zones.Count && !destino.Zones[i].Off).ToHashSet()
            : GridLayout.Carry(actual, ocupadas, destino).Values.ToHashSet();

    public static GridLayout Recomendada(GridLayout actual, IReadOnlyCollection<int> ocupadas, int elementos)
    {
        int Libres(GridLayout g)
        {
            HashSet<int> usadas = Ocupadas(actual, ocupadas, g);
            return g.Zones.Where((z, i) => !z.Off && !usadas.Contains(i)).Count();
        }
        if (Libres(actual) >= elementos) return actual;
        foreach ((int c, int r) in Uniformes)
        {
            GridLayout g = GridLayout.Uniform(c, r);
            if (Libres(g) >= elementos) return g;
        }
        foreach ((int c, int r) in Uniformes)
        {
            GridLayout g = GridLayout.Uniform(c, r);
            if (g.ActiveCount >= elementos) return g;
        }
        return GridLayout.Uniform(4, 4);
    }

    public static int?[] Repartir(GridLayout grilla, ISet<int> ocupadas, int elementos)
    {
        var zonas = new int?[grilla.Zones.Count];
        var orden = Enumerable.Range(0, grilla.Zones.Count).Where(i => !grilla.Zones[i].Off)
                              .OrderBy(i => ocupadas.Contains(i) ? 1 : 0).ThenBy(i => i).ToList();
        int e = 0;
        foreach (int i in orden)
        {
            if (e >= elementos || ocupadas.Contains(i)) break;
            zonas[i] = e++;
        }
        return zonas;
    }

    public static LinkDef Elemento(string ruta) => new(Path.GetFileNameWithoutExtension(ruta), ruta);
}

internal sealed class RepartoForm : Form
{
    private readonly IReadOnlyList<LinkDef> _elementos;
    private readonly List<EstadoMonitor> _monitores;
    private readonly ComboBox _cboMonitor = new();
    private readonly ComboBox _cboGrilla = new();
    private readonly Label _resumen = new();
    private readonly ToggleSwitch _agregar = new("Agregar también a la lista de enlaces");
    private readonly RepartoLienzo _lienzo = new();
    private readonly List<(string Nombre, GridLayout Grilla)> _grillas = new();
    private bool _cargando;

    public Reparto? Resultado { get; private set; }

    public RepartoForm(IReadOnlyList<LinkDef> elementos, IEnumerable<EstadoMonitor> monitores, string? monitor)
    {
        _elementos = elementos;
        _monitores = monitores.Where(m => !m.Omitido).ToList();
        Theme.StyleWindow(this, darkCaption: false);
        Text = $"Organizar {elementos.Count} elemento(s) en un monitor";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(760, 560);
        ClientSize = new Size(940, 660);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(14, 12, 14, 0), WrapContents = false, BackColor = Theme.Background };
        _cboMonitor.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboMonitor.Font = Theme.Body;
        _cboMonitor.Width = 230;
        _cboGrilla.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboGrilla.Font = Theme.Body;
        _cboGrilla.Width = 250;
        top.Controls.Add(new Label { Text = "Monitor", AutoSize = true, Font = Theme.BodyBold, ForeColor = Theme.Text, Margin = new Padding(0, 6, 6, 0) });
        top.Controls.Add(_cboMonitor);
        top.Controls.Add(new Label { Text = "Grilla", AutoSize = true, Font = Theme.BodyBold, ForeColor = Theme.Text, Margin = new Padding(18, 6, 6, 0) });
        top.Controls.Add(_cboGrilla);

        _resumen.Dock = DockStyle.Top;
        _resumen.Height = 30;
        _resumen.Padding = new Padding(16, 0, 16, 0);
        _resumen.ForeColor = Theme.TextMuted;
        _resumen.TextAlign = ContentAlignment.MiddleLeft;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
        _agregar.Checked = true;
        _agregar.Location = new Point(14, 16);
        _agregar.Width = 380;
        var ok = new ModernButton("Mostrar", ButtonKind.Primary, "▶") { Size = new Size(120, 38), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        var cancel = new ModernButton("Cancelar") { DialogResult = DialogResult.Cancel, Size = new Size(100, 38), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        bottom.Controls.AddRange(new Control[] { _agregar, ok, cancel });
        bottom.Resize += (_, _) =>
        {
            cancel.Location = new Point(bottom.Width - cancel.Width - 14, 11);
            ok.Location = new Point(cancel.Left - ok.Width - 8, 11);
        };
        ok.Click += (_, _) => Aceptar();
        CancelButton = cancel;

        _lienzo.Dock = DockStyle.Fill;
        _lienzo.Cambio += ActualizarResumen;
        Controls.Add(_lienzo);
        Controls.Add(_resumen);
        Controls.Add(top);
        Controls.Add(bottom);

        foreach (EstadoMonitor m in _monitores)
            _cboMonitor.Items.Add($"Monitor {m.Numero}" + (m.Principal ? " (principal)" : "") + (m.Bloqueado ? " · bloqueado" : ""));
        _cboMonitor.SelectedIndexChanged += (_, _) => CargarGrillas();
        _cboGrilla.SelectedIndexChanged += (_, _) => { if (!_cargando) Redistribuir(); };
        int inicial = Math.Max(0, _monitores.FindIndex(m => m.Id.Equals(monitor, StringComparison.OrdinalIgnoreCase)));
        if (_monitores.Count > 0) _cboMonitor.SelectedIndex = inicial;
    }

    private EstadoMonitor? Monitor => _cboMonitor.SelectedIndex >= 0 && _cboMonitor.SelectedIndex < _monitores.Count ? _monitores[_cboMonitor.SelectedIndex] : null;

    private static List<int> OcupadasDe(EstadoMonitor m) => m.Zonas.Where(z => z.Url is not null && !z.Desactivada).Select(z => z.Zona).ToList();

    private void CargarGrillas()
    {
        if (Monitor is not { } m) return;
        _cargando = true;
        _grillas.Clear();
        GridLayout actual = m.Layout();
        GridLayout recomendada = Repartos.Recomendada(actual, OcupadasDe(m), _elementos.Count);
        _grillas.Add(($"Recomendada · {recomendada.Describe()}", recomendada));
        if (!recomendada.SameAs(actual)) _grillas.Add(($"La actual · {actual.Describe()}", actual));
        foreach ((int c, int r) in Repartos.Uniformes)
        {
            GridLayout g = GridLayout.Uniform(c, r);
            if (_grillas.All(x => !x.Grilla.SameAs(g))) _grillas.Add(($"{c}×{r}", g));
        }
        foreach ((string nombre, GridLayout g) in GridLayout.Presets)
            if (_grillas.All(x => !x.Grilla.SameAs(g))) _grillas.Add((nombre, g));
        _cboGrilla.Items.Clear();
        foreach ((string nombre, _) in _grillas) _cboGrilla.Items.Add(nombre);
        _cboGrilla.SelectedIndex = 0;
        _cargando = false;
        Redistribuir();
    }

    private void Redistribuir()
    {
        if (Monitor is not { } m || _cboGrilla.SelectedIndex < 0) return;
        GridLayout actual = m.Layout();
        GridLayout grilla = _grillas[_cboGrilla.SelectedIndex].Grilla;
        List<int> ocupadas = OcupadasDe(m);
        var existentes = new Dictionary<int, string>();
        if (grilla.SameAs(actual))
        {
            foreach (EstadoZona z in m.Zonas.Where(z => z.Url is not null && !z.Desactivada)) existentes[z.Zona] = z.Titulo ?? "";
        }
        else
        {
            foreach ((int desde, int hacia) in GridLayout.Carry(actual, ocupadas, grilla))
                existentes[hacia] = m.Zonas.First(z => z.Zona == desde).Titulo ?? "";
        }
        int?[] zonas = Repartos.Repartir(grilla, existentes.Keys.ToHashSet(), _elementos.Count);
        _lienzo.Cargar(grilla, new SizeF(Math.Max(1, m.Ancho), Math.Max(1, m.Alto)), _elementos, zonas, existentes);
    }

    private void ActualizarResumen()
    {
        int sin = _lienzo.SinAsignar;
        int reemplaza = _lienzo.Reemplazos;
        _resumen.Text = "Arrastra cada elemento a la zona donde debe ir. " +
            (sin > 0 ? $"{sin} sin asignar (no se mostrarán). " : "Todos tienen zona. ") +
            (reemplaza > 0 ? $"{reemplaza} reemplaza(n) lo que hoy muestra esa zona." : "");
    }

    private void Aceptar()
    {
        if (Monitor is not { } m || _cboGrilla.SelectedIndex < 0) return;
        List<(int Zona, LinkDef Elemento)> asignaciones = _lienzo.Asignaciones().Select(a => (a.Zona, _elementos[a.Elemento])).ToList();
        if (asignaciones.Count == 0)
        {
            MessageBox.Show(this, "Arrastra al menos un elemento a una zona.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Resultado = new Reparto(m.Id, _grillas[_cboGrilla.SelectedIndex].Grilla, asignaciones, _agregar.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class RepartoLienzo : Control
{
    private const int Bandeja = 118;
    private const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

    private GridLayout _grilla = GridLayout.Uniform(1, 1);
    private SizeF _proporcion = new(16, 9);
    private IReadOnlyList<LinkDef> _elementos = Array.Empty<LinkDef>();
    private int?[] _zonas = Array.Empty<int?>();
    private Dictionary<int, string> _existentes = new();
    private int? _arrastrando;
    private int? _origenZona;
    private Point _cursor;
    private int? _sobreZona;
    private bool _sobreBandeja;

    public event Action? Cambio;

    public RepartoLienzo()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        Font = Theme.Body;
    }

    public int SinAsignar => _elementos.Count - _zonas.Count(z => z is not null);

    public int Reemplazos => _zonas.Select((e, i) => (e, i)).Count(x => x.e is not null && _existentes.ContainsKey(x.i));

    public IEnumerable<(int Zona, int Elemento)> Asignaciones() =>
        _zonas.Select((e, i) => (e, i)).Where(x => x.e is not null).Select(x => (x.i, x.e!.Value));

    public void Cargar(GridLayout grilla, SizeF proporcion, IReadOnlyList<LinkDef> elementos, int?[] zonas, Dictionary<int, string> existentes)
    {
        _grilla = grilla;
        _proporcion = proporcion;
        _elementos = elementos;
        _zonas = zonas;
        _existentes = existentes;
        Invalidate();
        Cambio?.Invoke();
    }

    private RectangleF Pantalla
    {
        get
        {
            var area = new RectangleF(16, 8, Width - 32, Height - Bandeja - 24);
            float escala = Math.Min(area.Width / _proporcion.Width, area.Height / _proporcion.Height);
            var tam = new SizeF(_proporcion.Width * escala, _proporcion.Height * escala);
            return new RectangleF(area.X + (area.Width - tam.Width) / 2, area.Y + (area.Height - tam.Height) / 2, tam.Width, tam.Height);
        }
    }

    private RectangleF ZonaRect(int i)
    {
        RectangleF p = Pantalla;
        RectangleF r = _grilla.AreaOf(_grilla.Zones[i], p.Size);
        r.Offset(p.Location);
        r.Inflate(-4, -4);
        return r;
    }

    private Rectangle BandejaRect => new(16, Height - Bandeja - 8, Width - 32, Bandeja);

    private List<int> EnBandeja => Enumerable.Range(0, _elementos.Count).Where(e => !_zonas.Contains(e)).ToList();

    private RectangleF ChipBandeja(int posicion)
    {
        Rectangle b = BandejaRect;
        const float w = 190, h = 34;
        int porFila = Math.Max(1, (int)((b.Width - 16) / (w + 8)));
        return new RectangleF(b.X + 8 + posicion % porFila * (w + 8), b.Y + 30 + posicion / porFila * (h + 6), w, h);
    }

    private int? ZonaEn(Point p)
    {
        for (int i = 0; i < _grilla.Zones.Count; i++)
            if (!_grilla.Zones[i].Off && ZonaRect(i).Contains(p)) return i;
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (ZonaEn(e.Location) is { } z && _zonas[z] is { } el)
        {
            _arrastrando = el;
            _origenZona = z;
        }
        else
        {
            List<int> bandeja = EnBandeja;
            for (int k = 0; k < bandeja.Count; k++)
                if (ChipBandeja(k).Contains(e.Location)) { _arrastrando = bandeja[k]; _origenZona = null; }
        }
        if (_arrastrando is null) return;
        _cursor = e.Location;
        Capture = true;
        Cursor = Cursors.SizeAll;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_arrastrando is null) return;
        _cursor = e.Location;
        _sobreZona = ZonaEn(e.Location);
        _sobreBandeja = BandejaRect.Contains(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_arrastrando is not { } el) return;
        if (ZonaEn(e.Location) is { } destino)
        {
            int? previo = _zonas[destino];
            if (_origenZona is { } o) _zonas[o] = previo;
            _zonas[destino] = el;
        }
        else if (BandejaRect.Contains(e.Location) && _origenZona is { } o)
            _zonas[o] = null;
        _arrastrando = null;
        _origenZona = null;
        _sobreZona = null;
        _sobreBandeja = false;
        Capture = false;
        Cursor = Cursors.Default;
        Invalidate();
        Cambio?.Invoke();
    }

    private string Nombre(int e) => _elementos[e].Titulo.Length > 0 ? _elementos[e].Titulo : Path.GetFileName(_elementos[e].Url);

    private static string Icono(string url) =>
        PestanasWeb.EsWeb(url) ? "🌐" : Documentos.Nombre(url) is not null ? "📄" : Thumbnails.KindOf(url) switch
        {
            MediaKind.Image => "🖼",
            MediaKind.Video => "🎞",
            _ => "•",
        };

    private void Chip(Graphics g, RectangleF r, int e, bool fantasma)
    {
        Theme.FillRound(g, fantasma ? Color.FromArgb(200, 37, 99, 235) : Theme.Accent, r, 10);
        TextRenderer.DrawText(g, $"{Icono(_elementos[e].Url)}  {Nombre(e)}", Theme.SmallBold, Rectangle.Round(new RectangleF(r.X + 6, r.Y, r.Width - 12, r.Height)),
            Color.White, C);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);

        RectangleF pantalla = Pantalla;
        Theme.FillRound(g, Color.FromArgb(30, 41, 59), pantalla, 12);
        for (int i = 0; i < _grilla.Zones.Count; i++)
        {
            RectangleF r = ZonaRect(i);
            if (_grilla.Zones[i].Off)
            {
                Theme.FillRound(g, Color.FromArgb(51, 65, 85), r, 8);
                TextRenderer.DrawText(g, "sin usar", Theme.Small, Rectangle.Round(r), Color.FromArgb(148, 163, 184), C);
                continue;
            }
            bool sobre = _arrastrando is not null && _sobreZona == i;
            Theme.FillRound(g, sobre ? Color.FromArgb(70, 37, 99, 235) : Color.FromArgb(18, 255, 255, 255), r, 8);
            using (var pen = new Pen(sobre ? Color.FromArgb(147, 197, 253) : Color.FromArgb(80, 255, 255, 255), sobre ? 2f : 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (var path = Theme.Round(r, 8))
                g.DrawPath(pen, path);
            TextRenderer.DrawText(g, $"Zona {i + 1}", Theme.Small, new Rectangle((int)r.X + 8, (int)r.Y + 6, 80, 18), Color.FromArgb(148, 163, 184), TextFormatFlags.Left);

            bool existe = _existentes.TryGetValue(i, out string? actual);
            if (_zonas[i] is { } el && !(el == _arrastrando && _origenZona == i))
            {
                var chip = new RectangleF(r.X + 10, r.Y + r.Height / 2 - 18, r.Width - 20, 36);
                Chip(g, chip, el, false);
                if (existe)
                    TextRenderer.DrawText(g, $"Reemplaza: {actual}", Theme.Small, Rectangle.Round(new RectangleF(r.X + 6, chip.Bottom + 4, r.Width - 12, 18)),
                        Color.FromArgb(251, 191, 36), C);
            }
            else if (existe)
                TextRenderer.DrawText(g, $"Se queda: {actual}", Theme.Small, Rectangle.Round(new RectangleF(r.X + 6, r.Y, r.Width - 12, r.Height)),
                    Color.FromArgb(203, 213, 225), C);
            else
                TextRenderer.DrawText(g, "Libre", Theme.Small, Rectangle.Round(r), Color.FromArgb(100, 116, 139), C);
        }

        Rectangle b = BandejaRect;
        Theme.FillRound(g, _sobreBandeja && _origenZona is not null ? Theme.AccentSoft : Theme.Surface, b, 12);
        Theme.DrawRound(g, Theme.Border, b, 12);
        List<int> bandeja = EnBandeja;
        TextRenderer.DrawText(g, bandeja.Count == 0 ? "Sin asignar · vacío (arrastra aquí lo que no quieras mostrar)" : $"Sin asignar · {bandeja.Count}",
            Theme.SmallBold, new Rectangle(b.X + 12, b.Y + 6, b.Width - 24, 20), Theme.TextMuted, TextFormatFlags.Left);
        for (int k = 0; k < bandeja.Count; k++)
        {
            RectangleF r = ChipBandeja(k);
            if (r.Bottom > b.Bottom) break;
            if (bandeja[k] != _arrastrando) Chip(g, r, bandeja[k], false);
        }

        if (_arrastrando is { } a)
            Chip(g, new RectangleF(_cursor.X - 95, _cursor.Y - 17, 190, 34), a, true);
    }
}
