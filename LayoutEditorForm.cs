namespace IGP_Soft;

internal sealed class LayoutEditorForm : Form
{
    private readonly GridLayout _original;
    private readonly IReadOnlyDictionary<int, string> _contents;
    private readonly LayoutCanvas _canvas;
    private readonly NumericUpDown _cols = new();
    private readonly NumericUpDown _rows = new();
    private readonly ModernButton _btnMerge = new("Unir en una zona", ButtonKind.Primary, "⊞");
    private readonly ModernButton _btnSplit = new("Separar en celdas", ButtonKind.Secondary, "⊟");
    private readonly ModernButton _btnToggle = new("No usar estas zonas", ButtonKind.Secondary, "⊘");
    private readonly Label _status = new();
    private bool _syncing = true;

    public GridLayout Result { get; private set; }

    public LayoutEditorForm(int monitorNumber, Size monitorSize, GridLayout current, IReadOnlyDictionary<int, string> contents,
        IReadOnlyList<Rectangle>? miembros = null)
    {
        _original = current;
        _contents = contents;
        Result = current;
        miembros ??= Array.Empty<Rectangle>();

        Theme.StyleWindow(this, darkCaption: false);
        Text = miembros.Count > 1 ? $"Diseñar grilla — Monitor {monitorNumber} ({miembros.Count} monitores combinados)" : $"Diseñar grilla — Monitor {monitorNumber}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 720);
        MinimumSize = new Size(880, 600);
        ShowInTaskbar = false;

        float aspecto = monitorSize.Height > 0 ? (float)monitorSize.Width / monitorSize.Height : 16f / 9f;

        var divisiones = Fila();
        divisiones.Controls.Add(Etiqueta("Dividir en"));
        foreach ((int c, int r) in GridLayout.Divisiones)
            AgregarBoton(divisiones, $"{c}×{r}", $"{c} columnas × {r} filas · {c * r} zonas", GridLayout.Uniform(c, r), aspecto);
        var muro = Fila();
        IReadOnlyList<(string Name, int Cols, int Rows)> atajos = GridLayout.AtajosDeGrupo(miembros);
        if (atajos.Count > 0)
        {
            muro.Controls.Add(Etiqueta("Videowall"));
            foreach ((string name, int c, int r) in atajos)
                AgregarBoton(muro, name, $"{name}: {c} × {r}, alineada con los bordes de cada monitor", GridLayout.Uniform(c, r), aspecto);
        }

        var modelos = Fila();
        modelos.Controls.Add(Etiqueta("Columnas"));
        _cols.SetBounds(0, 0, 50, 28);
        _cols.Margin = new Padding(0, 14, 16, 0);
        modelos.Controls.Add(_cols);
        modelos.Controls.Add(Etiqueta("Filas"));
        _rows.SetBounds(0, 0, 50, 28);
        _rows.Margin = new Padding(0, 14, 24, 0);
        modelos.Controls.Add(_rows);
        foreach (NumericUpDown n in new[] { _cols, _rows })
        {
            n.Minimum = 1;
            n.Maximum = GridLayout.Max;
            n.ValueChanged += (_, _) =>
            {
                if (_syncing) return;
                SetLayout(GridLayout.Uniform((int)_cols.Value, (int)_rows.Value));
            };
        }
        modelos.Controls.Add(Etiqueta("Modelos"));
        foreach ((string name, GridLayout layout) in GridLayout.Presets)
            AgregarBoton(modelos, name, name, layout, aspecto);

        var top = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 10, 16, 0), BackColor = Theme.Background };
        modelos.Dock = DockStyle.Top;
        divisiones.Dock = DockStyle.Top;
        muro.Dock = DockStyle.Top;
        top.Controls.Add(modelos);
        top.Controls.Add(divisiones);
        if (atajos.Count > 0) top.Controls.Add(muro);
        else muro.Dispose();

        _canvas = new LayoutCanvas(monitorSize, contents) { Dock = DockStyle.Fill, Miembros = miembros };
        _canvas.SelectionChanged += UpdateButtons;

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 280, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(16, 8, 16, 8), BackColor = Theme.Background,
        };
        right.Controls.Add(new Label
        {
            AutoSize = false, Size = new Size(248, 150), ForeColor = Theme.TextMuted,
            Text = "Elige una división arriba: el lienzo muestra cómo quedará y nada cambia en las pantallas hasta «Aplicar».\n\n" +
                   "Arrastra el mouse sobre las celdas para seleccionarlas (o haz clic en una zona) y únelas, sepáralas o márcalas para no usarlas.",
        });
        var btnReset = new ModernButton("Restablecer cuadrícula", ButtonKind.Ghost, "↺");
        foreach (ModernButton b in new[] { _btnMerge, _btnSplit, _btnToggle, btnReset })
        {
            b.Size = new Size(248, 40);
            b.Margin = new Padding(0, 0, 0, 6);
            b.TextAlign = ContentAlignment.MiddleLeft;
            right.Controls.Add(b);
        }
        _btnMerge.Click += (_, _) => Merge();
        _btnSplit.Click += (_, _) => Split();
        _btnToggle.Click += (_, _) => ToggleOff();
        btnReset.Click += (_, _) => SetLayout(GridLayout.Uniform(_canvas.Grid.Cols, _canvas.Grid.Rows));

        _status.AutoSize = false;
        _status.Size = new Size(248, 170);
        _status.Margin = new Padding(0, 10, 0, 0);
        right.Controls.Add(_status);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(16, 10, 16, 12), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Background };
        var cancel = new ModernButton("Cancelar") { DialogResult = DialogResult.Cancel, Size = new Size(110, 40), Margin = new Padding(0) };
        var ok = new ModernButton("Aplicar", ButtonKind.Primary, "✓") { DialogResult = DialogResult.OK, Size = new Size(120, 40), Margin = new Padding(0, 0, 8, 0) };
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_canvas);
        Controls.Add(right);
        Controls.Add(bottom);
        Controls.Add(top);

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (_canvas.Grid.ActiveCount == 0)
            {
                MessageBox.Show(this, "Debe quedar al menos una zona en uso.", "Diseñar grilla", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            Result = _canvas.Grid;
        };

        SetLayout(current);
    }

    private void SetLayout(GridLayout layout)
    {
        _syncing = true;
        _cols.Value = layout.Cols;
        _rows.Value = layout.Rows;
        _syncing = false;
        _canvas.Grid = layout;
        _canvas.ClearSelection();
        UpdateButtons();
    }

    private readonly List<BotonDivision> _botones = new();
    private readonly ToolTip _tips = new() { InitialDelay = 300 };

    private static FlowLayoutPanel Fila() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
        Padding = new Padding(0, 0, 0, 6), BackColor = Theme.Background,
    };

    private static Label Etiqueta(string texto, int izquierda = 0) => new()
    {
        Text = texto, AutoSize = true, Margin = new Padding(izquierda, 18, 8, 0), ForeColor = Theme.TextMuted,
    };

    private void AgregarBoton(FlowLayoutPanel fila, string texto, string ayuda, GridLayout layout, float aspecto)
    {
        var b = new BotonDivision(texto, layout, aspecto) { Margin = new Padding(0, 0, 6, 0) };
        _tips.SetToolTip(b, ayuda);
        b.Click += (_, _) => SetLayout(layout);
        b.MouseEnter += (_, _) => _canvas.Fantasma = layout.SameAs(_canvas.Grid) ? null : layout;
        b.MouseLeave += (_, _) => _canvas.Fantasma = null;
        _botones.Add(b);
        fila.Controls.Add(b);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }

    private void Merge()
    {
        if (_canvas.Selection is not Rectangle sel) return;
        GridLayout l = _canvas.Grid;
        var zones = l.Zones.Where(z => !Overlaps(z, sel)).ToList();
        zones.Add(new ZoneSpec(sel.X, sel.Y, sel.Width, sel.Height));
        SetLayout(l.With(zones));
        _canvas.SelectCells(sel);
        UpdateButtons();
    }

    private void Split()
    {
        if (_canvas.Selection is not Rectangle sel) return;
        GridLayout l = _canvas.Grid;
        var zones = new List<ZoneSpec>();
        foreach (ZoneSpec z in l.Zones)
        {
            if (!Overlaps(z, sel) || (z.CS == 1 && z.RS == 1)) { zones.Add(z); continue; }
            for (int c = z.C; c < z.C + z.CS; c++)
                for (int r = z.R; r < z.R + z.RS; r++)
                    zones.Add(new ZoneSpec(c, r, 1, 1, z.Off));
        }
        SetLayout(l.With(zones));
        _canvas.SelectCells(sel);
        UpdateButtons();
    }

    private void ToggleOff()
    {
        if (_canvas.Selection is not Rectangle sel) return;
        GridLayout l = _canvas.Grid;
        bool anyOn = l.Zones.Any(z => Overlaps(z, sel) && !z.Off);
        var zones = l.Zones.Select(z => Overlaps(z, sel) ? z with { Off = anyOn } : z).ToList();
        SetLayout(l.With(zones));
        _canvas.SelectCells(sel);
        UpdateButtons();
    }

    private static bool Overlaps(ZoneSpec z, Rectangle cells) =>
        z.C < cells.Right && z.C + z.CS > cells.Left && z.R < cells.Bottom && z.R + z.RS > cells.Top;

    private void UpdateButtons()
    {
        GridLayout l = _canvas.Grid;
        Rectangle? sel = _canvas.Selection;
        var selected = sel is Rectangle s ? l.Zones.Where(z => Overlaps(z, s)).ToList() : new List<ZoneSpec>();
        _btnMerge.Enabled = selected.Count > 1;
        _btnSplit.Enabled = selected.Any(z => z.CS > 1 || z.RS > 1);
        _btnToggle.Enabled = selected.Count > 0;
        bool allOff = selected.Count > 0 && selected.All(z => z.Off);
        _btnToggle.Text = allOff ? "Usar estas zonas" : "No usar estas zonas";
        _btnToggle.Glyph = allOff ? "✓" : "⊘";
        _btnToggle.Invalidate();

        Dictionary<int, int> carry = GridLayout.Carry(_original, _contents.Keys, l);
        var closing = _contents.Where(kv => !carry.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        string text = $"{l.ActiveCount} zona(s) en uso" + (l.Zones.Count > l.ActiveCount ? $", {l.Zones.Count - l.ActiveCount} sin usar" : "") + ".";
        if (closing.Count > 0)
        {
            _status.ForeColor = Theme.Warning;
            _status.Text = text + "\n\nAl aplicar se cerrará:\n• " + string.Join("\n• ", closing);
        }
        else
        {
            _status.ForeColor = Theme.TextMuted;
            _status.Text = text + (_contents.Count > 0 ? "\n\nLo que está abierto se mantiene." : "");
        }
        _canvas.Carry = carry;
        _canvas.Fantasma = null;
        _canvas.Invalidate();
        foreach (BotonDivision b in _botones) b.Marcado = b.Grilla.SameAs(l);
    }
}

