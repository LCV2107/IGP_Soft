namespace IGP_Soft;

internal static class MonitorGroups
{
    private const string Key = "combinados";

    public static List<List<string>> Saved() =>
        (Config.Get(Key) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.Split('+', StringSplitOptions.RemoveEmptyEntries).ToList())
            .Where(g => g.Count > 1)
            .ToList();

    public static void Save(IEnumerable<IEnumerable<string>> groups) =>
        Config.Set(Key, string.Join(";", groups.Select(g => string.Join("+", g))));

    public static bool IsGroupName(string device) => device.Contains('+');

    public static IReadOnlyList<string> MembersOf(string device) => device.Split('+', StringSplitOptions.RemoveEmptyEntries);

    public static bool TryUnion(IReadOnlyList<MonitorInfo> monitors, out Rectangle union)
    {
        union = Rectangle.Empty;
        if (monitors.Count < 2) return false;
        var rects = monitors.Select(m => new Rectangle(m.X, m.Y, m.Width, m.Height)).ToList();
        union = rects.Aggregate(Rectangle.Union);
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                if (rects[i].IntersectsWith(rects[j])) return false;
        long area = rects.Sum(r => (long)r.Width * r.Height);
        return area == (long)union.Width * union.Height;
    }

    public static List<MonitorInfo> Apply(List<MonitorInfo> physical)
    {
        var result = physical.ToList();
        foreach (List<string> group in Saved())
        {
            var members = group.Select(d => result.FirstOrDefault(m => !m.IsCombined && m.DeviceName.Equals(d, StringComparison.OrdinalIgnoreCase)))
                               .OfType<MonitorInfo>().ToList();
            if (members.Count != group.Count || !TryUnion(members, out Rectangle r)) continue;
            int at = members.Min(m => result.IndexOf(m));
            foreach (MonitorInfo m in members) result.Remove(m);
            var ordered = members.OrderBy(m => physical.IndexOf(m)).ToList();
            result.Insert(Math.Min(at, result.Count), new MonitorInfo(
                string.Join("+", ordered.Select(m => m.DeviceName)), r.X, r.Y, r.Width, r.Height, ordered.Any(m => m.IsPrimary))
            {
                Members = ordered,
                HardwareId = "grupo:" + string.Join("+", ordered.Select(m => m.Id)),
            });
        }
        return result;
    }
}

internal sealed class CombineForm : Form
{
    private readonly List<MonitorInfo> _physical;
    private readonly List<List<string>> _groups;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Panel _map = new();
    private readonly Label _status = new();
    private readonly ModernButton _btnCombine = new("Combinar seleccionados", ButtonKind.Primary, "⧉");
    private readonly ModernButton _btnSplitAll = new("Separar todos", ButtonKind.Secondary, "⊟");
    private List<(MonitorInfo Monitor, RectangleF Rect)> _hit = new();

    public List<List<string>> Result { get; private set; }

    public CombineForm(List<MonitorInfo> physical, List<List<string>> groups)
    {
        _physical = physical;
        _groups = groups;
        Result = _groups;

        Theme.StyleWindow(this, darkCaption: false);
        Text = "Combinar monitores";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 520);

        var intro = new Label
        {
            Location = new Point(20, 16), Size = new Size(720, 44), ForeColor = Theme.TextMuted,
            Text = "Haz clic en los monitores que quieres unir. Juntos funcionarán como una sola pantalla grande: " +
                   "podrás mostrar una página que cruce de uno a otro o dividirla en zonas.",
        };

        _map.SetBounds(20, 66, 720, 340);
        _map.BackColor = Theme.Background;
        _map.Cursor = Cursors.Hand;
        _map.Paint += PaintMap;
        _map.MouseClick += (_, e) => ToggleAt(e.Location);
        _map.Resize += (_, _) => _map.Invalidate();
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(_map, true);

        _status.SetBounds(20, 414, 720, 40);
        _status.Font = Theme.BodyBold;

        _btnCombine.SetBounds(20, 466, 230, 40);
        _btnCombine.Click += (_, _) => Combine();
        _btnSplitAll.SetBounds(258, 466, 160, 40);
        _btnSplitAll.Click += (_, _) =>
        {
            Result = new List<List<string>>();
            DialogResult = DialogResult.OK;
        };
        var cancel = new ModernButton("Cancelar") { DialogResult = DialogResult.Cancel };
        cancel.SetBounds(630, 466, 110, 40);
        CancelButton = cancel;

