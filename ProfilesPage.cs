namespace IGP_Soft;

internal sealed class ProfilesPage : Page
{
    private readonly Func<string, LayoutProfile> _snapshot;
    private readonly Action<LayoutProfile> _open;
    private readonly ListBox _list = new();
    private readonly RoundedTextBox _nameBox = new("Nombre del nuevo perfil, por ejemplo «Sala de monitoreo»", 400);
    private TextBox _name => _nameBox.Box;
    private readonly ModernButton _btnOpen;
    private readonly ModernButton _btnOverwrite;
    private readonly ModernButton _btnDelete;
    private readonly Label _status = new();

    public bool StartupLocal { get; init; } = true;

    public ProfilesPage(Func<string, LayoutProfile> snapshot, Action<LayoutProfile> open)
        : base("Perfiles", "Guarda cómo están las pantallas ahora y vuelve a abrirlas con un clic.")
    {
        _snapshot = snapshot;
        _open = open;

        _nameBox.Margin = new Padding(0, 0, 8, 0);
        _name.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SaveCurrent();
        };
        _status.AutoSize = true;
        _status.Margin = new Padding(8, 10, 0, 0);
        _status.Font = Theme.BodyBold;
        _status.ForeColor = Theme.Success;
        FlowLayoutPanel saveRow = ToolBar(_nameBox, ToolButton("★  Guardar pantallas actuales", SaveCurrent, primary: true), _status);

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Background;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 72;
        _list.DrawItem += DrawProfile;
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => OpenSelected();