internal sealed class BotonDivision : Control
{
    private const int AnchoMax = 52;
    private const int AltoMax = 30;
    private readonly Size _dibujo;
    private bool _encima;
    private bool _marcado;

    public GridLayout Grilla { get; }

    public bool Marcado
    {
        get => _marcado;
        set { if (_marcado == value) return; _marcado = value; Invalidate(); }
    }

    public BotonDivision(string texto, GridLayout layout, float aspecto)
    {
        Text = texto;
        Grilla = layout;
        aspecto = Math.Clamp(aspecto, 0.5f, 4f);
        int ancho = Math.Min(AnchoMax, (int)(AltoMax * aspecto));
        _dibujo = new Size(ancho, Math.Max(12, (int)(ancho / aspecto)));
        DoubleBuffered = true;
        Font = Theme.Small;
        Cursor = Cursors.Hand;
        int textoAncho = TextRenderer.MeasureText(texto, Font).Width;
        Size = new Size(Math.Max(ancho, textoAncho) + 14, AltoMax + 30);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _encima = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _encima = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Theme.Smooth(g);
        var fondo = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_marcado || _encima)
            Theme.FillRound(g, _marcado ? Theme.AccentSoft : Theme.Surface, fondo, 8);
        if (_marcado) Theme.DrawRound(g, Theme.Accent, fondo, 8, 1.5f);
        else if (_encima) Theme.DrawRound(g, Theme.BorderStrong, fondo, 8);

