using System.Threading;

namespace IGP_Soft;

internal enum ModoPanel { Normal, Controlado, Remoto }

internal sealed class MainForm : Form
{
    private readonly IPantallas _p;
    private readonly PantallasLocales? _local;
    private readonly ModoPanel _modo;
    private EstadoPantallas _estado = EstadoPantallas.Vacio;
    private string _tilesKey = "";
    private readonly List<MonitorTile> _tiles = new();
    private bool _syncing;
    private bool _activatePanelOnOpen;
    private string? _controlador;

    private readonly Label _info = new();
    private readonly Panel _canvas = new();
    private readonly ComboBox _cboBrowser = new();
    private readonly ToggleSwitch _chkPreview = new("");
    private readonly ToggleSwitch _chkSkipPrimary = new("");
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 1500 };
    private bool _capturing;
    private NotifyIcon? _tray;
    private readonly Icon _trayIcon;
    private bool _reallyExit;
    private bool _trayHintShown;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showReg;
    private readonly Panel _content = new();
    private readonly List<(ModernButton Nav, Page Page)> _nav = new();
    private readonly Panel _sideStatus = new();
    private readonly Panel _lock = new();
    private readonly Label _lockText = new();
    private readonly Chip _chipBrowser = new();
    private readonly Chip _chipMonitors = new();
    private readonly Chip _chipZones = new();
    private readonly Chip _chipOpen = new();
    private readonly Chip _chipLinks = new();
    private readonly Chip _chipMemory = new();
    private readonly System.Windows.Forms.Timer _maintenanceTimer = new() { Interval = 5000 };
    private long _browserMemory;
    private volatile bool _sampling;
    private Page _screensPage = null!;
    private LinksPage _linksPage = null!;
    private GalleryPage? _galleryPage;
    private GaleriaRemotaPage? _remoteGallery;
    private readonly EnviosRemotos? _envios;
    private readonly Dictionary<(string Monitor, int Zona), string> _copias = new();
    private ProfilesPage _profilesPage = null!;
    private ExportPage? _exportPage;
    private SettingsPage? _settingsPage;
    private ToolStripMenuItem? _trayProfiles;
    private ToolStripMenuItem? _trayCloseAll;

    public static Icon AppIcon { get; } = BuildIcon();

    private bool Remoto => _modo == ModoPanel.Remoto;

    public MainForm(IPantallas pantallas, ModoPanel modo, string startupProfile)
    {
        _p = pantallas;
        _local = pantallas as PantallasLocales;
        _modo = modo;
        _trayIcon = AppIcon;
        Text = Remoto ? "IGP_Soft — Control remoto" : "IGP_Soft — Pantallas de monitoreo IGP";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 620);
        Size = new Size(1240, 760);
        TopMost = false;

        _p.Cambio += e => OnUi(() => OnEstado(e));
        _p.Aviso += r => OnUi(() => ShowAviso(r));
        if (_local is not null)
        {
            _local.SurfaceShown += KeepPanelAboveSurfaces;
            _local.ZoneOpened += () => KeepPanelInFront(_activatePanelOnOpen);
        }
        if (Remoto)
        {
            LinkStore.Remote = list => _ = _p.GuardarEnlaces(list);
            ProfileStore.Remote = list => _ = _p.GuardarPerfiles(list);
            if (_p is IArchivosRemotos archivos) _envios = new EnviosRemotos(archivos);
        }

        BuildLayout();
        if (!Remoto) BuildTray();
        _local?.Start();
        OnEstado(_p.Estado);

        if (_estado.Navegador is null && !Remoto)
        {
            _info.Visible = true;
            _info.Text = "⚠  No hay un navegador compatible (Edge, Chrome, Brave, Firefox u Opera). Instala uno para mostrar pantallas.";
        }

        _previewTimer.Tick += (_, _) =>
        {
            if (_chkPreview.Checked) _ = CapturePreviews();
        };
        _previewTimer.Start();
        if (!Remoto)
        {
            _maintenanceTimer.Tick += (_, _) => Maintenance();
            _maintenanceTimer.Start();
            Shown += (_, _) => BeginInvoke(() => RunStartupProfile(startupProfile));
            Updater.Found += OnUpdateFound;
            Shown += async (_, _) =>
            {
                await Task.Delay(15000);
                await CheckForUpdatesQuietly();
            };
        }

        FormClosing += (_, e) =>
        {
            if (_reallyExit || Remoto) return;
            if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) return;
            e.Cancel = true;
            HideToTray();
        };

        FormClosed += (_, _) =>
        {
            _previewTimer.Stop();
            _maintenanceTimer.Stop();
            Updater.Found -= OnUpdateFound;
            if (!Remoto)
            {
                Hotkeys.Changed -= ReRegisterHotkeys;
                Hotkeys.Unregister(Handle);
            }
            _showReg?.Unregister(null);
            _showEvent?.Dispose();
            _local?.Dispose();
            (_p as IDisposable)?.Dispose();
            ClearPreviews();
            if (_tray is not null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
        };
    }

    private void OnUi(Action a)
    {
        if (IsDisposed) return;
        if (!IsHandleCreated || !InvokeRequired) { a(); return; }
        try { BeginInvoke(a); }
        catch { }
    }

    private void BuildLayout()
    {
        Theme.StyleWindow(this, darkCaption: true);

        _info.Dock = DockStyle.Top;
        _info.Height = 34;
        _info.TextAlign = ContentAlignment.MiddleLeft;
        _info.ForeColor = Theme.Danger;
        _info.Font = Theme.BodyBold;
        _info.Visible = false;

        _cboBrowser.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboBrowser.FlatStyle = FlatStyle.Flat;
        _cboBrowser.Font = Theme.Body;
        _cboBrowser.Width = 220;
        _cboBrowser.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_cboBrowser.SelectedItem is string name) _ = _p.ElegirNavegador(name);
        };

        _chkPreview.Text = "Vista previa en vivo de cada monitor en el panel";
        _chkPreview.Checked = true;
        _chkPreview.CheckedChanged += (_, _) => { if (!_chkPreview.Checked) ClearPreviews(); };

        _chkSkipPrimary.Text = "No usar la pantalla principal (la laptop desde la que manejas el programa)";
        _chkSkipPrimary.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _ = _p.OmitirPrincipal(_chkSkipPrimary.Checked);
        };

        _screensPage = new Page("Pantallas", "Clic en una zona para elegir qué mostrar  ·  ⊞ para dividir  ·  arrastra un monitor sobre otro para cambiar su número.");
        ModernButton gridAll = Page.ToolButton("▦  Dividir todos", () => { });
        gridAll.Click += (_, _) => ShowGridPickerForAll(gridAll);
        FlowLayoutPanel actions = Page.ToolBar(
            Page.ToolButton("▶  Abrir en orden", AutoAssignInOrder, primary: true),
            gridAll,
            Page.ToolButton("◎  Identificar", () => _ = _p.Identificar()),
            Page.ToolButton("⧉  Combinar monitores", ShowCombine),
            Page.ToolButton("⟳  Actualizar", () => _ = _p.RecargarMonitores()),
            Page.ToolButton("✕  Cerrar todas", CloseAll));

        var chips = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, BackColor = Theme.Background };
        foreach (Chip c in new[] { _chipBrowser, _chipMonitors, _chipZones, _chipOpen, _chipMemory, _chipLinks })
            if (!(Remoto && c == _chipMemory)) chips.Controls.Add(c);

        _canvas.Dock = DockStyle.Fill;
        _canvas.BackColor = Theme.Background;
        _canvas.Padding = new Padding(8, 12, 8, 8);
        _canvas.Resize += (_, _) => RelayoutTiles();
        BuildUpdateBanner();
        _screensPage.Body.Controls.Add(_canvas);
        _screensPage.Body.Controls.Add(chips);
        _screensPage.Body.Controls.Add(_updateBanner);
        _screensPage.Body.Controls.Add(_info);
        _screensPage.Body.Controls.Add(actions);

        _linksPage = new LinksPage(_envios);
        _linksPage.Changed += () =>
        {
            _local?.Refresh();
            foreach (MonitorTile t in _tiles) t.Invalidate();
            UpdateInfo();
        };
        _profilesPage = new ProfilesPage(name => _estado.ComoPerfil(name), p =>
        {
            ApplyProfile(p);
            ShowPage(_screensPage);
        })
        { StartupLocal = !Remoto };

        var pages = new List<(string, string, Page)> { ("▦", "Pantallas", _screensPage), ("☰", "Enlaces", _linksPage) };
        if (!Remoto)
        {
            _galleryPage = new GalleryPage(GalleryTargets);
            _exportPage = new ExportPage(() => { _local?.CloseAllWindows(); return true; }, AfterRestore);
            _settingsPage = new SettingsPage(_cboBrowser, _chkPreview, _chkSkipPrimary, () => ReloadAll(manual: true), RestartForUpdate);
            pages.Add(("▤", "Galería", _galleryPage));
            pages.Add(("★", "Perfiles", _profilesPage));
            pages.Add(("⇪", "Exportar y copias", _exportPage));
            pages.Add(("⚙", "Ajustes", _settingsPage));
        }
        else
        {
            if (_envios is not null)
            {
                _remoteGallery = new GaleriaRemotaPage(_envios, GalleryTargets);
                pages.Add(("▤", "Galería", _remoteGallery));
            }
            pages.Add(("★", "Perfiles", _profilesPage));
        }

        var side = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Sidebar };
        var brand = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Sidebar };
        brand.Paint += (_, e) =>
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            var logo = new Rectangle(22, 26, 42, 42);
            using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(logo, Color.FromArgb(59, 130, 246), Color.FromArgb(37, 99, 235), 45f))
            using (var path = Theme.Round(logo, 11))
                g.FillPath(grad, path);
            TextRenderer.DrawText(g, "IGP", Theme.SmallBold, logo, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using var title = Theme.Ui(13f, FontStyle.Bold);
            TextRenderer.DrawText(g, "IGP_Soft", title, new Point(74, 26), Color.White);
            string sub = Remoto ? "Controlando " + (_estado.Equipo.Length > 0 ? _estado.Equipo : "otro equipo") : "Pantallas de monitoreo";
            TextRenderer.DrawText(g, sub, Theme.Small, new Point(75, 50), Remoto ? Color.FromArgb(250, 204, 21) : Theme.SidebarMuted);
        };
        _brand = brand;

        var navList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Theme.Sidebar, Padding = new Padding(14, 8, 0, 0),
        };
        navList.Controls.Add(new Label
        {
            Text = "MENÚ", Font = Theme.SmallBold, ForeColor = Theme.SidebarMuted, AutoSize = false,
            Size = new Size(200, 24), Margin = new Padding(8, 0, 0, 4), TextAlign = ContentAlignment.MiddleLeft,
        });
        foreach ((string glyph, string text, Page page) in pages)
        {
            var b = new ModernButton(text, ButtonKind.Nav, glyph)
            {
                Size = new Size(208, 42), Margin = new Padding(0, 0, 0, 4), Radius = 10, BackColor = Theme.Sidebar,
            };
            Page target = page;
            b.Click += (_, _) => ShowPage(target);
            navList.Controls.Add(b);
            _nav.Add((b, page));
            page.Visible = false;
            _content.Controls.Add(page);
        }

        _sideStatus.Dock = DockStyle.Bottom;
        _sideStatus.Height = 78;
        _sideStatus.BackColor = Theme.Sidebar;
        _sideStatus.Paint += PaintSideStatus;

        side.Controls.Add(navList);
        side.Controls.Add(_sideStatus);
        side.Controls.Add(brand);

        _lock.Dock = DockStyle.Fill;
        _lock.BackColor = Theme.Background;
        _lock.Visible = false;
        _lockText.Dock = DockStyle.Fill;
        _lockText.TextAlign = ContentAlignment.MiddleCenter;
        _lockText.Font = Theme.Section;
        _lockText.ForeColor = Theme.Text;
        _lock.Controls.Add(_lockText);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Theme.Background;
        Controls.Add(_lock);
        Controls.Add(_content);
        Controls.Add(side);
        ShowPage(_screensPage);
    }

    private Panel? _brand;

    private void PaintSideStatus(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Smooth(g);
        var card = new RectangleF(14, 8, _sideStatus.Width - 28, _sideStatus.Height - 20);
        Theme.FillRound(g, Theme.SidebarHover, card, 10);
        string? browser = _estado.Navegador;
        int open = _estado.EnVivo;
        Color dot = browser is null ? Theme.Danger : open > 0 ? Color.FromArgb(74, 222, 128) : Theme.SidebarMuted;
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, card.X + 14, card.Y + 17, 9, 9);
        string state = browser is null ? "Sin navegador" : open > 0 ? $"{open} pantalla(s) en vivo" : "Sin pantallas abiertas";
        TextRenderer.DrawText(g, state, Theme.SmallBold, new Point((int)card.X + 30, (int)card.Y + 12), Color.White);
        string second = _browserMemory > 0 && !Remoto
            ? $"{ResourceMonitor.Format(_browserMemory)} · {browser ?? "—"}"
            : $"{_estado.Monitores.Count} monitor(es) · {browser ?? "—"}";
        TextRenderer.DrawText(g, second, Theme.Small,
            new Point((int)card.X + 30, (int)card.Y + 32), Theme.SidebarMuted);
    }

    private void ShowPage(Page page)
    {
        foreach ((ModernButton nav, Page p) in _nav) nav.Selected = p == page;
        page.OnPageShown();
        page.Visible = true;
        page.BringToFront();
        foreach ((_, Page p) in _nav)
            if (p != page) p.Visible = false;
        if (page == _screensPage)
        {
            foreach (MonitorTile t in _tiles) t.Invalidate();
            UpdateInfo();
        }
    }

    private IReadOnlyList<GalleryTarget> GalleryTargets()
    {
        var targets = new List<GalleryTarget>();
        foreach (EstadoMonitor m in _estado.Monitores.Where(m => !m.Omitido))
            foreach (EstadoZona z in m.Usables)
            {
                string id = m.Id;
                int zona = z.Zona;
                string label = m.Usables.Count() > 1 ? $"Monitor {m.Numero} · zona {m.NumeroVisible(z)}" : $"Monitor {m.Numero}";
                targets.Add(new GalleryTarget(label, z.Titulo, link => Open(id, zona, link)));
            }
        return targets;
    }

    private void AfterRestore()
    {
        _local?.ReloadSettings();
        foreach (MonitorTile t in _tiles) t.Invalidate();
        UpdateInfo();
    }

    private void OnEstado(EstadoPantallas e)
    {
        if (e.Seq < _estado.Seq && e.Equipo == _estado.Equipo) return;
        _estado = e;
        if (Remoto)
        {
            LinkStore.SetFromRemote(e.Enlaces);
            ProfileStore.SetFromRemote(e.Perfiles);
            Text = $"IGP_Soft — Controlando {e.Equipo}";
            _brand?.Invalidate();
        }

        _syncing = true;
        try
        {
            var names = e.Navegadores.ToList();
            if (!_cboBrowser.Items.Cast<string>().SequenceEqual(names))
            {
                _cboBrowser.Items.Clear();
                foreach (string n in names) _cboBrowser.Items.Add(n);
            }
            _cboBrowser.Enabled = names.Count > 0;
            if (e.Navegador is not null && !string.Equals(_cboBrowser.SelectedItem as string, e.Navegador, StringComparison.Ordinal))
                _cboBrowser.SelectedItem = e.Navegador;
            if (_chkSkipPrimary.Checked != e.OmitirPrincipal) _chkSkipPrimary.Checked = e.OmitirPrincipal;
        }
        finally { _syncing = false; }

        string key = string.Join(";", e.Monitores.Select(m => $"{m.Id}|{m.X}|{m.Y}|{m.Ancho}|{m.Alto}"));
        if (key != _tilesKey) RebuildTiles(key);
        else
        {
            foreach (MonitorTile t in _tiles)
                if (e.Monitor(t.Id) is { } m) t.Data = m;
            _tiles.Sort((a, b) => a.Data.Numero.CompareTo(b.Data.Numero));
            foreach (MonitorTile t in _tiles) t.Invalidate();
        }
        UpdateInfo();
    }

    private void ShowAviso(Resultado r)
    {
        if (r.Mensaje is null) return;
        MessageBoxIcon icon = r.Nivel switch
        {
            Nivel.Error => MessageBoxIcon.Error,
            Nivel.Aviso => MessageBoxIcon.Warning,
            _ => MessageBoxIcon.Information,
        };
        MessageBox.Show(this, r.Mensaje, r.Titulo ?? "IGP_Soft", MessageBoxButtons.OK, icon);
    }

    public void ShowInfo(string? text)
    {
        _info.Visible = text is not null;
        _info.Text = text ?? "";
    }

    public void SetControlador(string? equipo)
    {
        _controlador = equipo;
        _lock.Visible = equipo is not null;
        if (equipo is not null)
        {
            _lockText.Text = $"🔒  Controlado desde {equipo}\n\nMientras dure el control remoto, este panel queda bloqueado.\nSe libera solo al terminar la conexión.";
            _lock.BringToFront();
        }
        if (_trayProfiles is not null) _trayProfiles.Enabled = equipo is null;
        if (_trayCloseAll is not null) _trayCloseAll.Enabled = equipo is null;
    }

    public void CloseFromRemote(string mensaje)
    {
        if (IsDisposed) return;
        MessageBox.Show(this, mensaje, "IGP_Soft — Control remoto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _reallyExit = true;
        Close();
    }

    private void KeepPanelAboveSurfaces()
    {
        if (Visible && !IsDisposed)
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void RememberPanelFocus() => _activatePanelOnOpen = ActiveForm == this || (Visible && ContainsFocus);

    private void Open(string monitor, int zona, LinkDef link)
    {
        RememberPanelFocus();
        _ = _p.Abrir(monitor, zona, link);
    }

    private void AutoAssignInOrder()
    {
        if (_controlador is not null) return;
        RememberPanelFocus();
        _ = _p.AbrirEnOrden();
    }

    private void ApplyProfile(LayoutProfile p)
    {
        if (_controlador is not null) return;
        RememberPanelFocus();
        _ = _p.AplicarPerfil(p);
    }

    private void BuildTray()
    {
        Icon = _trayIcon;

        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripMenuItem("Abrir panel", null, (_, _) => ShowFromTray()) { Font = new Font(Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        _trayProfiles = new ToolStripMenuItem("Abrir perfil");
        menu.Items.Add(_trayProfiles);
        menu.Opening += (_, _) => FillProfileMenu(_trayProfiles);
        _trayCloseAll = new ToolStripMenuItem("Cerrar todas las ventanas", null, (_, _) => CloseAll());
        menu.Items.Add(_trayCloseAll);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Salir", null, (_, _) => ExitApp()));

        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "IGP_Soft — pantallas de monitoreo",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            _showReg = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => { try { if (!IsDisposed) BeginInvoke(ShowFromTray); } catch { } },
                null, Timeout.Infinite, false);
        }
        catch { }
    }

    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var b = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.FillEllipse(b, 0, 0, 31, 31);
            using var f = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("IGP", f, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        }
        IntPtr h = bmp.GetHicon();
        var ico = (Icon)Icon.FromHandle(h).Clone();
        NativeMethods.DestroyIcon(h);
        return ico;
    }

    public void HideToTray()
    {
        if (_tray is null) return;
        Hide();
        ShowInTaskbar = false;
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(3000, "IGP_Soft",
                "El panel sigue activo aquí. Doble clic para volver a abrirlo.", ToolTipIcon.Info);
        }
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void ExitApp()
    {
        if (!Visible) ShowFromTray();
        if (_estado.HayAbierto && _local is not null)
        {
            DialogResult r = MessageBox.Show(this,
                "Al salir se cerrarán las pantallas de monitoreo y su contenido " +
                "(las ventanas adjuntas vuelven al escritorio, no se cierran). ¿Desea continuar?",
                "Salir de IGP_Soft", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            _local.CloseEverything();
        }
        _reallyExit = true;
        Close();
    }

    private void RebuildTiles(string key)
    {
        _tilesKey = key;
        foreach (MonitorTile t in _tiles) { _canvas.Controls.Remove(t); t.Dispose(); }
        _tiles.Clear();

        foreach (EstadoMonitor m in _estado.Monitores.OrderBy(m => m.Numero))
        {
            var tile = new MonitorTile(m) { CopyText = CopyText };
            tile.ZoneActivated += ShowSegmentMenu;
            tile.MonitorMenuRequested += ShowMonitorMenu;
            tile.GridRequested += ShowGridPicker;
            tile.DragMoved += OnTileDragMoved;
            tile.DragDropped += OnTileDropped;
            _tiles.Add(tile);
            _canvas.Controls.Add(tile);
        }

        RelayoutTiles();
    }

    private MonitorTile? TileAtScreen(Point screen, MonitorTile? except)
    {
        Point p = _canvas.PointToClient(screen);
        return _tiles.FirstOrDefault(t => t != except && t.Bounds.Contains(p));
    }

    private void OnTileDragMoved(MonitorTile source, Point screen)
    {
        MonitorTile? target = TileAtScreen(screen, source);
        foreach (MonitorTile t in _tiles)
        {
            bool on = t == target;
            if (t.DropTarget == on) continue;
            t.DropTarget = on;
            t.Invalidate();
        }
    }

    private void OnTileDropped(MonitorTile source, Point screen)
    {
        MonitorTile? target = TileAtScreen(screen, source);
        foreach (MonitorTile t in _tiles.Where(t => t.DropTarget)) { t.DropTarget = false; t.Invalidate(); }
        if (target is null) return;
        _ = _p.IntercambiarNumeros(source.Id, target.Id);
    }

    private static List<List<string>> GroupsOf(EstadoPantallas e) =>
        e.Monitores.Where(m => m.Combinado && m.Id.StartsWith("grupo:", StringComparison.Ordinal))
                   .Select(m => m.Id["grupo:".Length..].Split('+', StringSplitOptions.RemoveEmptyEntries).ToList())
                   .ToList();

    private async void ShowCombine()
    {
        List<MonitorFisico> physical = await _p.MonitoresFisicos();
        if (physical.Count == 0 || IsDisposed) return;
        using var f = new CombineForm(physical.Select(m => m.ComoMonitor()).ToList(), GroupsOf(_estado));
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _ = _p.Combinar(f.Result);
    }

    private void SeparateMonitors(EstadoMonitor m)
    {
        if (!m.Combinado) return;
        if (m.Zonas.Any(z => z.Ocupada) &&
            MessageBox.Show(this, "Se cerrará lo que está abierto en esta pantalla combinada. ¿Separar los monitores?", "Separar monitores",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        _ = _p.Separar(m.Id);
    }

    private void AddMonitorItems(ContextMenuStrip menu, EstadoMonitor m)
    {
        var close = new ToolStripMenuItem("✕  Cerrar todo en este monitor", null, (_, _) => _ = _p.CerrarMonitor(m.Id))
        {
            Enabled = m.Zonas.Any(z => z.Ocupada),
        };
        Keys k = Hotkeys.Get(Hotkeys.Actions.First(a => a.Key == "cerrarmonitor"));
        if (k != Keys.None && !Remoto) close.ShortcutKeyDisplayString = Hotkeys.Format(k);
        menu.Items.Add(close);
        if (m.Combinado)
            menu.Items.Add(new ToolStripMenuItem("⊟  Separar monitores combinados", null, (_, _) => SeparateMonitors(m)));
        if (_estado.NumeracionPropia)
            menu.Items.Add(new ToolStripMenuItem("↺  Restablecer numeración de monitores", null, (_, _) => _ = _p.NumeracionOriginal()));
    }

    private void UpdateInfo()
    {
        _chipBrowser.Set("Navegador", _estado.Navegador ?? "ninguno");
        _chipMonitors.Set("Monitores", _estado.Monitores.Count.ToString());
        _chipZones.Set("Zonas", _estado.ZonasUsables.ToString());
        int abiertas = _estado.EnVivo;
        _chipOpen.Set("En vivo", abiertas.ToString());
        _chipOpen.Fill = abiertas > 0 ? Theme.SuccessSoft : Theme.Surface;
        _chipLinks.Set("Enlaces", LinkStore.Links.Count.ToString());
        UpdateMemoryChip();
        _sideStatus.Invalidate();
    }

    private void RelayoutTiles()
    {
        if (_tiles.Count == 0) return;

        int minX = _tiles.Min(t => t.Data.X);
        int minY = _tiles.Min(t => t.Data.Y);
        int maxX = _tiles.Max(t => t.Data.X + t.Data.Ancho);
        int maxY = _tiles.Max(t => t.Data.Y + t.Data.Alto);

        var area = new Rectangle(
            _canvas.Padding.Left, _canvas.Padding.Top,
            Math.Max(50, _canvas.ClientSize.Width - _canvas.Padding.Horizontal),
            Math.Max(50, _canvas.ClientSize.Height - _canvas.Padding.Vertical));

        double vw = Math.Max(1, maxX - minX);
        double vh = Math.Max(1, maxY - minY);
        double scale = Math.Min(area.Width / vw, area.Height / vh);

        int usedW = (int)(vw * scale);
        int usedH = (int)(vh * scale);
        int offX = area.Left + (area.Width - usedW) / 2;
        int offY = area.Top + (area.Height - usedH) / 2;

        const int gap = 8;
        foreach (MonitorTile t in _tiles)
        {
            EstadoMonitor m = t.Data;
            t.Bounds = new Rectangle(
                offX + (int)((m.X - minX) * scale) + gap,
                offY + (int)((m.Y - minY) * scale) + gap,
                Math.Max(120, (int)(m.Ancho * scale) - gap * 2),
                Math.Max(80, (int)(m.Alto * scale) - gap * 2));
        }
    }

    private void Maintenance()
    {
        if (!_sampling)
        {
            _sampling = true;
            Task.Run(() =>
            {
                long mem = 0;
                try { mem = ResourceMonitor.BrowserMemory(); }
                catch { }
                try
                {
                    BeginInvoke(() =>
                    {
                        _browserMemory = mem;
                        _sampling = false;
                        UpdateMemoryChip();
                        _sideStatus.Invalidate();
                    });
                }
                catch { _sampling = false; }
            });
        }

        if (DateTime.Now - _lastUpdateCheck >= TimeSpan.FromHours(6)) _ = CheckForUpdatesQuietly();
    }

    private readonly Card _updateBanner = new() { Dock = DockStyle.Top, Height = 64, Visible = false, Elevated = false, Padding = new Padding(16, 12, 12, 12) };
    private readonly Label _updateText = new();
    private DateTime _lastUpdateCheck = DateTime.Now;
    private string? _balloonShownFor;

    private void BuildUpdateBanner()
    {
        _updateBanner.Fill = Theme.AccentSoft;
        _updateText.Dock = DockStyle.Fill;
        _updateText.TextAlign = ContentAlignment.MiddleLeft;
        _updateText.Font = Theme.BodyBold;
        _updateText.ForeColor = Theme.AccentPressed;
        _updateText.BackColor = Theme.AccentSoft;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 300, WrapContents = false, BackColor = Theme.AccentSoft, FlowDirection = FlowDirection.RightToLeft };
        var dismiss = new ModernButton("", ButtonKind.Ghost, "✕") { Size = new Size(38, 38), Margin = new Padding(0) };
        dismiss.Click += (_, _) =>
        {
            if (Updater.Available is { } u) Updater.Dismissed = u.Version.ToString();
            _updateBanner.Visible = false;
        };
        var open = new ModernButton("Ver novedades y actualizar", ButtonKind.Primary, "⬆") { Margin = new Padding(0, 0, 8, 0) };
        open.Size = open.GetPreferredSize(Size.Empty);
        open.Click += (_, _) => ShowUpdates();
        actions.Controls.Add(dismiss);
        actions.Controls.Add(open);
        _updateBanner.Controls.Add(_updateText);
        _updateBanner.Controls.Add(actions);
    }

    private void ShowUpdates()
    {
        if (_settingsPage is null) return;
        if (!Visible) ShowFromTray();
        ShowPage(_settingsPage);
        if (_settingsPage.UpdatesCard is { } card && card.Parent is ScrollableControl sc) sc.ScrollControlIntoView(card);
    }

    private async Task CheckForUpdatesQuietly()
    {
        _lastUpdateCheck = DateTime.Now;
        if (!Updater.Notify) return;
        try { await Updater.CheckAsync(raiseEvent: true); }
        catch { }
    }

    private void OnUpdateFound(UpdateInfo u)
    {
        if (IsDisposed || _tray is null) return;
        bool dismissed = Updater.Dismissed == u.Version.ToString();
        _updateText.Text = $"⬆   Hay una versión nueva de IGP_Soft: {u.Version}" + (u.Version.IsPrerelease ? " (experimental)" : "") +
                           $"  ·  tienes la {Updater.Current}";
        _updateBanner.Visible = !dismissed;
        if (!dismissed && Updater.Notify && _balloonShownFor != u.Version.ToString())
        {
            _balloonShownFor = u.Version.ToString();
            _tray.BalloonTipClicked -= OnBalloonClicked;
            _tray.BalloonTipClicked += OnBalloonClicked;
            _tray.ShowBalloonTip(6000, "IGP_Soft — actualización disponible",
                $"La versión {u.Version} está lista. Haz clic aquí para ver las novedades.", ToolTipIcon.Info);
        }
    }

    private void OnBalloonClicked(object? sender, EventArgs e) => ShowUpdates();

    private void RestartForUpdate()
    {
        _local?.CloseEverything();
        Updater.Restart($"--perfil=\"{ProfileStore.LastSession}\"");
        _reallyExit = true;
        Close();
    }

    private void UpdateMemoryChip()
    {
        long total = ResourceMonitor.PhysicalMemory();
        bool high = total > 0 && _browserMemory > total / 2;
        _chipMemory.Set("Memoria navegador", _browserMemory > 0 ? ResourceMonitor.Format(_browserMemory) : "—");
        _chipMemory.Fill = high ? Color.FromArgb(254, 243, 199) : Theme.Surface;
        _chipMemory.Invalidate();
    }

    private async void ReloadAll(bool manual)
    {
        Resultado r = await _p.RecargarTodo();
        if (!r.Ok && manual && !IsDisposed)
            MessageBox.Show(this, "No hay páginas abiertas para recargar.", "Recargar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearPreviews()
    {
        foreach (MonitorTile t in _tiles)
        {
            Bitmap? p = t.Preview;
            t.Preview = null;
            p?.Dispose();
            t.Invalidate();
        }
    }

    private async Task CapturePreviews()
    {
        if (_capturing || !Visible || WindowState == FormWindowState.Minimized || _tiles.Count == 0 || _lock.Visible) return;
        _capturing = true;
        try
        {
            foreach (MonitorTile tile in _tiles.Where(t => !t.Data.Omitido && t.Width > 8 && t.Height > 8).ToList())
            {
                int width = Remoto ? Math.Clamp(tile.Width, 160, 320) : Math.Clamp(tile.Width * 2, 160, 720);
                byte[]? jpeg = await _p.VistaPrevia(tile.Id, width);
                if (jpeg is null || IsDisposed || !_tiles.Contains(tile) || tile.Data.Omitido || !_chkPreview.Checked) continue;
                Bitmap bmp;
                try
                {
                    using var ms = new MemoryStream(jpeg);
                    using var img = Image.FromStream(ms);
                    bmp = new Bitmap(img);
                }
                catch { continue; }
                Bitmap? old = tile.Preview;
                tile.Preview = bmp;
                old?.Dispose();
                tile.Invalidate();
            }
        }
        finally { _capturing = false; }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        if (m.Msg == Hotkeys.WM_HOTKEY)
        {
            int id = (int)m.WParam;
            BeginInvoke(() => HandleHotkey(id));
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_DISPLAYCHANGE && _local is not null)
            BeginInvoke(() => _ = _p.RecargarMonitores());
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Remoto) return;
        Hotkeys.Register(Handle);
        Hotkeys.Changed += ReRegisterHotkeys;
    }

    private void ReRegisterHotkeys()
    {
        if (IsHandleCreated && !IsDisposed && !Remoto) Hotkeys.Register(Handle);
    }

    private EstadoMonitor? MonitorUnderCursor()
    {
        Point p = Cursor.Position;
        return _estado.Monitores.FirstOrDefault(m => !m.Omitido && new Rectangle(m.X, m.Y, m.Ancho, m.Alto).Contains(p));
    }

    private void HandleHotkey(int id)
    {
        if (Remoto) return;
        string? key = Hotkeys.Actions.FirstOrDefault(a => a.Id == id)?.Key;
        if (_controlador is not null && key != "panel") return;
        EstadoMonitor? under = MonitorUnderCursor();
        switch (key)
        {
            case "ampliar":
                if (under is not null && under.Usables.FirstOrDefault(z => new Rectangle(z.X, z.Y, z.Ancho, z.Alto).Contains(Cursor.Position)) is { } z)
                    _ = _p.Ampliar(under.Id, z.Zona);
                break;
            case "restaurar":
                _ = _p.Restaurar(under?.Id);
                break;
            case "siguiente":
                if (under is not null) _ = _p.AmpliarSiguiente(under.Id, +1);
                break;
            case "anterior":
                if (under is not null) _ = _p.AmpliarSiguiente(under.Id, -1);
                break;
            case "panel":
                if (Visible && WindowState != FormWindowState.Minimized && ActiveForm == this) HideToTray();
                else ShowFromTray();
                break;
            case "abrirorden":
                AutoAssignInOrder();
                break;
            case "cerrartodas":
                CloseAll();
                break;
            case "cerrarmonitor":
                if (under is not null) _ = _p.CerrarMonitor(under.Id);
                break;
        }
    }

    private void ShowMonitorMenu(MonitorTile view)
    {
        EstadoMonitor m = view.Data;
        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripLabel($"Monitor {m.Numero}{(m.Principal ? " (principal)" : "")}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        var incl = new ToolStripMenuItem(m.Omitido ? "Incluir esta pantalla" : "Omitir esta pantalla") { Checked = m.Omitido };
        incl.Click += (_, _) => _ = _p.AlternarOmitir(m.Id);
        menu.Items.Add(incl);
        if (m.Combinado)
            menu.Items.Add(new ToolStripMenuItem("⊟  Separar monitores combinados", null, (_, _) => SeparateMonitors(m)));
        if (_estado.NumeracionPropia)
            menu.Items.Add(new ToolStripMenuItem("↺  Restablecer numeración de monitores", null, (_, _) => _ = _p.NumeracionOriginal()));
        menu.Show(Cursor.Position);
    }

    private async void ShowSegmentMenu(MonitorTile view, EstadoZona zona)
    {
        EstadoMonitor m = view.Data;
        if (_estado.Navegador is null)
        {
            MessageBox.Show(this, "No hay navegador compatible instalado.", "IGP_Soft",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        bool abriendo = zona.Estado == EstadosZona.Abriendo;
        List<VentanaAbierta> ventanas = zona.Desactivada || abriendo ? new() : await _p.VentanasAbiertas();
        if (IsDisposed) return;

        var menu = Theme.Menu();
        int usable = m.Usables.Count();
        string label = zona.Desactivada ? " · zona sin usar" : usable > 1 ? $" · zona {m.NumeroVisible(zona)}/{usable}" : "";
        menu.Items.Add(new ToolStripLabel($"Monitor {m.Numero}{label}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        if (!zona.Desactivada && usable > 1)
        {
            ToolStripMenuItem ampliar = m.Ampliada == zona.Zona
                ? new ToolStripMenuItem("⤡  Volver a la grilla", null, (_, _) => _ = _p.Restaurar(m.Id))
                : new ToolStripMenuItem("⤢  Ampliar a todo el monitor", null, (_, _) => _ = _p.Ampliar(m.Id, zona.Zona)) { Enabled = zona.Ocupada };
            HotkeyAction act = Hotkeys.Actions[m.Ampliada == zona.Zona ? 1 : 0];
            if (Hotkeys.Get(act) != Keys.None && !Remoto) ampliar.ShortcutKeyDisplayString = Hotkeys.Format(Hotkeys.Get(act));
            menu.Items.Add(ampliar);
        }

        var disenar = new ToolStripMenuItem("▦  Diseñar grilla de este monitor…");
        disenar.Click += (_, _) => EditLayout(m);
        menu.Items.Add(disenar);

        if (zona.Desactivada)
        {
            var usar = new ToolStripMenuItem("✓  Usar esta zona");
            usar.Click += (_, _) => ToggleZone(m, zona);
            menu.Items.Add(usar);
            menu.Show(Cursor.Position);
            return;
        }

        if (m.Zonas.Count > 1)
        {
            var noUsar = new ToolStripMenuItem("⊘  No usar esta zona") { Enabled = usable > 1 };
            noUsar.Click += (_, _) => ToggleZone(m, zona);
            menu.Items.Add(noUsar);
        }

        var omitir = new ToolStripMenuItem("Omitir esta pantalla");
        omitir.Click += (_, _) => _ = _p.AlternarOmitir(m.Id);
        menu.Items.Add(omitir);
        AddMonitorItems(menu, m);
        menu.Items.Add(new ToolStripSeparator());

        if (zona.Estado is EstadosZona.EnPantalla or EstadosZona.Abriendo)
        {
            var cerrar = new ToolStripMenuItem(
                abriendo ? "✖  Cancelar apertura"
                : zona.Adjunta ? "↩  Soltar ventana (devolverla al escritorio)"
                : "✖  Cerrar esta ventana");
            cerrar.Click += (_, _) => _ = _p.Cerrar(m.Id, zona.Zona);
            menu.Items.Add(cerrar);
            menu.Items.Add(new ToolStripSeparator());
        }

        foreach (LinkDef link in LinkStore.Links)
        {
            bool aqui = string.Equals(zona.Url, link.Url, StringComparison.Ordinal);
            bool enOtro = _estado.EstaAbierto(link.Url) && !aqui;
            var item = new ToolStripMenuItem((aqui ? "" : "Abrir: ") + link.Titulo)
            {
                Checked = aqui,
                Enabled = !enOtro && !aqui && !abriendo,
            };
            if (enOtro) item.Text += "   (ya abierto)";
            LinkDef captured = link;
            item.Click += (_, _) => Open(m.Id, zona.Zona, captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("➕  Elemento personalizado (imagen / video / URL)…") { Enabled = !abriendo };
        custom.Click += (_, _) => AddCustomElement(m, zona);
        menu.Items.Add(custom);
        menu.Items.Add(BuildAttachMenu(m, zona, ventanas, abriendo));

        menu.Show(Cursor.Position);
    }

    private ToolStripMenuItem BuildAttachMenu(EstadoMonitor m, EstadoZona zona, List<VentanaAbierta> ventanas, bool abriendo)
    {
        var attach = new ToolStripMenuItem("▣  Adjuntar ventana abierta") { Enabled = !abriendo };
        foreach (VentanaAbierta w in ventanas)
        {
            string title = w.Titulo.Length > 70 ? w.Titulo[..67] + "…" : w.Titulo;
            var item = new ToolStripMenuItem($"{title}   — {w.Proceso}".Replace("&", "&&"))
            {
                Enabled = !_estado.EstaAbierto(w.Spec),
            };
            VentanaAbierta captured = w;
            item.Click += (_, _) => Open(m.Id, zona.Zona, new LinkDef("▣ " + captured.Titulo, captured.Spec));
            attach.DropDownItems.Add(item);
        }
        if (attach.DropDownItems.Count == 0)
            attach.DropDownItems.Add(new ToolStripMenuItem("(no hay ventanas disponibles)") { Enabled = false });
        return attach;
    }

    private async void AddCustomElement(EstadoMonitor m, EstadoZona zona)
    {
        if (m.Omitido) return;
        string titulo, target;
        bool addToList;
        using (var d = new CustomElementForm(m.Numero, Remoto ? TiposPermitidos.Filtro : null))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            (titulo, target, addToList) = (d.Titulo, d.Target, d.AddToList);
        }

        if (AssetStore.IsExternalFile(target))
        {
            if (Remoto)
            {
                if (_envios is null) return;
                string id = m.Id;
                int z = zona.Zona;
                SetCopy(id, z, 0);
                (Resultado R, string? Url) sent;
                try { sent = await _envios.Enviar(target, f => SetCopy(id, z, f)); }
                finally { SetCopy(id, z, null); }
                if (!sent.R.Ok || sent.Url is null)
                {
                    if (sent.R.Mensaje is not null) ShowAviso(sent.R);
                    return;
                }
                target = sent.Url;
            }
            else
            {
                UseWaitCursor = true;
                try { target = await Task.Run(() => AssetStore.Import(target)); }
                finally { UseWaitCursor = false; }
            }
        }

        var link = new LinkDef(titulo, target);
        link = addToList ? LinkStore.Add(link) : link;
        Open(m.Id, zona.Zona, link);
    }

    private string? CopyText(string monitor, int zona) => _copias.GetValueOrDefault((monitor, zona));

    private void SetCopy(string monitor, int zona, double? fraction)
    {
        if (fraction is double f) _copias[(monitor, zona)] = $"Copiando al otro equipo…  {EnviosRemotos.Porcentaje(f)}";
        else _copias.Remove((monitor, zona));
        foreach (MonitorTile t in _tiles)
            if (t.Id == monitor) t.Invalidate();
    }

    private void KeepPanelInFront(bool wasActive)
    {
        if (!wasActive || !Visible || IsDisposed || WindowState == FormWindowState.Minimized) return;
        Activate();
    }

    private void ToggleZone(EstadoMonitor m, EstadoZona zona)
    {
        GridLayout grid = m.Layout();
        var zones = grid.Zones.Select((z, i) => i == zona.Zona ? z with { Off = !z.Off } : z).ToList();
        GridLayout next = grid.With(zones);
        if (next.ActiveCount == 0) return;
        _ = _p.CambiarGrilla(m.Id, next);
    }

    private void ShowGridPicker(MonitorTile view, Point screen)
    {
        EstadoMonitor m = view.Data;
        GridPicker.Show(this, screen, m.Columnas, m.Filas, (c, r) => _ = _p.CambiarGrilla(m.Id, GridLayout.Uniform(c, r)), () => EditLayout(m));
    }

    private void EditLayout(EstadoMonitor m)
    {
        var contents = m.Zonas.Where(z => z.Url is not null).ToDictionary(z => z.Zona, z => z.Titulo ?? "");
        using var f = new LayoutEditorForm(m.Numero, new Size(m.Ancho, m.Alto), m.Layout(), contents);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _ = _p.CambiarGrilla(m.Id, f.Result);
    }

    private void ShowGridPickerForAll(Control anchor)
    {
        EstadoMonitor? first = _estado.Monitores.OrderBy(m => m.Numero).FirstOrDefault(m => !m.Omitido);
        if (first is null) return;
        GridPicker.Show(this, anchor.PointToScreen(new Point(anchor.Width, 0)), first.Columnas, first.Filas, (c, r) => _ = _p.DividirTodos(c, r));
    }

    private async void CloseAll()
    {
        if (_controlador is not null) return;
        Resultado r = await _p.CerrarTodo();
        if (!r.Ok && r.Codigo == "nada_abierto" && !IsDisposed)
            MessageBox.Show(this, "No hay ventanas abiertas por este programa.", "Cerrar todas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void FillProfileMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        foreach (LayoutProfile p in ProfileStore.Get(ProfileStore.LastSession) is { } last
                     ? ProfileStore.Named.Prepend(last) : ProfileStore.Named)
        {
            LayoutProfile captured = p;
            parent.DropDownItems.Add(new ToolStripMenuItem(p.Name, null, (_, _) => ApplyProfile(captured)));
        }
        if (parent.DropDownItems.Count == 0)
            parent.DropDownItems.Add(new ToolStripMenuItem("(no hay perfiles guardados)") { Enabled = false });
    }

    private void RunStartupProfile(string name)
    {
        if (_local is null) return;
        LayoutProfile? p = ProfileStore.Get(name);
        if (p is null && !string.IsNullOrWhiteSpace(name) && !ProfileStore.IsLastSession(name))
            _tray?.ShowBalloonTip(4000, "IGP_Soft", $"No existe el perfil «{name}».", ToolTipIcon.Warning);

        bool hide = _modo == ModoPanel.Controlado;
        if (p is not null && p.LinkCount > 0 && _estado.Navegador is not null)
        {
            ApplyProfile(p);
            hide = true;
        }
        if (hide) HideToTray();
        _local.SessionReady = true;
        if (p is not null) _local.SaveLastSession();
    }

    private sealed class MonitorTile : Panel
    {
        private const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

        private EstadoMonitor _data;
        private GridLayout _grid;

        public EstadoMonitor Data
        {
            get => _data;
            set
            {
                _data = value;
                _grid = value.Layout();
            }
        }

        public string Id => _data.Id;
        public bool DropTarget { get; set; }
        public Bitmap? Preview { get; set; }
        public Func<string, int, string?>? CopyText { get; set; }

        public event Action<MonitorTile, EstadoZona>? ZoneActivated;
        public event Action<MonitorTile>? MonitorMenuRequested;
        public event Action<MonitorTile, Point>? GridRequested;
        public event Action<MonitorTile, Point>? DragMoved;
        public event Action<MonitorTile, Point>? DragDropped;

        private Point _downAt;
        private bool _pressed;
        private bool _dragging;
        private bool _suppressClick;
        private bool _hover;
        private bool _hoverCtrl;
        private int? _hoverZone;
        private Rectangle _ctrlRect;

        public MonitorTile(EstadoMonitor data)
        {
            _data = data;
            _grid = data.Layout();
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; _hoverZone = null; _hoverCtrl = false; Invalidate(); };
        }

        private RectangleF RectOf(EstadoZona z) =>
            z.Zona < _grid.Zones.Count ? _grid.AreaOf(_grid.Zones[z.Zona], new SizeF(Width, Height)) : RectangleF.Empty;

        private EstadoZona? ZoneAt(Point p) => _data.Zonas.FirstOrDefault(z => RectOf(z).Contains(p));

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Preview?.Dispose(); Preview = null; }
            base.Dispose(disposing);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _ctrlRect.Contains(e.Location)) return;
            _downAt = e.Location;
            _pressed = true;
            _dragging = false;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                _suppressClick = true;
                Cursor = Cursors.Hand;
                Capture = false;
                DragDropped?.Invoke(this, PointToScreen(e.Location));
                Invalidate();
            }
            _pressed = false;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_suppressClick) { _suppressClick = false; return; }

            if (!_data.Omitido && _ctrlRect.Contains(e.Location))
            {
                GridRequested?.Invoke(this, PointToScreen(new Point(_ctrlRect.Left, _ctrlRect.Bottom + 2)));
                return;
            }

            if (_data.Omitido) { MonitorMenuRequested?.Invoke(this); return; }

            if (ZoneAt(e.Location) is { } z) ZoneActivated?.Invoke(this, z);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_pressed && !_dragging && (Math.Abs(e.X - _downAt.X) > 10 || Math.Abs(e.Y - _downAt.Y) > 10))
            {
                _dragging = true;
                Capture = true;
                Cursor = Cursors.SizeAll;
                _hoverZone = null;
                Invalidate();
            }
            if (_dragging)
            {
                DragMoved?.Invoke(this, PointToScreen(e.Location));
                return;
            }
            int? over = _data.Omitido || _ctrlRect.Contains(e.Location) ? null : ZoneAt(e.Location)?.Zona;
            bool onCtrl = !_data.Omitido && _ctrlRect.Contains(e.Location);
            if (over == _hoverZone && onCtrl == _hoverCtrl) return;
            _hoverZone = over;
            _hoverCtrl = onCtrl;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);
            Theme.Smooth(g);

            bool excluded = _data.Omitido;
            bool preview = Preview is not null && !excluded;
            bool live = _data.Zonas.Any(z => z.Estado == EstadosZona.EnPantalla);
            var card = new RectangleF(3, 2, Width - 7, Height - 8);
            Theme.Shadow(g, card, 14, _hover ? 6 : 4);
            using var clip = Theme.Round(card, 14);

            Theme.FillRound(g, excluded ? Color.FromArgb(226, 232, 240) : Color.FromArgb(30, 41, 59), card, 14);
            g.SetClip(clip);
            if (preview)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(Preview!, card);
            }

            if (!excluded)
            {
                bool multi = _data.Zonas.Count > 1;
                foreach (EstadoZona s in _data.Zonas)
                {
                    RectangleF zr = RectOf(s);
                    zr.Intersect(card);
                    zr.Inflate(multi ? -3f : -6f, multi ? -3f : -6f);
                    if (zr.Width < 4 || zr.Height < 4) continue;
                    bool hover = s.Zona == _hoverZone;

                    if (s.Desactivada)
                    {
                        using var hatch = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.BackwardDiagonal,
                            Color.FromArgb(70, 148, 163, 184), Color.FromArgb(preview ? 170 : 90, 15, 23, 42));
                        using (var hp = Theme.Round(zr, 8)) g.FillPath(hatch, hp);
                        TextRenderer.DrawText(g, "sin usar", Theme.Small, Rectangle.Round(zr), Color.FromArgb(203, 213, 225), C);
                        continue;
                    }

                    if (_data.Ampliada is { } amp && s.Zona != amp)
                    {
                        Theme.FillRound(g, Color.FromArgb(150, 15, 23, 42), zr, 8);
                        TextRenderer.DrawText(g, "detrás", Theme.Small, Rectangle.Round(zr), Color.FromArgb(148, 163, 184), C);
                        continue;
                    }
                    if (_data.Ampliada == s.Zona)
                    {
                        Theme.DrawRound(g, Color.FromArgb(250, 204, 21), zr, 8, 2.5f);
                        var badge = new RectangleF(zr.X + 8, zr.Y + 40, 150, 22);
                        if (zr.Width > 170 && zr.Height > 90)
                        {
                            Theme.FillRound(g, Color.FromArgb(250, 204, 21), badge, 11);
                            TextRenderer.DrawText(g, "⤢  Ampliada al monitor", Theme.SmallBold, Rectangle.Round(badge), Color.FromArgb(66, 32, 6), C);
                        }
                    }

                    bool pending = s.Estado == EstadosZona.Abriendo;
                    Color fill = hover ? Color.FromArgb(preview ? 90 : 120, 37, 99, 235)
                               : s.Url is not null ? Color.FromArgb(preview ? 0 : 60, 37, 99, 235)
                               : Color.FromArgb(preview ? 40 : 18, 255, 255, 255);
                    if (fill.A > 0) Theme.FillRound(g, fill, zr, 8);
                    if (multi || hover)
                    {
                        using var zp = Theme.Round(zr, 8);
                        using var pen = new Pen(hover ? Color.FromArgb(147, 197, 253) : Color.FromArgb(preview ? 150 : 60, 255, 255, 255), hover ? 2f : 1f);
                        if (s.Url is null && !hover) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        g.DrawPath(pen, zp);
                    }

                    string? copy = CopyText?.Invoke(_data.Id, s.Zona);
                    if (copy is not null) pending = true;
                    if (s.Url is null && !pending)
                    {
                        string hint = hover ? "＋  Elegir contenido" : "＋";
                        TextRenderer.DrawText(g, hint, hover ? Theme.BodyBold : Theme.Section, Rectangle.Round(zr),
                            hover ? Color.White : Color.FromArgb(preview ? 200 : 110, 255, 255, 255), C);
                        continue;
                    }

                    string title = s.Titulo ?? "";
                    Color dot = pending ? Color.FromArgb(250, 204, 21) : Color.FromArgb(74, 222, 128);
                    string txt = copy ?? (pending ? "Abriendo " + title : title);
                    int maxW = (int)zr.Width - 16;
                    int tw = Math.Min(maxW - 22, TextRenderer.MeasureText(txt, Theme.Small).Width);
                    if (maxW < 40) continue;
                    var pill = new RectangleF(zr.X + 8, zr.Bottom - 30, tw + 28, 22);
                    Theme.FillRound(g, Color.FromArgb(215, 15, 23, 42), pill, 11);
                    using (var db = new SolidBrush(dot)) g.FillEllipse(db, pill.X + 9, pill.Y + 7.5f, 7, 7);
                    TextRenderer.DrawText(g, txt, Theme.Small, new Rectangle((int)pill.X + 20, (int)pill.Y, tw + 4, (int)pill.Height),
                        Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
            g.ResetClip();

            if (_dragging || DropTarget)
            {
                g.SetClip(clip);
                Theme.FillRound(g, _dragging ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(150, 37, 99, 235), card, 14);
                g.ResetClip();
                string msg = _dragging ? "Suelta sobre otro monitor" : $"⇄  Intercambiar con el monitor {_data.Numero}";
                TextRenderer.DrawText(g, msg, Theme.Section, Rectangle.Round(card), _dragging ? Theme.Text : Color.White, C);
            }

            Color border = DropTarget ? Color.FromArgb(147, 197, 253) : excluded ? Theme.BorderStrong : _hover ? Theme.Accent : live ? Theme.Success : Color.FromArgb(51, 65, 85);
            using (var bp = new Pen(border, DropTarget ? 3f : _hover || live ? 2f : 1f))
            {
                if (excluded) bp.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawPath(bp, clip);
            }

            string name = $"Monitor {_data.Numero}" + (_data.Combinado ? $" · Combinado ({_data.Miembros})" : _data.Principal ? " · Principal" : "");
            int nw = TextRenderer.MeasureText(name, Theme.SmallBold).Width + 20;
            var tag = new RectangleF(card.X + 10, card.Y + 10, nw, 24);
            Theme.FillRound(g, excluded ? Color.FromArgb(203, 213, 225) : Color.FromArgb(215, 15, 23, 42), tag, 12);
            TextRenderer.DrawText(g, name, Theme.SmallBold, Rectangle.Round(tag), excluded ? Theme.TextMuted : Color.White, C);

            if (!excluded)
            {
                string label = $"⊞  {_grid.Describe()}  ▾";
                int w = TextRenderer.MeasureText(label, Theme.SmallBold).Width + 22;
                _ctrlRect = new Rectangle((int)(card.Right - w - 10), (int)card.Y + 10, w, 24);
                Theme.FillRound(g, _hoverCtrl ? Theme.Accent : Color.FromArgb(215, 15, 23, 42), _ctrlRect, 12);
                TextRenderer.DrawText(g, label, Theme.SmallBold, _ctrlRect, Color.White, C);
            }
            else
            {
                _ctrlRect = Rectangle.Empty;
                var center = Rectangle.Round(card);
                TextRenderer.DrawText(g, "Pantalla omitida", Theme.Section, new Rectangle(center.X, center.Y + center.Height / 2 - 24, center.Width, 26),
                    Theme.TextMuted, C);
                TextRenderer.DrawText(g, "Clic para volver a incluirla", Theme.Small, new Rectangle(center.X, center.Y + center.Height / 2 + 4, center.Width, 20),
                    Theme.TextSubtle, C);
            }
        }
    }
}
