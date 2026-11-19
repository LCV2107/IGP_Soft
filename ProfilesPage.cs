namespace IGP_Soft;

internal sealed class ProfilesPage : Page
{
    private const int AltoTarjeta = 72;
    private const string FormatoArrastre = "IGP_Soft.Perfil";

    private sealed record CarpetaItem(string Nombre, string Clave, int Cantidad)
    {
        public const string Todos = "\u0001todos";
        public const string SinCarpeta = "\u0001sin";
        public bool Propia => Clave is not (Todos or SinCarpeta);
    }

    private readonly Func<string, LayoutProfile> _snapshot;
    private readonly Action<LayoutProfile> _open;
    private readonly ListBox _list = new ListaSinParpadeo();
    private readonly ListBox _carpetas = new();
    private readonly RoundedTextBox _nameBox = new("Nombre del nuevo perfil, por ejemplo «Sala de monitoreo»", 400);
    private TextBox _name => _nameBox.Box;
    private readonly ModernButton _btnOpen;
    private readonly ModernButton _btnOverwrite;
    private readonly ModernButton _btnMover;
    private readonly ModernButton _btnDelete;
    private readonly Label _status = new();
    private readonly HashSet<string> _desplegados = new(StringComparer.OrdinalIgnoreCase);
    private string _carpeta = CarpetaItem.Todos;
    private bool _recargando;
    private int _soltarEn = -1;
    private Point _arrastreDesde;
    private int _arrastreIndice = -1;
    private int _flechaEncima = -1;

    public bool StartupLocal { get; init; } = true;