        var side = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 250, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(16, 4, 0, 0), BackColor = Theme.Background,
        };
        _btnOpen = ToolButton("▶  Abrir este perfil", OpenSelected, primary: true);
        _btnOverwrite = ToolButton("⟳  Reemplazar con lo actual", OverwriteSelected);
        _btnDelete = ToolButton("✕  Eliminar", DeleteSelected);
        _btnDelete.Kind = ButtonKind.Danger;
        foreach (ModernButton b in new[] { _btnOpen, _btnOverwrite, _btnDelete })
        {
            b.Size = new Size(230, 40);
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Margin = new Padding(0, 0, 0, 8);
            side.Controls.Add(b);
        }
        side.Controls.Add(new Label
        {
            AutoSize = false, Size = new Size(230, 120), ForeColor = Theme.TextMuted, Margin = new Padding(2, 12, 0, 0),
            Text = "Doble clic en un perfil también lo abre.\n\n«Última sesión» se guarda sola.\n\n" +
                   "Qué abrir al iniciar el programa se elige en Ajustes.",
        });

        var gap = Gap(10);
        Body.Controls.Add(_list);
        Body.Controls.Add(side);
        Body.Controls.Add(gap);
        Body.Controls.Add(saveRow);
    }

    public override void OnPageShown()
    {
        _status.Text = "";
        RefreshList();
    }

    private void RefreshList(string? select = null)
    {
        string? keep = select ?? (_list.SelectedItem as LayoutProfile)?.Name;
        _list.Items.Clear();
        if (ProfileStore.Get(ProfileStore.LastSession) is { } last) _list.Items.Add(last);
        foreach (LayoutProfile p in ProfileStore.Named) _list.Items.Add(p);

        int idx = _list.Items.Cast<LayoutProfile>().ToList()
                       .FindIndex(p => string.Equals(p.Name, keep, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 && _list.Items.Count > 0) idx = 0;
        _list.SelectedIndex = idx;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var p = _list.SelectedItem as LayoutProfile;
        _btnOpen.Enabled = p is not null;
        _btnOverwrite.Enabled = p is not null && !ProfileStore.IsLastSession(p.Name);
        _btnDelete.Enabled = p is not null && !ProfileStore.IsLastSession(p.Name);
    }

    private void DrawProfile(object? sender, DrawItemEventArgs e)
    {
        Graphics g = e.Graphics;
        using (var bg = new SolidBrush(Theme.Background)) g.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || _list.Items[e.Index] is not LayoutProfile p) return;
        Theme.Smooth(g);

        bool sel = (e.State & DrawItemState.Selected) != 0;
        bool last = ProfileStore.IsLastSession(p.Name);
        bool auto = StartupLocal && string.Equals(p.Name, Startup.Profile, StringComparison.OrdinalIgnoreCase);
        var card = new RectangleF(e.Bounds.X + 1, e.Bounds.Y + 4, e.Bounds.Width - 3, e.Bounds.Height - 9);
        Theme.FillRound(g, sel ? Color.FromArgb(239, 246, 255) : Theme.Surface, card, 10);
        Theme.DrawRound(g, sel ? Theme.Accent : Theme.Border, card, 10, sel ? 1.6f : 1f);

        var icon = new Rectangle((int)card.X + 14, (int)card.Y + (int)(card.Height - 38) / 2, 38, 38);
        Theme.FillRound(g, last ? Color.FromArgb(241, 245, 249) : Theme.AccentSoft, icon, 10);
        TextRenderer.DrawText(g, last ? "⟲" : "★", Theme.Section, icon, last ? Theme.TextMuted : Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        int x = icon.Right + 14;
        TextRenderer.DrawText(g, p.Name, Theme.BodyBold, new Point(x, (int)card.Y + 12), Theme.Text);
        if (auto)
        {
            int tw = TextRenderer.MeasureText(p.Name, Theme.BodyBold).Width;
            var badge = new RectangleF(x + tw + 8, card.Y + 12, 104, 20);
            Theme.FillRound(g, Theme.SuccessSoft, badge, 10);
            TextRenderer.DrawText(g, "▶ Al iniciar", Theme.SmallBold, Rectangle.Round(badge), Theme.Success,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        int mons = p.Monitors.Count(m => !m.Excluded);
        string detail = (last ? "Se guarda sola  ·  " : "") + $"{p.LinkCount} enlace(s) en {mons} monitor(es)" +
                        (p.Browser is null ? "" : $"  ·  {p.Browser}") + $"  ·  {p.Saved:dd/MM/yyyy HH:mm}";
        TextRenderer.DrawText(g, detail, Theme.Small, new Point(x, (int)card.Y + 34), Theme.TextMuted);
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is LayoutProfile p) _open(p);
    }

    private void SaveCurrent()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _status.ForeColor = Theme.Warning;
            _status.Text = "Escribe un nombre para el perfil.";
            _name.Focus();
            return;
        }
        if (ProfileStore.IsLastSession(name))
        {
            _status.ForeColor = Theme.Warning;
            _status.Text = "Ese nombre está reservado. Elige otro.";
            return;
        }
        if (ProfileStore.Get(name) is not null &&
            MessageBox.Show(this, $"Ya existe «{name}». ¿Reemplazarlo?", "Perfiles",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        ProfileStore.Put(_snapshot(name));
        _name.Clear();
        _status.ForeColor = Theme.Success;
        _status.Text = $"✓ Guardado «{name}»";
        RefreshList(name);
    }

    private void OverwriteSelected()
    {
        if (_list.SelectedItem is not LayoutProfile p || ProfileStore.IsLastSession(p.Name)) return;
        if (MessageBox.Show(this, $"¿Reemplazar «{p.Name}» con las pantallas actuales?", "Perfiles",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        ProfileStore.Put(_snapshot(p.Name));
        _status.ForeColor = Theme.Success;
        _status.Text = $"✓ Actualizado «{p.Name}»";
        RefreshList(p.Name);
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not LayoutProfile p || ProfileStore.IsLastSession(p.Name)) return;
        if (MessageBox.Show(this, $"¿Eliminar el perfil «{p.Name}»?", "Perfiles",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        ProfileStore.Remove(p.Name);
        if (StartupLocal && string.Equals(Startup.Profile, p.Name, StringComparison.OrdinalIgnoreCase))
            Startup.Profile = Startup.Nothing;
        _status.Text = "";
        RefreshList();
    }
}