        var tablero = new Rectangle((Width - _dibujo.Width) / 2, 6 + (AltoMax - _dibujo.Height) / 2, _dibujo.Width, _dibujo.Height);
        foreach (ZoneSpec z in Grilla.Zones)
        {
            Rectangle r = Grilla.AreaOf(z, tablero);
            r.Inflate(-1, -1);
            if (r.Width < 1 || r.Height < 1) continue;
            Theme.FillRound(g, z.Off ? Theme.BorderStrong : _marcado || _encima ? Theme.Accent : Theme.TextSubtle, r, 2);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 6 + AltoMax + 3, Width, Height - AltoMax - 9),
            _marcado ? Theme.Accent : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
    }
}

internal sealed class LayoutCanvas : Control
{
    private readonly Size _monitor;
    private readonly IReadOnlyDictionary<int, string> _contents;
    private Point? _dragStart;
    private Point _dragEnd;

    public GridLayout Grid { get; set; } = GridLayout.Uniform(1, 1);
    public Rectangle? Selection { get; private set; }
    public Dictionary<int, int> Carry { get; set; } = new();
    public IReadOnlyList<Rectangle> Miembros { get; set; } = Array.Empty<Rectangle>();

    private GridLayout? _fantasma;

    public GridLayout? Fantasma
    {
        get => _fantasma;
        set { if (_fantasma == value) return; _fantasma = value; Invalidate(); }
    }

