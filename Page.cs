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
