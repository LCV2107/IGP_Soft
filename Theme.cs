using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace IGP_Soft;

internal static class Theme
{
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
    public static readonly Color AccentSoft = Color.FromArgb(219, 234, 254);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color SuccessSoft = Color.FromArgb(220, 252, 231);
    public static readonly Color Warning = Color.FromArgb(202, 138, 4);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color DangerSoft = Color.FromArgb(254, 226, 226);

    public static readonly Color Background = Color.FromArgb(244, 246, 250);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color BorderStrong = Color.FromArgb(203, 213, 225);
    public static readonly Color Text = Color.FromArgb(15, 23, 42);
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
    public static readonly Color TextSubtle = Color.FromArgb(148, 163, 184);

    public static readonly Color Sidebar = Color.FromArgb(15, 23, 42);
    public static readonly Color SidebarHover = Color.FromArgb(30, 41, 59);
    public static readonly Color SidebarText = Color.FromArgb(203, 213, 225);
    public static readonly Color SidebarMuted = Color.FromArgb(100, 116, 139);

    public static readonly string FontName = PickFont();

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new(FontName, size, style);

    public static readonly Font Body = Ui(9.5f);
    public static readonly Font BodyBold = Ui(9.5f, FontStyle.Bold);
    public static readonly Font Small = Ui(8.5f);
    public static readonly Font SmallBold = Ui(8.5f, FontStyle.Bold);
    public static readonly Font Heading = Ui(18f, FontStyle.Bold);
    public static readonly Font Section = Ui(11f, FontStyle.Bold);