        Controls.AddRange(new Control[] { intro, _map, _status, _btnCombine, _btnSplitAll, cancel });
        UpdateState();
    }

    private List<string>? GroupOf(string device) =>
        _groups.FirstOrDefault(g => g.Contains(device, StringComparer.OrdinalIgnoreCase));

    private void ToggleAt(Point p)
    {
        foreach ((MonitorInfo m, RectangleF r) in _hit)
        {
            if (!r.Contains(p)) continue;
            if (!_selected.Remove(m.DeviceName)) _selected.Add(m.DeviceName);
            UpdateState();
            return;
        }
    }

    private List<MonitorInfo> SelectedMonitors() =>
        _physical.Where(m => _selected.Contains(m.DeviceName)).ToList();

    private void UpdateState()
    {
        var sel = SelectedMonitors();
        bool valid = MonitorGroups.TryUnion(sel, out Rectangle union);
        _btnCombine.Enabled = valid;
        _btnSplitAll.Visible = _groups.Count > 0;
        if (sel.Count < 2)
        {
            _status.ForeColor = Theme.TextMuted;
            _status.Text = _physical.Count < 2 ? "Solo hay un monitor conectado: no hay nada que combinar." : "Selecciona 2 o más monitores que estén uno al lado del otro.";
        }
        else if (!valid)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = "✕  Esos monitores no forman un rectángulo (hay huecos o tienen distinto tamaño). Elige otra combinación.";
        }
        else
        {
            _status.ForeColor = Theme.Success;
            _status.Text = $"✓  Pantalla combinada de {union.Width} × {union.Height}. Lo que esté abierto en esos monitores se cerrará.";
        }
        _map.Invalidate();
    }

    private void Combine()
    {
        var sel = SelectedMonitors();
        if (!MonitorGroups.TryUnion(sel, out _)) return;
        var groups = _groups.Where(g => !g.Any(d => _selected.Contains(d))).Select(g => g.ToList()).ToList();
        groups.Add(sel.Select(m => m.DeviceName).ToList());
        Result = groups;
        DialogResult = DialogResult.OK;
    }

    private void PaintMap(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Background);
        Theme.Smooth(g);
        _hit = new();
        if (_physical.Count == 0) return;

        Rectangle all = _physical.Select(m => new Rectangle(m.X, m.Y, m.Width, m.Height)).Aggregate(Rectangle.Union);
        var area = Rectangle.Inflate(_map.ClientRectangle, -20, -20);
        double scale = Math.Min((double)area.Width / all.Width, (double)area.Height / all.Height);
        float ox = area.X + (float)(area.Width - all.Width * scale) / 2;
        float oy = area.Y + (float)(area.Height - all.Height * scale) / 2;
        RectangleF Map(Rectangle r) => new(ox + (float)((r.X - all.X) * scale) + 4, oy + (float)((r.Y - all.Y) * scale) + 4,
                                           (float)(r.Width * scale) - 8, (float)(r.Height * scale) - 8);

        foreach (List<string> group in _groups)
        {
            var members = _physical.Where(m => group.Contains(m.DeviceName, StringComparer.OrdinalIgnoreCase)).ToList();
            if (members.Count < 2) continue;
            RectangleF gr = Map(members.Select(m => new Rectangle(m.X, m.Y, m.Width, m.Height)).Aggregate(Rectangle.Union));
            gr.Inflate(6, 6);
            Theme.DrawRound(g, Theme.Warning, gr, 16, 2.5f);
            var tag = new RectangleF(gr.X + 12, gr.Y - 12, 150, 22);
            Theme.FillRound(g, Theme.Warning, tag, 11);
            TextRenderer.DrawText(g, "Ya combinados", Theme.SmallBold, Rectangle.Round(tag), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        for (int i = 0; i < _physical.Count; i++)
        {
            MonitorInfo m = _physical[i];
            RectangleF r = Map(new Rectangle(m.X, m.Y, m.Width, m.Height));
            _hit.Add((m, r));
            bool sel = _selected.Contains(m.DeviceName);
            Theme.FillRound(g, sel ? Theme.Accent : Color.FromArgb(30, 41, 59), r, 12);
            if (sel) Theme.DrawRound(g, Color.FromArgb(147, 197, 253), r, 12, 3f);
            using var big = Theme.Ui(28f, FontStyle.Bold);
            TextRenderer.DrawText(g, (i + 1).ToString(), big, Rectangle.Round(new RectangleF(r.X, r.Y, r.Width, r.Height - 26)), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            string info = $"{m.Width} × {m.Height}" + (m.IsPrimary ? " · Principal" : "");
            TextRenderer.DrawText(g, info, Theme.Small, Rectangle.Round(new RectangleF(r.X, r.Bottom - 40, r.Width, 22)),
                sel ? Color.White : Color.FromArgb(148, 163, 184), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (sel)
                TextRenderer.DrawText(g, "✓ Seleccionado", Theme.SmallBold, Rectangle.Round(new RectangleF(r.X, r.Bottom - 22, r.Width, 18)),
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
