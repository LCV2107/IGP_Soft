namespace IGP_Soft;

internal sealed record AccionZona(string Icono, string Texto, Action<Control> Clic, bool Activa = true, string? Ayuda = null, bool Peligro = false, bool Cierra = true);

internal sealed class PanelZonaForm : Form
{
    private const string ClaveSeguir = "panelzona.seguir";

    private readonly Func<EstadoPantallas> _estado;
    private readonly Func<EstadoMonitor, EstadoZona, IReadOnlyList<AccionZona>> _acciones;
    private readonly Func<EstadoMonitor, EstadoZona, IReadOnlyList<AccionZona>> _pie;
    private readonly Action<EstadoMonitor, EstadoZona, LinkDef> _elegir;
    private readonly string _monitor;
    private int _zona;
    private readonly HashSet<int> _asignadas = new();
    private readonly Dictionary<string, (string Monitor, int Zona)> _pendientes = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _graciaHasta;

    private readonly Panel _cabecera = new() { Dock = DockStyle.Top, Height = 78 };
    private readonly FlowLayoutPanel _botones = new() { Dock = DockStyle.Top, Height = 92, WrapContents = false, AutoScroll = false, Padding = new Padding(14, 10, 14, 4) };
    private readonly Panel _busqueda = new() { Dock = DockStyle.Top, Height = 50, Padding = new Padding(14, 6, 14, 6) };
    private readonly RoundedTextBox _buscar = new("Buscar qué mostrar…  (escribe para filtrar)", 300);
    private readonly FlowLayoutPanel _filtros = new() { Dock = DockStyle.Top, Height = 38, WrapContents = false, Padding = new Padding(12, 2, 14, 0) };
    private readonly Panel _desplazable = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 4, 6, 4) };
    private readonly LienzoTarjetas _tarjetas = new();
    private readonly Panel _piePanel = new() { Dock = DockStyle.Bottom, Height = 58 };
    private readonly ToggleSwitch _seguir = new("Seguir con la siguiente zona");
    private readonly FlowLayoutPanel _pieBotones = new() { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 12, 12, 0) };
    private readonly Label _vacio = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted, Visible = false };
    private TipoEnlace? _filtro;

    public PanelZonaForm(Func<EstadoPantallas> estado, string monitor, int zona,
        Func<EstadoMonitor, EstadoZona, IReadOnlyList<AccionZona>> acciones,
        Func<EstadoMonitor, EstadoZona, IReadOnlyList<AccionZona>> pie,
        Action<EstadoMonitor, EstadoZona, LinkDef> elegir)
    {
        _estado = estado;
        _monitor = monitor;
        _zona = zona;
        _acciones = acciones;
        _pie = pie;
        _elegir = elegir;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = true;
        TopMost = true;
        BackColor = Theme.Surface;
        Font = Theme.Body;
        Size = new Size(744, 620);
        Padding = new Padding(1);
        DoubleBuffered = true;

        _cabecera.BackColor = Theme.Sidebar;
        _cabecera.Paint += PintarCabecera;
        _cabecera.MouseClick += (_, e) => { if (CerrarRect.Contains(e.Location)) Close(); };
        _cabecera.MouseMove += (_, e) => _cabecera.Cursor = CerrarRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;

        _botones.BackColor = Theme.Surface;
        _busqueda.BackColor = Theme.Surface;
        _buscar.Dock = DockStyle.Fill;
        _buscar.Box.TextChanged += (_, _) => Recargar();
        _buscar.Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Down or Keys.Enter && _tarjetas.Cantidad > 0)
            {
                e.SuppressKeyPress = true;
                _tarjetas.Focus();
                if (_tarjetas.Seleccion < 0) _tarjetas.Seleccion = 0;
                if (e.KeyCode == Keys.Enter) _tarjetas.ElegirSeleccion();
            }
        };
        _busqueda.Controls.Add(_buscar);
        _filtros.BackColor = Theme.Surface;
        _desplazable.BackColor = Theme.Surface;
        _desplazable.Controls.Add(_tarjetas);
        _desplazable.Controls.Add(_vacio);
        _tarjetas.Location = new Point(_desplazable.Padding.Left, _desplazable.Padding.Top);
        _desplazable.Resize += (_, _) => _tarjetas.Ajustar(_desplazable.ClientSize.Width - _desplazable.Padding.Horizontal);
        _tarjetas.Elegida += Elegir;
        _tarjetas.Tecla += c =>
        {
            _buscar.Box.Focus();
            _buscar.Box.AppendText(c.ToString());
        };

        _piePanel.BackColor = Theme.SurfaceAlt;
        _piePanel.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, _piePanel.Width, 0); };
        _seguir.Location = new Point(14, 13);
        _seguir.Size = _seguir.GetPreferredSize(Size.Empty);
        _seguir.BackColor = Theme.SurfaceAlt;
        _seguir.Checked = Config.Get(ClaveSeguir) == "1";
        _seguir.CheckedChanged += (_, _) => Config.Set(ClaveSeguir, _seguir.Checked ? "1" : "0");
        _pieBotones.BackColor = Theme.SurfaceAlt;
        _piePanel.Controls.Add(_pieBotones);
        _piePanel.Controls.Add(_seguir);

        Controls.Add(_desplazable);
        Controls.Add(_filtros);
        Controls.Add(_busqueda);
        Controls.Add(_botones);
        Controls.Add(_cabecera);
        Controls.Add(_piePanel);

        Miniaturas.Iniciar();
        Miniaturas.Lista += _tarjetas.Invalidate;
        FormClosed += (_, _) => Miniaturas.Lista -= _tarjetas.Invalidate;
        Deactivate += (_, _) =>
        {
            if (DateTime.Now < _graciaHasta)
            {
                var t = new System.Windows.Forms.Timer { Interval = 700 };
                t.Tick += (_, _) =>
                {
                    t.Dispose();
                    if (!IsDisposed && Visible) Activate();
                };
                t.Start();
                return;
            }
            BeginInvoke(Close);
        };
        Shown += (_, _) => _buscar.Box.Focus();
        Construir();
    }

    private Rectangle CerrarRect => new(_cabecera.Width - 46, 14, 32, 32);

    private EstadoMonitor? Monitor => _estado().Monitores.FirstOrDefault(m => m.Id == _monitor);

    private EstadoZona? Zona => Monitor?.Zonas.FirstOrDefault(z => z.Zona == _zona);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Theme.BorderStrong);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void MostrarEn(Point cursor, IWin32Window owner)
    {
        Rectangle area = Screen.FromPoint(cursor).WorkingArea;
        int x = Math.Clamp(cursor.X + 8, area.X + 4, Math.Max(area.X + 4, area.Right - Width - 4));
        int y = Math.Clamp(cursor.Y - 40, area.Y + 4, Math.Max(area.Y + 4, area.Bottom - Height - 4));
        Location = new Point(x, y);
        Show(owner);
    }

    private void Construir()
    {
        if (Monitor is not { } m || Zona is not { } z)
        {
            Close();
            return;
        }
        SuspendLayout();
        foreach (Control c in _botones.Controls.Cast<Control>().ToList()) c.Dispose();
        _botones.Controls.Clear();
        foreach (AccionZona a in _acciones(m, z)) _botones.Controls.Add(new BotonAccion(a, Ejecutar));
        foreach (Control c in _pieBotones.Controls.Cast<Control>().ToList()) c.Dispose();
        _pieBotones.Controls.Clear();
        foreach (AccionZona a in _pie(m, z))
        {
            var b = new ModernButton(a.Texto, ButtonKind.Ghost, a.Icono) { Enabled = a.Activa, Margin = new Padding(6, 0, 0, 0) };
            b.Size = b.GetPreferredSize(Size.Empty);
            AccionZona captured = a;
            b.Click += (_, _) => Ejecutar(captured, b);
            _pieBotones.Controls.Add(b);
        }
        bool contenido = !z.Desactivada && z.Estado != EstadosZona.Abriendo;
        _busqueda.Visible = _filtros.Visible = contenido;
        _seguir.Visible = contenido;
        _tarjetas.Visible = contenido;
        _vacio.Visible = !contenido;
        _vacio.Text = z.Desactivada ? "Esta zona no se usa. Pulsa «Usar esta zona» para mostrar algo en ella."
                                    : "La zona se está abriendo…";
        Recargar();
        ResumeLayout(true);
        _cabecera.Invalidate();
    }

    private void Ejecutar(AccionZona a, Control c)
    {
        a.Clic(c);
        if (a.Cierra && !IsDisposed) Close();
    }

    public void Actualizar()
    {
        if (IsDisposed) return;
        if (Monitor is null || Zona is null)
        {
            Close();
            return;
        }
        Point scroll = _desplazable.AutoScrollPosition;
        bool foco = _buscar.Box.Focused;
        Construir();
        _desplazable.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        if (foco) _buscar.Box.Focus();
    }

    private string? DondeEsta(string url)
    {
        if (_pendientes.TryGetValue(url, out var p)) return p.Monitor == _monitor && p.Zona == _zona ? "" : Etiqueta(p.Monitor, p.Zona);
        foreach (EstadoMonitor m in _estado().Monitores)
            foreach (EstadoZona z in m.Zonas)
            {
                bool esta = string.Equals(z.Url, url, StringComparison.OrdinalIgnoreCase) ||
                            z.Pestanas?.Any(t => string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase)) == true;
                if (!esta) continue;
                return m.Id == _monitor && z.Zona == _zona ? "" : Etiqueta(m.Id, z.Zona);
            }
        return null;
    }

    private string Etiqueta(string monitor, int zona)
    {
        EstadoMonitor? m = _estado().Monitores.FirstOrDefault(x => x.Id == monitor);
        if (m is null) return "En otra zona";
        EstadoZona? z = m.Zonas.FirstOrDefault(x => x.Zona == zona);
        int usables = m.Usables.Count();
        return usables > 1 && z is not null ? $"En monitor {m.Numero} · zona {m.NumeroVisible(z)}" : $"En monitor {m.Numero}";
    }

    private void Recargar()
    {
        string q = _buscar.Box.Text.Trim();
        List<LinkDef> todos = LinkStore.Links.ToList();
        var porTipo = todos.GroupBy(l => TiposEnlace.De(l.Url)).ToDictionary(g => g.Key, g => g.Count());

        _filtros.SuspendLayout();
        foreach (Control c in _filtros.Controls.Cast<Control>().ToList()) c.Dispose();
        _filtros.Controls.Clear();
        AgregarFiltro(null, "Todo", todos.Count);
        foreach (TipoEnlace t in Enum.GetValues<TipoEnlace>())
            if (porTipo.TryGetValue(t, out int n) && n > 0) AgregarFiltro(t, TiposEnlace.Grupo(t), n);
        _filtros.ResumeLayout(true);
        if (_filtro is { } f && !porTipo.ContainsKey(f)) _filtro = null;

        var lista = todos
            .Where(l => _filtro is null || TiposEnlace.De(l.Url) == _filtro)
            .Where(l => q.Length == 0 || l.Titulo.Contains(q, StringComparison.CurrentCultureIgnoreCase) || l.Url.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(l => new TarjetaEnlace(l, TiposEnlace.De(l.Url), DondeEsta(l.Url)))
            .ToList();
        _tarjetas.Fijar(lista, _desplazable.ClientSize.Width - _desplazable.Padding.Horizontal);
        _desplazable.AutoScrollPosition = Point.Empty;
        if (lista.Count == 0)
        {
            _vacio.Visible = true;
            _vacio.Text = todos.Count == 0 ? "La lista de enlaces está vacía. Usa «Agregar archivo o URL…»." : "No hay nada que coincida con la búsqueda.";
        }
        else if (Zona is { Desactivada: false } z && z.Estado != EstadosZona.Abriendo) _vacio.Visible = false;
    }

    private void AgregarFiltro(TipoEnlace? tipo, string texto, int n)
    {
        var chip = new ChipFiltro($"{(tipo is { } t ? TiposEnlace.Icono(t) + "  " : "")}{texto}  {n}", _filtro == tipo);
        chip.Click += (_, _) =>
        {
            _filtro = tipo;
            Recargar();
        };
        _filtros.Controls.Add(chip);
    }

    private void Elegir(TarjetaEnlace t)
    {
        if (Monitor is not { } m || Zona is not { } z) return;
        if (t.Donde is not null) return;
        _elegir(m, z, t.Enlace);
        _pendientes[t.Enlace.Url] = (_monitor, _zona);
        _asignadas.Add(_zona);
        if (!_seguir.Checked)
        {
            Close();
            return;
        }
        _graciaHasta = DateTime.Now.AddSeconds(6);
        EstadoZona? siguiente = Siguiente(m);
        if (siguiente is null)
        {
            Close();
            return;
        }
        _zona = siguiente.Zona;
        _buscar.Box.Clear();
        Construir();
        _buscar.Box.Focus();
    }

    private EstadoZona? Siguiente(EstadoMonitor m)
    {
        List<EstadoZona> usables = m.Usables.ToList();
        int desde = usables.FindIndex(z => z.Zona == _zona);
        for (int k = 1; k <= usables.Count; k++)
        {
            EstadoZona z = usables[(desde + k) % usables.Count];
            if (z.Zona == _zona || _asignadas.Contains(z.Zona) || z.Ocupada) continue;
            return z;
        }
        return null;
    }

    private void PintarCabecera(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Smooth(g);
        if (Monitor is not { } m || Zona is not { } z) return;
        var mini = new Rectangle(16, 14, 80, 50);
        Theme.FillRound(g, Theme.SidebarHover, mini, 6);
        GridLayout grilla = m.Layout();
        var dentro = Rectangle.Inflate(mini, -4, -4);
        float cw = dentro.Width / (float)grilla.Cols, rh = dentro.Height / (float)grilla.Rows;
        for (int i = 0; i < grilla.Zones.Count; i++)
        {
            ZoneSpec s = grilla.Zones[i];
            var r = new RectangleF(dentro.X + s.C * cw + 1, dentro.Y + s.R * rh + 1, s.CS * cw - 2, s.RS * rh - 2);
            EstadoZona? ez = m.Zonas.FirstOrDefault(x => x.Zona == i);
            Color c = i == _zona ? Theme.Accent
                : s.Off ? Theme.Sidebar
                : ez?.Ocupada == true || _asignadas.Contains(i) ? Color.FromArgb(71, 85, 105)
                : Color.FromArgb(51, 65, 85);
            Theme.FillRound(g, c, r, 3);
        }
        int usables = m.Usables.Count();
        string titulo = $"Monitor {m.Numero}" + (z.Desactivada ? " · zona sin usar" : usables > 1 ? $" · zona {m.NumeroVisible(z)} de {usables}" : "") +
                        (m.Bloqueado ? "  · 🔒 bloqueado" : "");
        TextRenderer.DrawText(g, titulo, Theme.Section, new Rectangle(110, 14, _cabecera.Width - 170, 24), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string muestra = z.Url is null ? "Vacía · elige abajo qué mostrar"
            : (z.Estado == EstadosZona.Abriendo ? "Abriendo: " : "Muestra: ") + TiposEnlace.Icono(TiposEnlace.De(z.Url)) + " " + (z.Titulo ?? z.Url);
        TextRenderer.DrawText(g, muestra, Theme.Body, new Rectangle(110, 40, _cabecera.Width - 170, 22), Theme.SidebarText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        Rectangle x = CerrarRect;
        if (x.Contains(_cabecera.PointToClient(Cursor.Position))) Theme.FillRound(g, Theme.SidebarHover, x, 8);
        TextRenderer.DrawText(g, "✕", Theme.Section, x, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    public static void MostrarSubmenu(ToolStripMenuItem sub, Control junto)
    {
        ContextMenuStrip menu = Theme.Menu();
        menu.Items.AddRange(sub.DropDownItems.Cast<ToolStripItem>().ToArray());
        menu.Closed += (_, _) => junto.BeginInvoke(menu.Dispose);
        menu.Show(junto, new Point(0, junto.Height));
    }
}

internal sealed record TarjetaEnlace(LinkDef Enlace, TipoEnlace Tipo, string? Donde)
{
    public bool Aqui => Donde == "";
    public bool EnOtra => Donde is { Length: > 0 };
}

internal sealed class BotonAccion : Control
{
    private static readonly Font Grande = Theme.Ui(15f);

    private readonly AccionZona _accion;
    private readonly Action<AccionZona, Control> _ejecutar;
    private bool _encima;
    private readonly ToolTip? _ayuda;

    public BotonAccion(AccionZona accion, Action<AccionZona, Control> ejecutar)
    {
        _accion = accion;
        _ejecutar = ejecutar;
        Size = new Size(78, 72);
        Margin = new Padding(0, 0, 6, 0);
        Enabled = accion.Activa;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        AccessibleName = accion.Texto;
        if (accion.Ayuda is not null)
        {
            _ayuda = new ToolTip();
            _ayuda.SetToolTip(this, accion.Ayuda);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _encima = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _encima = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Enabled) _ejecutar(_accion, this);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space && Enabled) _ejecutar(_accion, this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fondo = !Enabled ? Theme.SurfaceAlt
            : _encima ? (_accion.Peligro ? Theme.DangerSoft : Theme.AccentSoft)
            : Theme.SurfaceAlt;
        Theme.FillRound(g, fondo, r, 10);
        Theme.DrawRound(g, Focused ? Theme.Accent : _encima ? (_accion.Peligro ? Theme.Danger : Theme.Accent) : Theme.Border, r, 10, Focused ? 1.6f : 1f);
        Color tinta = !Enabled ? Theme.TextSubtle : _accion.Peligro ? Theme.Danger : Theme.Accent;
        TextRenderer.DrawText(g, _accion.Icono, Grande, new Rectangle(0, 6, Width, 34), tinta,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _accion.Texto, Theme.Small, new Rectangle(2, 40, Width - 4, Height - 44), Enabled ? Theme.Text : Theme.TextSubtle,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _ayuda?.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class ChipFiltro : Control
{
    private readonly bool _marcado;
    private bool _encima;

    public ChipFiltro(string texto, bool marcado)
    {
        Text = texto;
        _marcado = marcado;
        Font = Theme.Small;
        Cursor = Cursors.Hand;
        Size = new Size(TextRenderer.MeasureText(texto, Theme.SmallBold).Width + 22, 28);
        Margin = new Padding(2, 2, 4, 2);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _encima = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _encima = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, _marcado ? Theme.Accent : _encima ? Theme.AccentSoft : Theme.SurfaceAlt, r, 14);
        if (!_marcado) Theme.DrawRound(g, Theme.Border, r, 14);
        TextRenderer.DrawText(g, Text, _marcado ? Theme.SmallBold : Theme.Small, ClientRectangle, _marcado ? Color.White : Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

internal sealed class LienzoTarjetas : Control
{
    private static readonly Size Celda = new(152, 122);
    private static readonly Font Icono = Theme.Ui(22f);
    private const int Hueco = 10;

    private List<TarjetaEnlace> _items = new();
    private int _columnas = 1;
    private int _encima = -1;
    private int _seleccion = -1;

    public event Action<TarjetaEnlace>? Elegida;
    public event Action<char>? Tecla;

    public LienzoTarjetas()
    {
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        BackColor = Theme.Surface;
    }

    public int Cantidad => _items.Count;

    public int Seleccion
    {
        get => _seleccion;
        set
        {
            _seleccion = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            Mostrar(_seleccion);
            Invalidate();
        }
    }

    public void Fijar(List<TarjetaEnlace> items, int ancho)
    {
        _items = items;
        _encima = -1;
        _seleccion = -1;
        Ajustar(ancho);
        Invalidate();
    }

    public void Ajustar(int ancho)
    {
        ancho = Math.Max(Celda.Width, ancho);
        _columnas = Math.Max(1, (ancho + Hueco) / (Celda.Width + Hueco));
        int filas = (_items.Count + _columnas - 1) / _columnas;
        Size = new Size(ancho, Math.Max(10, filas * (Celda.Height + Hueco)));
    }

    private Rectangle RectDe(int i) => new((i % _columnas) * (Celda.Width + Hueco), (i / _columnas) * (Celda.Height + Hueco), Celda.Width, Celda.Height);

    private int En(Point p)
    {
        for (int i = 0; i < _items.Count; i++) if (RectDe(i).Contains(p)) return i;
        return -1;
    }

    private void Mostrar(int i)
    {
        if (i < 0 || Parent is not ScrollableControl sc) return;
        Rectangle r = RectDe(i);
        int arriba = -sc.AutoScrollPosition.Y;
        int alto = sc.ClientSize.Height - sc.Padding.Vertical;
        if (r.Top < arriba) sc.AutoScrollPosition = new Point(0, r.Top);
        else if (r.Bottom > arriba + alto) sc.AutoScrollPosition = new Point(0, r.Bottom - alto);
    }

    public void ElegirSeleccion()
    {
        if (_seleccion >= 0 && _seleccion < _items.Count) Elegida?.Invoke(_items[_seleccion]);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = En(e.Location);
        Cursor = i >= 0 && !_items[i].EnOtra && !_items[i].Aqui ? Cursors.Hand : Cursors.Default;
        if (i == _encima) return;
        _encima = i;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _encima = -1;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Parent is ScrollableControl sc)
            sc.AutoScrollPosition = new Point(0, Math.Max(0, -sc.AutoScrollPosition.Y - e.Delta / 120 * (Celda.Height + Hueco) / 2));
        else base.OnMouseWheel(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        int i = En(e.Location);
        if (i < 0) return;
        _seleccion = i;
        Invalidate();
        Elegida?.Invoke(_items[i]);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int s = Math.Max(0, _seleccion);
        switch (e.KeyCode)
        {
            case Keys.Left: Seleccion = s - 1; break;
            case Keys.Right: Seleccion = _seleccion < 0 ? 0 : s + 1; break;
            case Keys.Up: Seleccion = s - _columnas; break;
            case Keys.Down: Seleccion = _seleccion < 0 ? 0 : s + _columnas; break;
            case Keys.Home: Seleccion = 0; break;
            case Keys.End: Seleccion = _items.Count - 1; break;
            case Keys.Enter: ElegirSeleccion(); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!char.IsControl(e.KeyChar))
        {
            e.Handled = true;
            Tecla?.Invoke(e.KeyChar);
        }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        for (int i = 0; i < _items.Count; i++)
        {
            Rectangle r = RectDe(i);
            if (!r.IntersectsWith(e.ClipRectangle)) continue;
            Pintar(g, r, _items[i], i == _encima, i == _seleccion && Focused);
        }
    }

    private static void Pintar(Graphics g, Rectangle r, TarjetaEnlace t, bool encima, bool seleccion)
    {
        bool apagada = t.EnOtra;
        var marco = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f);
        Theme.FillRound(g, t.Aqui ? Theme.AccentSoft : encima && !apagada ? Color.FromArgb(239, 246, 255) : Theme.Surface, marco, 10);
        var foto = new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, 78);
        Image? mini = Miniaturas.De(t.Enlace.Url);
        Color tipo = TiposEnlace.Color(t.Tipo);
        if (mini is not null)
        {
            using Region antes = g.Clip;
            g.SetClip(foto, System.Drawing.Drawing2D.CombineMode.Intersect);
            using (var negro = new SolidBrush(Color.Black)) g.FillRectangle(negro, foto);
            g.DrawImage(mini, DibujoPerfil.Ajustar(mini.Size, foto));
            g.Clip = antes;
        }
        else
        {
            Theme.FillRound(g, Color.FromArgb(28, tipo), foto, 8);
            TextRenderer.DrawText(g, TiposEnlace.Icono(t.Tipo), Icono, foto, tipo,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        var insignia = new Rectangle(foto.X + 4, foto.Y + 4, 24, 20);
        Theme.FillRound(g, Color.FromArgb(220, tipo), insignia, 6);
        TextRenderer.DrawText(g, TiposEnlace.Icono(t.Tipo), Theme.Small, insignia, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (apagada)
        {
            using var velo = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
            g.FillRectangle(velo, r);
        }
        TextRenderer.DrawText(g, t.Enlace.Titulo, Theme.SmallBold, new Rectangle(r.X + 6, foto.Bottom + 3, r.Width - 12, 18),
            apagada ? Theme.TextSubtle : Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string? nota = t.Aqui ? "✓ En esta zona" : t.EnOtra ? t.Donde : null;
        if (nota is not null)
            TextRenderer.DrawText(g, nota, Theme.Small, new Rectangle(r.X + 6, foto.Bottom + 20, r.Width - 12, 16),
                t.Aqui ? Theme.Accent : Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        Color borde = seleccion ? Theme.Accent : t.Aqui ? Theme.Accent : encima && !apagada ? Theme.Accent : Theme.Border;
        Theme.DrawRound(g, borde, marco, 10, seleccion || t.Aqui ? 1.8f : 1f);
    }
}