    private static string PickFont()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = Round(r, radius);
        using var b = new SolidBrush(color);
        g.FillPath(b, path);
    }

    public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        using var path = Round(r, radius);
        using var p = new Pen(color, width);
        g.DrawPath(p, path);
    }

    public static void Shadow(Graphics g, RectangleF r, float radius, int depth = 6)
    {
        for (int i = depth; i >= 1; i--)
        {
            var sr = new RectangleF(r.X - i * 0.5f, r.Y + i * 0.6f, r.Width + i, r.Height + i * 0.8f);
            FillRound(g, Color.FromArgb(Math.Max(1, 10 - i), 15, 23, 42), sr, radius + i);
        }
    }

    public static void StyleWindow(Form form, bool darkCaption)
    {
        form.BackColor = Background;
        form.Font = Body;
        form.HandleCreated += (_, _) =>
        {
            try
            {
                int corner = 2;
                DwmSetWindowAttribute(form.Handle, 33, ref corner, sizeof(int));
                if (!darkCaption) return;
                int caption = ColorTranslator.ToWin32(Sidebar);
                int text = ColorTranslator.ToWin32(Color.White);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
            }
            catch { }
        };
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = Surface;
        grid.GridColor = Border;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersVisible = false;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = 38;
        grid.RowTemplate.Height = 36;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SurfaceAlt, ForeColor = TextMuted, Font = SmallBold,
            SelectionBackColor = SurfaceAlt, SelectionForeColor = TextMuted, Padding = new Padding(10, 0, 0, 0),
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Text, Font = Body,
            SelectionBackColor = AccentSoft, SelectionForeColor = Text, Padding = new Padding(10, 0, 6, 0),
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(251, 252, 254) };
    }

    public static ContextMenuStrip Menu()
    {
        var m = new ContextMenuStrip { Font = Body, Padding = new Padding(4), ShowImageMargin = false, ShowCheckMargin = true };
        m.ItemAdded += (_, e) => Pad(e.Item);
        return m;
    }

    public static void Pad(ToolStripItem? item)
    {
        if (item is ToolStripMenuItem mi)
        {
            mi.Padding = new Padding(4, 6, 12, 6);
            mi.DropDown.Padding = new Padding(4);
            if (mi.DropDown is ToolStripDropDownMenu dm) { dm.ShowImageMargin = false; dm.ShowCheckMargin = true; }
            foreach (ToolStripItem child in mi.DropDownItems) Pad(child);
            mi.DropDown.ItemAdded += (_, e) => Pad(e.Item);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

internal enum ButtonKind { Primary, Secondary, Ghost, Danger, Nav }

internal sealed class ModernButton : Button
{
    private bool _hover;
    private bool _down;
    private bool _selected;

    public ButtonKind Kind { get; set; }
    public string Glyph { get; set; } = "";
    public string Description { get; set; } = "";
    public int Radius { get; set; } = 8;

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public ModernButton(string text, ButtonKind kind = ButtonKind.Secondary, string glyph = "")
    {
        Text = text;
        Kind = kind;
        Glyph = glyph;
        Font = Theme.Body;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Height = 36;
        Margin = new Padding(0, 0, 8, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = true;
    }

    public override Size GetPreferredSize(Size proposed)
    {
        Size t = TextRenderer.MeasureText(Text, Font);
        int glyph = Glyph.Length > 0 ? 22 : 0;
        if (Description.Length > 0)
        {
            Size d = TextRenderer.MeasureText(Description, Theme.Small);
            return new Size(Math.Max(t.Width, d.Width) + glyph + 40, 64);
        }
        return new Size(t.Width + glyph + 32, Math.Max(36, Height));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Color parentBack = Parent?.BackColor ?? Theme.Background;
        if (parentBack == Color.Transparent) parentBack = Theme.Background;
        g.Clear(parentBack);
        Theme.Smooth(g);

        (Color back, Color fore, Color border) = Colors();
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (back.A > 0) Theme.FillRound(g, back, r, Radius);
        if (border.A > 0) Theme.DrawRound(g, border, r, Radius);
        if (Kind == ButtonKind.Nav && _selected)
            Theme.FillRound(g, Color.FromArgb(147, 197, 253), new RectangleF(0, Height * 0.25f, 4, Height * 0.5f), 2);

        int x = Kind == ButtonKind.Nav ? 16 : 14;
        if (Description.Length > 0)
        {
            if (Glyph.Length > 0)
            {
                var gr = new Rectangle(x, (Height - 36) / 2, 36, 36);
                Theme.FillRound(g, Kind == ButtonKind.Primary ? Color.FromArgb(60, 255, 255, 255) : Theme.AccentSoft, gr, 8);
                using var gf = Theme.Ui(13f);
                TextRenderer.DrawText(g, Glyph, gf, gr, Kind == ButtonKind.Primary ? Color.White : Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += 48;
            }
            TextRenderer.DrawText(g, Text, Theme.BodyBold, new Rectangle(x, Height / 2 - 20, Width - x - 8, 20), fore,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Description, Theme.Small, new Rectangle(x, Height / 2 + 1, Width - x - 8, 34),
                Kind == ButtonKind.Primary ? Color.FromArgb(219, 234, 254) : Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            return;
        }

        bool left = Kind == ButtonKind.Nav || TextAlign == ContentAlignment.MiddleLeft;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    (left ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        Rectangle textRect = new(x, 0, Width - x - 10, Height);
        if (Glyph.Length > 0)
        {
            if (left)
            {
                using var gf = Theme.Ui(Kind == ButtonKind.Nav ? 11.5f : 10.5f);
                TextRenderer.DrawText(g, Glyph, gf, new Rectangle(x - 2, 0, 24, Height), fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                textRect = new Rectangle(x + 28, 0, Width - x - 34, Height);
            }
            else
            {
                Size ts = TextRenderer.MeasureText(Text, Font);
                int total = ts.Width + 22;
                int sx = (Width - total) / 2;
                using var gf = Theme.Ui(10.5f);
                TextRenderer.DrawText(g, Glyph, gf, new Rectangle(sx - 2, 0, 22, Height), fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                textRect = new Rectangle(sx + 22, 0, ts.Width + 4, Height);
                flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
            }
        }
        TextRenderer.DrawText(g, Text, Kind == ButtonKind.Nav && _selected ? Theme.BodyBold : Font, textRect, fore, flags);

        if (Focused && ShowFocusCues && Kind != ButtonKind.Nav)
            Theme.DrawRound(g, Color.FromArgb(120, Theme.Accent), new RectangleF(2, 2, Width - 5, Height - 5), Radius - 2);
    }

    private (Color back, Color fore, Color border) Colors()
    {
        if (!Enabled)
            return Kind == ButtonKind.Nav
                ? (Color.Transparent, Theme.SidebarMuted, Color.Transparent)
                : (Color.FromArgb(241, 245, 249), Theme.TextSubtle, Theme.Border);
        return Kind switch
        {
            ButtonKind.Primary => (_down ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent, Color.White, Color.Transparent),
            ButtonKind.Danger => (_down ? Theme.DangerSoft : _hover ? Color.FromArgb(254, 242, 242) : Theme.Surface, Theme.Danger,
                _hover ? Color.FromArgb(252, 165, 165) : Theme.Border),
            ButtonKind.Ghost => (_down ? Theme.Border : _hover ? Color.FromArgb(241, 245, 249) : Color.Transparent, Theme.Text, Color.Transparent),
            ButtonKind.Nav => (_selected ? Theme.Accent : _down ? Theme.AccentPressed : _hover ? Theme.SidebarHover : Color.Transparent,
                _selected || _hover ? Color.White : Theme.SidebarText, Color.Transparent),
            _ => (_down ? Theme.Border : _hover ? Color.FromArgb(241, 245, 249) : Theme.Surface, Theme.Text,
                _hover ? Theme.BorderStrong : Theme.Border),
        };
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    private bool _hover;

    public ToggleSwitch(string text)
    {
        Text = text;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        AutoSize = false;
        Height = 32;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public override Size GetPreferredSize(Size proposed) =>
        new(TextRenderer.MeasureText(Text, Font).Width + 64, 32);

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.Smooth(g);
        var track = new RectangleF(1, (Height - 22) / 2f, 40, 22);
        Color on = Enabled ? Theme.Accent : Theme.TextSubtle;
        Theme.FillRound(g, Checked ? on : _hover ? Theme.BorderStrong : Color.FromArgb(214, 220, 228), track, 11);
        float knobX = Checked ? track.Right - 19 : track.X + 3;
        using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            g.FillEllipse(shadow, knobX, track.Y + 4, 16, 16);
        using (var knob = new SolidBrush(Color.White))
            g.FillEllipse(knob, knobX, track.Y + 3, 16, 16);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(52, 0, Width - 52, Height), Enabled ? Theme.Text : Theme.TextSubtle,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
            Theme.DrawRound(g, Color.FromArgb(120, Theme.Accent), new RectangleF(track.X - 1, track.Y - 1, track.Width + 1, track.Height + 1), 12);
    }
}

internal class Card : Panel
{
    public int Radius { get; set; } = 12;
    public Color Fill { get; set; } = Theme.Surface;
    public bool Elevated { get; set; } = true;

    public Card()
    {
        BackColor = Theme.Surface;
        Padding = new Padding(18);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Theme.Smooth(g);
        var r = new RectangleF(1, 1, Width - 3, Height - 4);
        if (Elevated) Theme.Shadow(g, r, Radius, 3);
        Theme.FillRound(g, Fill, r, Radius);
        Theme.DrawRound(g, Theme.Border, r, Radius);
    }
}

internal sealed class Chip : Control
{
    public Color Fill { get; set; } = Theme.Surface;
    public Color Fore { get; set; } = Theme.TextMuted;
    public string Value { get; set; } = "";

    public Chip()
    {
        Font = Theme.Small;
        Height = 28;
        Margin = new Padding(0, 0, 8, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void Set(string label, string value)
    {
        Text = label;
        Value = value;
        Width = TextRenderer.MeasureText(label, Font).Width + TextRenderer.MeasureText(value, Theme.SmallBold).Width + 26;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, Fill, r, Height / 2f);
        Theme.DrawRound(g, Theme.Border, r, Height / 2f);
        int lw = TextRenderer.MeasureText(Text, Font).Width;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(10, 0, lw, Height), Fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Value, Theme.SmallBold, new Rectangle(8 + lw, 0, Width - lw - 8, Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class RoundedTextBox : Panel
{
    public TextBox Box { get; } = new();

    public RoundedTextBox(string placeholder, int width)
    {
        Size = new Size(width, 38);
        BackColor = Theme.Surface;
        Padding = new Padding(12, 10, 12, 6);
        Box.BorderStyle = BorderStyle.None;
        Box.Font = Theme.Body;
        Box.PlaceholderText = placeholder;
        Box.Dock = DockStyle.Fill;
        Box.BackColor = Theme.Surface;
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Controls.Add(Box);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.IBeam;
        Click += (_, _) => Box.Focus();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, Theme.Surface, r, 8);
        Theme.DrawRound(g, Box.Focused ? Theme.Accent : Theme.BorderStrong, r, 8, Box.Focused ? 1.6f : 1f);
    }
}

internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new MenuColors()) { RoundedEdges = true; }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled || e.Item.IsOnDropDown == false && e.Item.Owner is MenuStrip) return;
        Theme.Smooth(e.Graphics);
        Theme.FillRound(e.Graphics, Theme.AccentSoft, new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 6);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.TextSubtle : e.Item is ToolStripLabel ? Theme.TextMuted : Theme.Text;
        if (e.Item is ToolStripLabel) e.TextFont = Theme.SmallBold;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var p = new Pen(Theme.Border);
        e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is not ToolStripDropDown) { base.OnRenderToolStripBorder(e); return; }
        using var p = new Pen(Theme.BorderStrong);
        e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        Theme.Smooth(e.Graphics);
        var r = new Rectangle(e.ImageRectangle.X, (e.Item.Height - 18) / 2, 18, 18);
        Theme.FillRound(e.Graphics, Theme.Accent, r, 5);
        TextRenderer.DrawText(e.Graphics, "✓", Theme.SmallBold, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.BorderStrong;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.AccentSoft;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Surface;
    }
}