    public Func<Task<IReadOnlyList<(int Numero, byte[] Jpeg)>>>? Capturar { get; init; }

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
        _list.DrawMode = DrawMode.OwnerDrawVariable;
        _list.MeasureItem += MedirPerfil;
        _list.DrawItem += DrawProfile;
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) =>
        {
            Point p = _list.PointToClient(Cursor.Position);
            int i = _list.Indice(p);
            if (i >= 0 && !Flecha(_list.GetItemRectangle(i)).Contains(p)) OpenSelected();
        };
        _list.MouseDown += ListMouseDown;
        _list.MouseMove += ListMouseMove;
        _list.MouseUp += (_, e) =>
        {
            _arrastreIndice = -1;
            if (e.Button == MouseButtons.Right && _list.SelectedItem is LayoutProfile) ShowProfileMenu(e.Location);
        };
        _list.KeyDown += (_, e) =>
        {
            if (_list.SelectedItem is not LayoutProfile p) return;
            if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.KeyCode is Keys.Space or Keys.Right or Keys.Left)
            {
                bool abrir = e.KeyCode == Keys.Right || (e.KeyCode == Keys.Space && !_desplegados.Contains(p.Name));
                if (abrir) _desplegados.Add(p.Name); else _desplegados.Remove(p.Name);
                RefreshList(p.Name);
                e.Handled = true;
            }
        };
        _list.Resize += (_, _) => _list.Invalidate();

        var folders = new Card { Dock = DockStyle.Left, Width = 220, Padding = new Padding(10) };
        var foldersTitle = new Label { Dock = DockStyle.Top, Height = 28, Text = "Carpetas", Font = Theme.Section, ForeColor = Theme.Text, BackColor = Theme.Surface };
        _carpetas.Dock = DockStyle.Fill;
        _carpetas.BorderStyle = BorderStyle.None;
        _carpetas.DrawMode = DrawMode.OwnerDrawFixed;
        _carpetas.ItemHeight = 34;
        _carpetas.Font = Theme.Body;
        _carpetas.BackColor = Theme.Surface;
        _carpetas.IntegralHeight = false;
        _carpetas.AllowDrop = true;
        _carpetas.DrawItem += DrawCarpeta;
        _carpetas.SelectedIndexChanged += (_, _) =>
        {
            if (_recargando || _carpetas.SelectedItem is not CarpetaItem c) return;
            _carpeta = c.Clave;
            RefreshList();
        };
        _carpetas.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int i = _carpetas.Indice(e.Location);
            if (i >= 0) _carpetas.SelectedIndex = i;
            ShowFolderMenu(e.Location);
        };
        _carpetas.DragOver += (_, e) =>
        {
            int i = _carpetas.Indice(_carpetas.PointToClient(new Point(e.X, e.Y)));
            bool ok = e.Data?.GetDataPresent(FormatoArrastre) == true && i >= 0 && _carpetas.Items[i] is CarpetaItem c && c.Clave != CarpetaItem.Todos;
            e.Effect = ok ? DragDropEffects.Move : DragDropEffects.None;
            int nuevo = ok ? i : -1;
            if (nuevo != _soltarEn) { _soltarEn = nuevo; _carpetas.Invalidate(); }
        };
        _carpetas.DragLeave += (_, _) => { _soltarEn = -1; _carpetas.Invalidate(); };
        _carpetas.DragDrop += (_, e) =>
        {
            int i = _soltarEn;
            _soltarEn = -1;
            _carpetas.Invalidate();
            if (i < 0 || e.Data?.GetData(FormatoArrastre) is not string nombre || _carpetas.Items[i] is not CarpetaItem c) return;
            MoveTo(nombre, c.Propia ? c.Clave : null);
        };
        ModernButton nueva = new("Nueva carpeta", ButtonKind.Secondary, "＋") { Dock = DockStyle.Bottom, Height = 36 };
        nueva.Click += (_, _) => NewFolder(null);
        folders.Controls.Add(_carpetas);
        folders.Controls.Add(nueva);
        folders.Controls.Add(foldersTitle);

        var side = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 250, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(16, 4, 0, 0), BackColor = Theme.Background,
        };
        _btnOpen = ToolButton("▶  Abrir este perfil", OpenSelected, primary: true);
        _btnOverwrite = ToolButton("⟳  Reemplazar con lo actual", OverwriteSelected);
        _btnMover = ToolButton("▤  Mover a carpeta", () => ShowMoveMenu(_btnMover!, new Point(0, _btnMover!.Height)));
        _btnDelete = ToolButton("✕  Eliminar", DeleteSelected);
        _btnDelete.Kind = ButtonKind.Danger;
        foreach (ModernButton b in new[] { _btnOpen, _btnOverwrite, _btnMover, _btnDelete })
        {
            b.Size = new Size(230, 40);
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Margin = new Padding(0, 0, 0, 8);
            side.Controls.Add(b);
        }
        side.Controls.Add(new Label
        {
            AutoSize = false, Size = new Size(230, 190), ForeColor = Theme.TextMuted, Margin = new Padding(2, 12, 0, 0),
            Text = "Doble clic en un perfil también lo abre.\n\n▾ muestra la captura de los monitores tomada al guardar el perfil.\n\n" +
                   "Arrastra un perfil a una carpeta para ordenarlo.\n\n«Última sesión» se guarda sola. Qué abrir al iniciar el programa se elige en Ajustes.",
        });

        var centro = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 0, 0), BackColor = Theme.Background };
        centro.Controls.Add(_list);

        var gap = Gap(10);
        Body.Controls.Add(centro);
        Body.Controls.Add(side);
        Body.Controls.Add(folders);
        Body.Controls.Add(gap);
        Body.Controls.Add(saveRow);

        Miniaturas.Iniciar();
        Miniaturas.Lista += Repintar;
        Disposed += (_, _) => Miniaturas.Lista -= Repintar;
    }

    private void Repintar()
    {
        if (!IsDisposed && _list.IsHandleCreated) _list.Invalidate();
    }

    public override void OnPageShown()
    {
        _status.Text = "";
        RefreshList();
    }

    private bool EnCarpeta(LayoutProfile p) => _carpeta switch
    {
        CarpetaItem.Todos => true,
        CarpetaItem.SinCarpeta => p.Carpeta is null && !ProfileStore.IsLastSession(p.Name),
        _ => string.Equals(p.Carpeta, _carpeta, StringComparison.CurrentCultureIgnoreCase),
    };

    private void LoadFolders()
    {
        List<LayoutProfile> named = ProfileStore.Named.ToList();
        var items = new List<CarpetaItem>
        {
            new("Todos", CarpetaItem.Todos, named.Count + (ProfileStore.Get(ProfileStore.LastSession) is null ? 0 : 1)),
            new("Sin carpeta", CarpetaItem.SinCarpeta, named.Count(p => p.Carpeta is null)),
        };
        items.AddRange(CarpetasPerfiles.Todas.Select(c => new CarpetaItem(c, c, CarpetasPerfiles.Cantidad(c))));
        if (items.All(i => !string.Equals(i.Clave, _carpeta, StringComparison.CurrentCultureIgnoreCase))) _carpeta = CarpetaItem.Todos;
        _carpetas.BeginUpdate();
        _carpetas.Items.Clear();
        foreach (CarpetaItem i in items) _carpetas.Items.Add(i);
        _carpetas.SelectedIndex = items.FindIndex(i => string.Equals(i.Clave, _carpeta, StringComparison.CurrentCultureIgnoreCase));
        _carpetas.EndUpdate();
    }

    private void RefreshList(string? select = null)
    {
        _recargando = true;
        string? keep = select ?? (_list.SelectedItem as LayoutProfile)?.Name;
        int top = _list.TopIndex;
        LoadFolders();
        _list.BeginUpdate();
        _list.Items.Clear();
        if (_carpeta == CarpetaItem.Todos && ProfileStore.Get(ProfileStore.LastSession) is { } last) _list.Items.Add(last);
        foreach (LayoutProfile p in ProfileStore.Named.Where(EnCarpeta)) _list.Items.Add(p);

        int idx = _list.Items.Cast<LayoutProfile>().ToList()
                       .FindIndex(p => string.Equals(p.Name, keep, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 && _list.Items.Count > 0) idx = 0;
        _list.SelectedIndex = idx;
        if (top > 0 && top < _list.Items.Count) _list.TopIndex = top;
        _list.EndUpdate();
        _recargando = false;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var p = _list.SelectedItem as LayoutProfile;
        bool propio = p is not null && !ProfileStore.IsLastSession(p.Name);
        _btnOpen.Enabled = p is not null;
        _btnOverwrite.Enabled = propio;
        _btnMover.Enabled = propio;
        _btnDelete.Enabled = propio;
    }

    private void MedirPerfil(object? sender, MeasureItemEventArgs e)
    {
        bool abierto = e.Index >= 0 && _list.Items[e.Index] is LayoutProfile p && _desplegados.Contains(p.Name);
        e.ItemHeight = abierto ? Math.Min(255, AltoTarjeta + DibujoPerfil.Alto + 14) : AltoTarjeta;
    }

    private static Rectangle Tarjeta(Rectangle item) => new(item.X + 1, item.Y + 4, item.Width - 3, item.Height - 9);

    private static Rectangle Flecha(Rectangle item)
    {
        Rectangle card = Tarjeta(item);
        return new Rectangle(card.Right - 46, card.Y + (AltoTarjeta - 9 - 34) / 2, 34, 34);
    }

    private void ListMouseDown(object? sender, MouseEventArgs e)
    {
        int i = _list.Indice(e.Location);
        _arrastreIndice = -1;
        if (i < 0 || e.Button != MouseButtons.Left || _list.Items[i] is not LayoutProfile p) return;
        if (Flecha(_list.GetItemRectangle(i)).Contains(e.Location))
        {
            if (!_desplegados.Remove(p.Name)) _desplegados.Add(p.Name);
            RefreshList(p.Name);
            return;
        }
        if (!ProfileStore.IsLastSession(p.Name))
        {
            _arrastreIndice = i;
            _arrastreDesde = e.Location;
        }
    }

    private void ListMouseMove(object? sender, MouseEventArgs e)
    {
        int i = _list.Indice(e.Location);
        int encima = i >= 0 && Flecha(_list.GetItemRectangle(i)).Contains(e.Location) ? i : -1;
        if (encima != _flechaEncima)
        {
            if (_flechaEncima >= 0 && _flechaEncima < _list.Items.Count) _list.Invalidate(Flecha(_list.GetItemRectangle(_flechaEncima)));
            if (encima >= 0) _list.Invalidate(Flecha(_list.GetItemRectangle(encima)));
            _flechaEncima = encima;
            _list.Cursor = encima >= 0 ? Cursors.Hand : Cursors.Default;
        }
        if (_arrastreIndice < 0 || e.Button != MouseButtons.Left) return;
        Size umbral = SystemInformation.DragSize;
        if (Math.Abs(e.X - _arrastreDesde.X) < umbral.Width && Math.Abs(e.Y - _arrastreDesde.Y) < umbral.Height) return;
        if (_arrastreIndice >= _list.Items.Count || _list.Items[_arrastreIndice] is not LayoutProfile p) return;
        _arrastreIndice = -1;
        _list.DoDragDrop(new DataObject(FormatoArrastre, p.Name), DragDropEffects.Move);
    }

    private void DrawProfile(object? sender, DrawItemEventArgs e)
    {
        Graphics g = e.Graphics;
        using (var bg = new SolidBrush(Theme.Background)) g.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || _list.Items[e.Index] is not LayoutProfile p) return;
        Theme.Smooth(g);

        bool sel = (e.State & DrawItemState.Selected) != 0;
        bool last = ProfileStore.IsLastSession(p.Name);
        bool abierto = _desplegados.Contains(p.Name);
        bool auto = StartupLocal && string.Equals(p.Name, Startup.Profile, StringComparison.OrdinalIgnoreCase);
        Rectangle card = Tarjeta(e.Bounds);
        Theme.FillRound(g, sel ? Color.FromArgb(239, 246, 255) : Theme.Surface, card, 10);
        Theme.DrawRound(g, sel ? Theme.Accent : Theme.Border, card, 10, sel ? 1.6f : 1f);

        var icon = new Rectangle(card.X + 14, card.Y + (AltoTarjeta - 9 - 38) / 2, 38, 38);
        Theme.FillRound(g, last ? Color.FromArgb(241, 245, 249) : Theme.AccentSoft, icon, 10);
        TextRenderer.DrawText(g, last ? "⟲" : "★", Theme.Section, icon, last ? Theme.TextMuted : Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        int x = icon.Right + 14;
        int textoAncho = Flecha(e.Bounds).X - x - 8;
        TextRenderer.DrawText(g, p.Name, Theme.BodyBold, new Rectangle(x, card.Y + 12, textoAncho, 20), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (auto)
        {
            int tw = Math.Min(textoAncho, TextRenderer.MeasureText(p.Name, Theme.BodyBold).Width);
            var badge = new RectangleF(x + tw + 8, card.Y + 12, 104, 20);
            Theme.FillRound(g, Theme.SuccessSoft, badge, 10);
            TextRenderer.DrawText(g, "▶ Al iniciar", Theme.SmallBold, Rectangle.Round(badge), Theme.Success,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        int mons = p.Monitors.Count(m => !m.Excluded);
        string detail = (last ? "Se guarda sola  ·  " : "") + $"{p.LinkCount} enlace(s) en {mons} monitor(es)" +
                        (p.Browser is null ? "" : $"  ·  {p.Browser}") + $"  ·  {p.Saved:dd/MM/yyyy HH:mm}" +
                        (p.Carpeta is not null && _carpeta == CarpetaItem.Todos ? $"  ·  ▤ {p.Carpeta}" : "");
        TextRenderer.DrawText(g, detail, Theme.Small, new Rectangle(x, card.Y + 34, textoAncho, 20), Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        Rectangle flecha = Flecha(e.Bounds);
        Point raton = _list.PointToClient(Cursor.Position);
        bool encima = flecha.Contains(raton);
        Theme.FillRound(g, abierto || encima ? Theme.AccentSoft : Color.FromArgb(241, 245, 249), flecha, 8);
        TextRenderer.DrawText(g, abierto ? "▴" : "▾", Theme.Section, flecha, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (!abierto) return;
        var area = new Rectangle(card.X + 14, card.Y + AltoTarjeta - 6, card.Width - 28, DibujoPerfil.Alto);
        using (var linea = new Pen(Theme.Border)) g.DrawLine(linea, area.X, area.Y - 4, area.Right, area.Y - 4);
        DibujoPerfil.Dibujar(g, new Rectangle(area.X, area.Y + 6, area.Width, area.Height - 8), p);
    }

    private void DrawCarpeta(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _carpetas.Items[e.Index] is not CarpetaItem c) return;
        Graphics g = e.Graphics;
        g.FillRectangle(Brushes.White, e.Bounds);
        Theme.Smooth(g);
        var r = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);
        bool sel = (e.State & DrawItemState.Selected) != 0;
        if (e.Index == _soltarEn) Theme.FillRound(g, Theme.AccentSoft, r, 8);
        else if (sel) Theme.FillRound(g, Theme.Accent, r, 8);
        string glyph = c.Clave switch { CarpetaItem.Todos => "▦", CarpetaItem.SinCarpeta => "▢", _ => "▤" };
        Color fore = sel && e.Index != _soltarEn ? Color.White : Theme.Text;
        TextRenderer.DrawText(g, $"{glyph}  {c.Nombre}", c.Propia ? Theme.Body : Theme.BodyBold, new Rectangle(r.X + 8, r.Y, r.Width - 48, r.Height), fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, c.Cantidad.ToString(), Theme.Small, new Rectangle(r.Right - 40, r.Y, 32, r.Height),
            sel && e.Index != _soltarEn ? Color.White : Theme.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private void ShowFolderMenu(Point at)
    {
        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripMenuItem("＋  Nueva carpeta…", null, (_, _) => NewFolder(null)));
        if (_carpetas.SelectedItem is CarpetaItem { Propia: true } c)
        {
            menu.Items.Add(new ToolStripMenuItem("✎  Renombrar…", null, (_, _) => RenameFolder(c.Clave)));
            menu.Items.Add(new ToolStripMenuItem("✕  Eliminar carpeta", null, (_, _) => DeleteFolder(c.Clave)));
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_carpetas, at);
    }

    private void ShowProfileMenu(Point at)
    {
        if (_list.SelectedItem is not LayoutProfile p) return;
        bool propio = !ProfileStore.IsLastSession(p.Name);
        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripMenuItem("▶  Abrir este perfil", null, (_, _) => OpenSelected()));
        menu.Items.Add(new ToolStripMenuItem(_desplegados.Contains(p.Name) ? "▴  Ocultar vista previa" : "▾  Ver vista previa", null, (_, _) =>
        {
            if (!_desplegados.Remove(p.Name)) _desplegados.Add(p.Name);
            RefreshList(p.Name);
        }));
        if (propio)
        {
            var mover = new ToolStripMenuItem("▤  Mover a");
            FillMoveItems(mover.DropDownItems, p);
            menu.Items.Add(mover);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("⟳  Reemplazar con lo actual", null, (_, _) => OverwriteSelected()));
            menu.Items.Add(new ToolStripMenuItem("✕  Eliminar", null, (_, _) => DeleteSelected()));
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_list, at);
    }

    private void ShowMoveMenu(Control owner, Point at)
    {
        if (_list.SelectedItem is not LayoutProfile p || ProfileStore.IsLastSession(p.Name)) return;
        var menu = Theme.Menu();
        FillMoveItems(menu.Items, p);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(owner, at);
    }

    private void FillMoveItems(ToolStripItemCollection items, LayoutProfile p)
    {
        IReadOnlyList<string> carpetas = CarpetasPerfiles.Todas;
        foreach (string c in carpetas)
        {
            string destino = c;
            items.Add(new ToolStripMenuItem("▤  " + c.Replace("&", "&&"), null, (_, _) => MoveTo(p.Name, destino))
            {
                Checked = string.Equals(p.Carpeta, c, StringComparison.CurrentCultureIgnoreCase),
            });
        }
        if (carpetas.Count > 0) items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem("▢  Sin carpeta", null, (_, _) => MoveTo(p.Name, null)) { Checked = p.Carpeta is null });
        items.Add(new ToolStripMenuItem("＋  Nueva carpeta…", null, (_, _) => NewFolder(p.Name)));
    }

    private string? AskName(string titulo, string inicial, string? actual)
    {
        using var f = new NombreForm(titulo, inicial, n => CarpetasPerfiles.Validar(n, actual));
        return f.ShowDialog(this) == DialogResult.OK ? f.Nombre : null;
    }

    private void NewFolder(string? mover)
    {
        if (AskName("Nueva carpeta", "", null) is not { } nombre || !CarpetasPerfiles.Crear(nombre)) return;
        if (mover is not null) CarpetasPerfiles.Mover(new[] { mover }, nombre);
        else _carpeta = nombre.Trim();
        RefreshList(mover);
    }

    private void RenameFolder(string carpeta)
    {
        if (AskName("Renombrar carpeta", carpeta, carpeta) is not { } nombre || !CarpetasPerfiles.Renombrar(carpeta, nombre)) return;
        if (string.Equals(_carpeta, carpeta, StringComparison.CurrentCultureIgnoreCase)) _carpeta = nombre.Trim();
        RefreshList();
    }

    private void DeleteFolder(string carpeta)
    {
        int n = CarpetasPerfiles.Cantidad(carpeta);
        if (MessageBox.Show(this, $"¿Eliminar la carpeta «{carpeta}»?" + (n > 0 ? $"\n\nSus {n} perfil(es) no se borran: pasan a «Sin carpeta»." : ""),
                "Eliminar carpeta", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        CarpetasPerfiles.Eliminar(carpeta);
        _carpeta = CarpetaItem.Todos;
        RefreshList();
    }

    private void MoveTo(string perfil, string? carpeta)
    {
        CarpetasPerfiles.Mover(new[] { perfil }, carpeta);
        _status.ForeColor = Theme.Success;
        _status.Text = carpeta is null ? $"✓ «{perfil}» quedó sin carpeta" : $"✓ «{perfil}» movido a «{carpeta}»";
        RefreshList(perfil);
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

        LayoutProfile nuevo = _snapshot(name);
        if (ProfileStore.Get(name) is null && _carpetas.SelectedItem is CarpetaItem { Propia: true } c) nuevo.Carpeta = c.Clave;
        ProfileStore.Put(nuevo);
        _name.Clear();
        string listo = $"✓ Guardado «{name}»" + (nuevo.Carpeta is null ? "" : $" en «{nuevo.Carpeta}»");
        RefreshList(name);
        _ = TomarCaptura(name, listo);
    }

    private async Task TomarCaptura(string perfil, string listo)
    {
        if (Capturar is null)
        {
            _status.ForeColor = Theme.Success;
            _status.Text = listo;
            return;
        }
        _status.ForeColor = Theme.TextMuted;
        _status.Text = listo + " · tomando la captura de los monitores…";
        IReadOnlyList<(int Numero, byte[] Jpeg)> capturas;
        try { capturas = await Capturar(); }
        catch { capturas = Array.Empty<(int, byte[])>(); }
        if (IsDisposed) return;
        CapturasPerfiles.Guardar(perfil, capturas);
        _status.ForeColor = capturas.Count > 0 ? Theme.Success : Theme.Warning;
        _status.Text = capturas.Count > 0 ? listo + " con su captura" : listo + " (no se pudo tomar la captura)";
        _list.Invalidate();
    }

    private void OverwriteSelected()
    {
        if (_list.SelectedItem is not LayoutProfile p || ProfileStore.IsLastSession(p.Name)) return;
        if (MessageBox.Show(this, $"¿Reemplazar «{p.Name}» con las pantallas actuales?", "Perfiles",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        ProfileStore.Put(_snapshot(p.Name));
        RefreshList(p.Name);
        _ = TomarCaptura(p.Name, $"✓ Actualizado «{p.Name}»");
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not LayoutProfile p || ProfileStore.IsLastSession(p.Name)) return;
        if (MessageBox.Show(this, $"¿Eliminar el perfil «{p.Name}»?", "Perfiles",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        ProfileStore.Remove(p.Name);
        CapturasPerfiles.Borrar(p.Name);
        _desplegados.Remove(p.Name);
        if (StartupLocal && string.Equals(Startup.Profile, p.Name, StringComparison.OrdinalIgnoreCase))
            Startup.Profile = Startup.Nothing;
        _status.Text = "";
        RefreshList();
    }
}
