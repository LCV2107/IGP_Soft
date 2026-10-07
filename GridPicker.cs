namespace IGP_Soft;

internal sealed class GridPicker : Control
{
    public const int Max = GridLayout.Max;
    private const int Cell = 30;
    private const int Gap = 4;
    private const int Pad = 10;
    private const int LabelHeight = 26;

    private int _hoverCols;
    private int _hoverRows;

    public int Cols { get; }
    public int Rows { get; }

    public event Action<int, int>? Picked;

    public GridPicker(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
        (_hoverCols, _hoverRows) = (cols, rows);
        DoubleBuffered = true;
        Font = Theme.BodyBold;
        Size = new Size(Pad * 2 + Max * Cell + (Max - 1) * Gap, Pad * 2 + Max * Cell + (Max - 1) * Gap + LabelHeight);
        Cursor = Cursors.Hand;
    }

    private (int c, int r) CellAt(Point p)
    {
        int c = Math.Clamp((p.X - Pad) / (Cell + Gap) + 1, 1, Max);
        int r = Math.Clamp((p.Y - Pad) / (Cell + Gap) + 1, 1, Max);
        return (c, r);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        (int c, int r) = CellAt(e.Location);
        if (c == _hoverCols && r == _hoverRows) return;
        (_hoverCols, _hoverRows) = (c, r);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        (_hoverCols, _hoverRows) = (Cols, Rows);
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        (int c, int r) = CellAt(e.Location);
        Picked?.Invoke(c, r);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(Theme.Surface);
        Theme.Smooth(g);

        for (int r = 1; r <= Max; r++)
        for (int c = 1; c <= Max; c++)
        {
            var rect = new Rectangle(Pad + (c - 1) * (Cell + Gap), Pad + (r - 1) * (Cell + Gap), Cell, Cell);
            bool on = c <= _hoverCols && r <= _hoverRows;
            Color fill = on ? Theme.Accent : c <= Cols && r <= Rows ? Theme.AccentSoft : Color.FromArgb(241, 245, 249);
            Theme.FillRound(g, fill, rect, 6);
            if (!on) Theme.DrawRound(g, Theme.Border, rect, 6);
        }

        int zones = _hoverCols * _hoverRows;
        string text = $"{_hoverCols} × {_hoverRows}   ·   {zones} {(zones == 1 ? "zona" : "zonas")}";
        var labelRect = new Rectangle(0, Height - LabelHeight - 4, Width, LabelHeight);
        TextRenderer.DrawText(g, text, Font, labelRect, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    public static void Show(Control owner, Point screen, int cols, int rows, Action<int, int> picked, Action? advanced = null)
    {
        var picker = new GridPicker(cols, rows);
        var host = new ToolStripControlHost(picker) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = picker.Size };
        var drop = new ToolStripDropDown { Padding = Padding.Empty };
        drop.Items.Add(host);
        if (advanced is not null)
        {
            drop.Items.Add(new ToolStripSeparator());
            drop.Items.Add(new ToolStripMenuItem("⊞  Unir zonas o elegir cuáles usar…", null, (_, _) =>
            {
                drop.Close();
                owner.BeginInvoke(advanced);
            }) { Padding = new Padding(4) });
        }
        picker.Picked += (c, r) =>
        {
            drop.Close();
            picked(c, r);
        };
        drop.Closed += (_, _) => owner.BeginInvoke(drop.Dispose);
        drop.Show(screen);
    }
}
