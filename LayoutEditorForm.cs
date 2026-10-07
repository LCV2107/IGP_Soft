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

    public LayoutEditorForm(int monitorNumber, Size monitorSize, GridLayout current, IReadOnlyDictionary<int, string> contents)
    {
        _original = current;
        _contents = contents;
        Result = current;

        Theme.StyleWindow(this, darkCaption: false);
        Text = $"Diseñar grilla — Monitor {monitorNumber}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1020, 680);
        MinimumSize = new Size(820, 560);
        ShowInTaskbar = false;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(16, 14, 16, 0), WrapContents = false, BackColor = Theme.Background };
        top.Controls.Add(new Label { Text = "Columnas", AutoSize = true, Margin = new Padding(0, 9, 6, 0), ForeColor = Theme.TextMuted });
        _cols.SetBounds(0, 0, 50, 28);
        _cols.Margin = new Padding(0, 5, 16, 0);
        top.Controls.Add(_cols);
        top.Controls.Add(new Label { Text = "Filas", AutoSize = true, Margin = new Padding(0, 9, 6, 0), ForeColor = Theme.TextMuted });
        _rows.SetBounds(0, 0, 50, 28);
        _rows.Margin = new Padding(0, 5, 24, 0);
        top.Controls.Add(_rows);
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
        top.Controls.Add(new Label { Text = "Listas:", AutoSize = true, Margin = new Padding(0, 9, 8, 0), ForeColor = Theme.TextMuted });
        foreach ((string name, GridLayout layout) in GridLayout.Presets)
        {
            GridLayout captured = layout;
            ModernButton b = Page.ToolButton(name, () => SetLayout(captured));
            b.Margin = new Padding(0, 0, 6, 0);
            top.Controls.Add(b);
        }

        _canvas = new LayoutCanvas(monitorSize, contents) { Dock = DockStyle.Fill };
        _canvas.SelectionChanged += UpdateButtons;

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 280, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(16, 8, 16, 8), BackColor = Theme.Background,
        };
        right.Controls.Add(new Label
        {
            AutoSize = false, Size = new Size(248, 100), ForeColor = Theme.TextMuted,
            Text = "Arrastra el mouse sobre las celdas para seleccionarlas (o haz clic en una zona).\n\n" +
                   "Luego únelas en una sola zona, sepáralas o márcalas para no usarlas.",
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
        _canvas.Invalidate();
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
    }
}
