namespace IGP_Soft;

/// <summary>Guardar / abrir / eliminar perfiles de pantallas y elegir qué se abre al iniciar.</summary>
internal sealed class ProfilesForm : Form
{
    private readonly Func<string, LayoutProfile> _snapshot;
    private readonly ListBox _list = new();
    private readonly ComboBox _cboStartup = new();
    private readonly Button _btnOpen = new();
    private readonly Button _btnOverwrite = new();
    private readonly Button _btnDelete = new();

    /// <summary>Perfil que el usuario eligió abrir (lo aplica el panel al cerrar el diálogo).</summary>
    public LayoutProfile? ToOpen { get; private set; }

    public ProfilesForm(Func<string, LayoutProfile> snapshot)
    {
        _snapshot = snapshot;

        Text = "Perfiles de pantallas";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 520);
        MinimumSize = new Size(560, 440);
        Font = new Font("Segoe UI", 9f);
        ShowInTaskbar = false;
        TopMost = true;   // el panel es topmost; sin esto el diálogo queda detrás

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(10, 6, 10, 6),
            ForeColor = Color.DimGray,
            Text = "Un perfil recuerda qué enlace va en cada zona de cada monitor. «Última sesión» se " +
                   "actualiza sola con cada cambio.\nArchivo: " + ProfileStore.FilePath,
        };

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 38;
        _list.DrawItem += DrawProfile;
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => OpenSelected();

        var side = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 200, FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8, 0, 8, 0), WrapContents = false,
        };
        var btnSave = new Button { Text = "Guardar pantallas actuales…" };
        _btnOpen.Text = "Abrir ahora";
        _btnOverwrite.Text = "Reemplazar con las actuales";
        _btnDelete.Text = "Eliminar";
        foreach (Button b in new[] { _btnOpen, btnSave, _btnOverwrite, _btnDelete })
        {
            b.Width = 180;
            b.Height = 30;
            b.Margin = new Padding(0, 0, 0, 6);
        }
        _btnOpen.Font = new Font(Font, FontStyle.Bold);
        _btnOpen.Click += (_, _) => OpenSelected();
        btnSave.Click += (_, _) => SaveCurrentAs();
        _btnOverwrite.Click += (_, _) => OverwriteSelected();
        _btnDelete.Click += (_, _) => DeleteSelected();
        side.Controls.AddRange(new Control[] { _btnOpen, btnSave, _btnOverwrite, _btnDelete });

        var options = BuildOptions();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Cerrar", AutoSize = true, DialogResult = DialogResult.Cancel };
        bar.Controls.Add(close);
        CancelButton = close;

        var middle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };
        middle.Controls.Add(_list);
        middle.Controls.Add(side);

        Controls.Add(middle);
        Controls.Add(options);
        Controls.Add(bar);
        Controls.Add(hint);

        RefreshList();
    }

    private Control BuildOptions()
    {
        var box = new GroupBox { Text = "Inicio y datos", Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(10) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };

        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        row.Controls.Add(new Label { Text = "Al iniciar el programa, abrir:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
        _cboStartup.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboStartup.Width = 260;
        _cboStartup.SelectionChangeCommitted += (_, _) =>
        {
            Startup.Profile = _cboStartup.SelectedIndex <= 0 ? Startup.Nothing : (string)_cboStartup.SelectedItem!;
            _list.Invalidate();   // mueve la marca «★ al iniciar»
        };
        row.Controls.Add(_cboStartup);

        var chkWindows = new CheckBox
        {
            Text = "Iniciar IGP_Soft al encender Windows (con el perfil de arriba)",
            AutoSize = true, Checked = Startup.WithWindows, Margin = new Padding(3, 2, 0, 4),
        };
        chkWindows.CheckedChanged += (_, _) =>
        {
            Startup.WithWindows = chkWindows.Checked;
            if (Startup.WithWindows != chkWindows.Checked)
            {
                MessageBox.Show(this, "No se pudo cambiar el inicio automático con Windows.", "Perfiles",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                chkWindows.Checked = Startup.WithWindows;
            }
        };

        var keepRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        var chkKeep = new CheckBox
        {
            Text = "Conservar datos de navegación (cookies, inicios de sesión) entre sesiones",
            AutoSize = true, Checked = Profiles.KeepData, Margin = new Padding(3, 6, 8, 0),
        };
        chkKeep.CheckedChanged += (_, _) => Profiles.KeepData = chkKeep.Checked;
        var btnWipe = new Button { Text = "Borrar datos guardados", AutoSize = true };
        btnWipe.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                    "Se borrarán las cookies e inicios de sesión guardados de todas las pantallas.\n" +
                    "Las pantallas abiertas ahora pueden conservar algunos hasta que se cierren. ¿Continuar?",
                    "Borrar datos", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Profiles.DeleteKept();
        };
        keepRow.Controls.Add(chkKeep);
        keepRow.Controls.Add(btnWipe);

        flow.Controls.AddRange(new Control[] { row, chkWindows, keepRow });
        box.Controls.Add(flow);
        return box;
    }

    // ------------------------------------------------------------------ lista

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

        _cboStartup.Items.Clear();
        _cboStartup.Items.Add("(nada — panel vacío)");
        _cboStartup.Items.Add(ProfileStore.LastSession);
        foreach (LayoutProfile p in ProfileStore.Named) _cboStartup.Items.Add(p.Name);
        string current = Startup.Profile;
        int si = _cboStartup.Items.Cast<string>().ToList()
                            .FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
        _cboStartup.SelectedIndex = Math.Max(0, si);

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
        e.DrawBackground();
        if (e.Index < 0 || _list.Items[e.Index] is not LayoutProfile p) return;

        bool sel = (e.State & DrawItemState.Selected) != 0;
        bool last = ProfileStore.IsLastSession(p.Name);
        bool auto = string.Equals(p.Name, Startup.Profile, StringComparison.OrdinalIgnoreCase);
        Color main = sel ? SystemColors.HighlightText : SystemColors.WindowText;
        Color sub = sel ? SystemColors.HighlightText : Color.DimGray;

        using var bold = new Font(Font, last ? FontStyle.Bold | FontStyle.Italic : FontStyle.Bold);
        string title = p.Name + (auto ? "   ★ al iniciar" : "");
        TextRenderer.DrawText(e.Graphics, title, bold, new Point(e.Bounds.X + 6, e.Bounds.Y + 3), main);

        int mons = p.Monitors.Count(m => !m.Excluded);
        string detail = $"{p.LinkCount} enlace(s) en {mons} monitor(es)" +
                        (p.Browser is null ? "" : $" · {p.Browser}") +
                        $" · {p.Saved:dd/MM/yyyy HH:mm}";
        TextRenderer.DrawText(e.Graphics, detail, Font, new Point(e.Bounds.X + 6, e.Bounds.Y + 20), sub);
        e.DrawFocusRectangle();
    }

    // ------------------------------------------------------------------ acciones

    private void OpenSelected()
    {
        if (_list.SelectedItem is not LayoutProfile p) return;
        ToOpen = p;
        DialogResult = DialogResult.OK;
    }

    private void SaveCurrentAs()
    {
        string? name = AskName("Nombre del perfil:", "");
        if (name is null) return;
        if (ProfileStore.IsLastSession(name))
        {
            MessageBox.Show(this, "Ese nombre está reservado. Elija otro.", "Perfiles",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (ProfileStore.Get(name) is not null &&
            MessageBox.Show(this, $"Ya existe «{name}». ¿Reemplazarlo?", "Perfiles",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        ProfileStore.Put(_snapshot(name));
        RefreshList(name);
    }

    private void OverwriteSelected()
    {
        if (_list.SelectedItem is not LayoutProfile p || ProfileStore.IsLastSession(p.Name)) return;
        if (MessageBox.Show(this, $"¿Reemplazar «{p.Name}» con las pantallas actuales?", "Perfiles",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        ProfileStore.Put(_snapshot(p.Name));
        RefreshList(p.Name);
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not LayoutProfile p || ProfileStore.IsLastSession(p.Name)) return;
        if (MessageBox.Show(this, $"¿Eliminar el perfil «{p.Name}»?", "Perfiles",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        ProfileStore.Remove(p.Name);
        if (string.Equals(Startup.Profile, p.Name, StringComparison.OrdinalIgnoreCase))
            Startup.Profile = Startup.Nothing;
        RefreshList();
    }

    private string? AskName(string prompt, string value)
    {
        using var f = new Form
        {
            Text = "Perfiles", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, TopMost = true,
            ClientSize = new Size(360, 110), Font = Font,
        };
        var lbl = new Label { Text = prompt, AutoSize = true, Location = new Point(12, 12) };
        var txt = new TextBox { Text = value, Location = new Point(12, 34), Width = 336, MaxLength = 60 };
        var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, Location = new Point(192, 72), Width = 75 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(273, 72), Width = 75 };
        f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;

        if (f.ShowDialog(this) != DialogResult.OK) return null;
        string name = txt.Text.Trim();
        return name.Length == 0 ? null : name;
    }
}
