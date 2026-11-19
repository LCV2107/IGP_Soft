namespace IGP_Soft;

internal class Page : UserControl
{
    internal readonly Panel Body = new() { Dock = DockStyle.Fill, Padding = new Padding(28, 6, 28, 24), BackColor = Theme.Background };

    public string Title { get; }

    public Page(string title, string description)
    {
        Title = title;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        Font = Theme.Body;

        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Background };
        header.Controls.Add(new Label
        {
            Text = title, AutoSize = true, Location = new Point(26, 18),
            Font = Theme.Heading, ForeColor = Theme.Text, BackColor = Theme.Background,
        });
        header.Controls.Add(new Label
        {
            Text = description, AutoSize = true, Location = new Point(28, 54), ForeColor = Theme.TextMuted, BackColor = Theme.Background,
        });

        Controls.Add(Body);
        Controls.Add(header);
    }

    public virtual void OnPageShown() { }

    internal static ModernButton ToolButton(string text, Action click, bool primary = false)
    {
        string glyph = "";
        int sp = text.IndexOf("  ", StringComparison.Ordinal);
        if (sp > 0 && sp <= 2)
        {
            glyph = text[..sp];
            text = text[(sp + 2)..];
        }
        var b = new ModernButton(text, primary ? ButtonKind.Primary : ButtonKind.Secondary, glyph);
        b.Size = b.GetPreferredSize(Size.Empty);
        b.Click += (_, _) => click();
        return b;
    }

    internal static FlowLayoutPanel ToolBar(params Control[] items)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false, BackColor = Theme.Background, Padding = new Padding(0, 0, 0, 8) };
        bar.Controls.AddRange(items);
        return bar;
    }

    internal static Panel Gap(int height) => new() { Dock = DockStyle.Top, Height = height, BackColor = Theme.Background };
}

internal sealed class ListaSinSaltos : FlowLayoutPanel
{
    private bool _mostrando;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int tecla);

    private static bool Tabulando => (GetKeyState(0x09) & 0x8000) != 0;

    public void Mostrar(Control c)
    {
        _mostrando = true;
        try { ScrollControlIntoView(c); }
        finally { _mostrando = false; }
    }

    protected override Point ScrollToControl(Control activeControl) =>
        _mostrando || Tabulando ? base.ScrollToControl(activeControl) : DisplayRectangle.Location;
}

internal sealed class ListaSinParpadeo : ListBox
{
    private const int WM_ERASEBKGND = 0x0014;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND)
        {
            using (Graphics g = Graphics.FromHdc(m.WParam))
            using (var libre = new Region(ClientRectangle))
            {
                for (int i = Math.Max(0, TopIndex); i < Items.Count; i++)
                {
                    Rectangle r = GetItemRectangle(i);
                    if (r.Top >= ClientSize.Height) break;
                    libre.Exclude(r);
                }
                using var fondo = new SolidBrush(BackColor);
                g.FillRegion(fondo, libre);
            }
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0)
        {
            base.OnDrawItem(e);
            return;
        }
        using BufferedGraphics buffer = BufferedGraphicsManager.Current.Allocate(e.Graphics, e.Bounds);
        base.OnDrawItem(new DrawItemEventArgs(buffer.Graphics, e.Font, e.Bounds, e.Index, e.State, e.ForeColor, e.BackColor));
        buffer.Render(e.Graphics);
    }
}

internal static class ListasUtil
{
    public static int Indice(this ListBox lista, Point p)
    {
        int i = lista.IndexFromPoint(p);
        return i >= 0 && i < lista.Items.Count && lista.GetItemRectangle(i).Contains(p) ? i : -1;
    }
}