    public event Action? SelectionChanged;

    public LayoutCanvas(Size monitor, IReadOnlyDictionary<int, string> contents)
    {
        _monitor = monitor;
        _contents = contents;
        DoubleBuffered = true;
        BackColor = Theme.Background;
        Cursor = Cursors.Cross;
    }

    private Rectangle Board()
    {
        var area = Rectangle.Inflate(ClientRectangle, -24, -24);
        double scale = Math.Min((double)area.Width / _monitor.Width, (double)area.Height / _monitor.Height);
        int w = (int)(_monitor.Width * scale), h = (int)(_monitor.Height * scale);
        return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    private Point CellAt(Point p)
    {
        Rectangle b = Board();
        int c = Math.Clamp((int)((p.X - b.X) * (double)Grid.Cols / Math.Max(1, b.Width)), 0, Grid.Cols - 1);
        int r = Math.Clamp((int)((p.Y - b.Y) * (double)Grid.Rows / Math.Max(1, b.Height)), 0, Grid.Rows - 1);
        return new Point(c, r);
    }

    public void ClearSelection()
    {
        Selection = null;
        Invalidate();
    }

    public void SelectCells(Rectangle cells)
    {
        Selection = Expand(Rectangle.Intersect(cells, new Rectangle(0, 0, Grid.Cols, Grid.Rows)));
        if (Selection is { IsEmpty: true }) Selection = null;
        Invalidate();
    }

    private Rectangle Expand(Rectangle cells)
    {
        bool grown = true;
        while (grown)
        {
            grown = false;
            foreach (ZoneSpec z in Grid.Zones)
            {
                if (!(z.C < cells.Right && z.C + z.CS > cells.Left && z.R < cells.Bottom && z.R + z.RS > cells.Top)) continue;
                var u = Rectangle.Union(cells, new Rectangle(z.C, z.R, z.CS, z.RS));
                if (u != cells) { cells = u; grown = true; }
            }
        }
        return cells;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Board().Contains(e.Location)) return;
        _dragStart = CellAt(e.Location);
        _dragEnd = _dragStart.Value;
        UpdateDragSelection();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is null) return;
        Point cell = CellAt(e.Location);
        if (cell == _dragEnd) return;
        _dragEnd = cell;
        UpdateDragSelection();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragStart = null;
    }

    private void UpdateDragSelection()
    {
        if (_dragStart is not Point a) return;
        Point b = _dragEnd;
        var cells = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        Selection = Expand(cells);
        Invalidate();
        SelectionChanged?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Rectangle board = Board();
        Theme.Smooth(g);
        Theme.Shadow(g, board, 14, 5);
        Theme.FillRound(g, Color.FromArgb(30, 41, 59), board, 14);

        using var cellPen = new Pen(Color.FromArgb(70, 255, 255, 255)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
        for (int c = 1; c < Grid.Cols; c++)
        {
            int x = board.X + board.Width * c / Grid.Cols;
            g.DrawLine(cellPen, x, board.Top, x, board.Bottom);
        }
        for (int r = 1; r < Grid.Rows; r++)
        {
            int y = board.Y + board.Height * r / Grid.Rows;
            g.DrawLine(cellPen, board.Left, y, board.Right, y);
        }

        var kept = Carry.ToDictionary(kv => kv.Value, kv => _contents[kv.Key]);
        Font numFont = Theme.Heading;
        Font smallFont = Theme.Small;
        int number = 0;
        for (int i = 0; i < Grid.Zones.Count; i++)
        {
            ZoneSpec z = Grid.Zones[i];
            Rectangle rect = Rectangle.Inflate(Grid.AreaOf(z, board), -3, -3);
            if (z.Off)
            {
                using var hatch = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.BackwardDiagonal,
                    Color.FromArgb(71, 85, 105), Color.FromArgb(51, 65, 85));
                g.FillRectangle(hatch, rect);
                TextRenderer.DrawText(g, "sin usar", smallFont, rect, Color.Gainsboro,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                continue;
            }

            number++;
            bool hasContent = kept.TryGetValue(i, out string? title);
            Theme.FillRound(g, hasContent ? Theme.Success : Theme.Accent, rect, 8);
            TextRenderer.DrawText(g, number.ToString(), numFont, rect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (hasContent)
            {
                var band = new Rectangle(rect.X, rect.Bottom - 22, rect.Width, 22);
                Theme.FillRound(g, Color.FromArgb(90, 0, 0, 0), band, 6);
                TextRenderer.DrawText(g, "✔ " + title, smallFont, band, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        if (Selection is Rectangle sel)
        {
            var zs = new ZoneSpec(sel.X, sel.Y, sel.Width, sel.Height);
            Rectangle rect = Rectangle.Inflate(Grid.AreaOf(zs, board), -1, -1);
            Theme.FillRound(g, Color.FromArgb(70, 250, 204, 21), rect, 9);
            Theme.DrawRound(g, Color.FromArgb(250, 204, 21), rect, 9, 3f);
        }

        if (_fantasma is { } f)
        {
            using var velo = new SolidBrush(Color.FromArgb(200, 30, 41, 59));
            g.FillRectangle(velo, board);
            foreach (ZoneSpec z in f.Zones)
            {
                Rectangle rect = Rectangle.Inflate(f.AreaOf(z, board), -4, -4);
                Theme.FillRound(g, Color.FromArgb(z.Off ? 20 : 45, 250, 204, 21), rect, 8);
                Theme.DrawRound(g, Color.FromArgb(250, 204, 21), rect, 8, 2f);
            }
            int n = f.ActiveCount;
            TextRenderer.DrawText(g, $"{f.Describe()} · {n} {(n == 1 ? "zona" : "zonas")} · clic para ver cómo queda", Theme.SmallBold,
                new Rectangle(board.X, board.Bottom + 2, board.Width, 20), Theme.TextMuted, TextFormatFlags.HorizontalCenter);
        }

        if (Miembros.Count > 1)
        {
            double sx = (double)board.Width / _monitor.Width, sy = (double)board.Height / _monitor.Height;
            using var sombra = new Pen(Color.FromArgb(200, 15, 23, 42), 5f);
            using var linea = new Pen(Color.FromArgb(235, 255, 255, 255), 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            foreach (Rectangle m in Miembros)
            {
                int x0 = board.X + (int)Math.Round(m.Left * sx), y0 = board.Y + (int)Math.Round(m.Top * sy);
                int x1 = board.X + (int)Math.Round(m.Right * sx), y1 = board.Y + (int)Math.Round(m.Bottom * sy);
                foreach (Pen pen in new[] { sombra, linea })
                {
                    if (x0 > board.Left + 2) g.DrawLine(pen, x0, y0, x0, y1);
                    if (y0 > board.Top + 2) g.DrawLine(pen, x0, y0, x1, y0);
                }
            }
            TextRenderer.DrawText(g, "- - -  bordes de cada monitor", Theme.Small,
                new Rectangle(board.X, board.Top - 20, board.Width, 18), Theme.TextMuted, TextFormatFlags.Right);
        }
    }
}
