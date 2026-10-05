using System.Threading;

namespace IGP_Soft;

internal sealed class MainForm : Form
{
    private Browser? _browser;
    private List<Browser> _browsers = new();
    private List<MonitorInfo> _monitors = new();
    private readonly HashSet<string> _openedUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedDevices = new();
    private readonly List<MonitorTile> _tiles = new();
    private readonly Dictionary<string, DisplaySurface> _surfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Form> _identifyForms = new();
    private readonly List<Task<LaunchedWindow?>> _pendingLaunches = new();
    private bool _skipPrimaryTouched;

    private readonly Label _info = new();
    private readonly Panel _canvas = new();
    private readonly ComboBox _cboBrowser = new();
    private readonly CheckBox _chkPreview = new();
    private readonly CheckBox _chkSkipPrimary = new();
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 1500 };
    private volatile bool _capturing;
    private NotifyIcon _tray = null!;
    private Icon? _trayIcon;
    private bool _reallyExit;
    private bool _trayHintShown;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showReg;
    private readonly Button _btnAuto = new();
    private readonly Button _btnLinks = new();
    private readonly Button _btnIdentify = new();
    private readonly Button _btnCloseAll = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnProfiles = new();
    private ToolStripMenuItem _trayProfiles = null!;
    private bool _sessionReady;
    private bool _applyingProfile;

    public MainForm(string startupProfile)
    {
        Text = "IGP_Soft — Pantallas de monitoreo IGP";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
        Size = new Size(980, 600);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;
        TopMost = false;

        _browsers = BrowserLocator.InstalledAll();
        string? want = Config.Get("browser") ?? BrowserLocator.DefaultName();
        _browser = _browsers.FirstOrDefault(b => b.Name.Equals(want, StringComparison.OrdinalIgnoreCase))
                   ?? _browsers.FirstOrDefault();

        BuildLayout();
        BuildTray();
        Profiles.CleanupOld();
        ReloadMonitors();

        if (_browser is null)
        {
            _info.ForeColor = Color.Firebrick;
            _info.Text = "ERROR: no hay navegador compatible (Edge, Chrome, Brave, Firefox u Opera). Instale uno.";
        }

        _previewTimer.Tick += (_, _) =>
        {
            CheckClosedWindows();
            EnforceWindows();
            ApplyPanelStacking();
            if (_chkPreview.Checked) CapturePreviews();
        };
        _previewTimer.Start();
        WindowWatcher.Start(OnWindowMoved);

        Shown += (_, _) => BeginInvoke(() => RunStartupProfile(startupProfile));

        FormClosing += (_, e) =>
        {
            if (_reallyExit) return;
            if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) return;
            e.Cancel = true;
            HideToTray();
        };

        FormClosed += (_, _) =>
        {
            _previewTimer.Stop();
            WindowWatcher.Stop();
            _showReg?.Unregister(null);
            _showEvent?.Dispose();
            CloseIdentifyForms();
            CloseAllSurfaces();
            CloseInFlightLaunches();
            ClearPreviews();
            _tray.Visible = false;
            _tray.Dispose();
            _trayIcon?.Dispose();
        };
    }

    private void BuildLayout()
    {
        _info.Dock = DockStyle.Top;
        _info.Height = 34;
        _info.TextAlign = ContentAlignment.MiddleLeft;
        _info.Padding = new Padding(12, 0, 12, 0);
        _info.Text = _browser is null ? "Buscando navegador…" : $"Navegador: {_browser.Name}";

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0),
            ForeColor = Color.DimGray,
            Text = "Clic en una zona → elige el enlace.  Con  − n +  (arriba de cada monitor) divides ese monitor en 2, 4, 6… partes.",
        };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(12, 8, 12, 8) };

        var lblBrowser = new Label { Text = "Navegador:", AutoSize = true, Margin = new Padding(0, 9, 4, 10) };
        _cboBrowser.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboBrowser.Width = 110;
        _cboBrowser.Margin = new Padding(0, 5, 20, 10);
        foreach (Browser b in _browsers) _cboBrowser.Items.Add(b.Name);
        if (_browser is not null) _cboBrowser.SelectedItem = _browser.Name;
        _cboBrowser.Enabled = _browsers.Count > 0;
        _cboBrowser.SelectedIndexChanged += (_, _) =>
        {
            Browser? b = _browsers.FirstOrDefault(x => x.Name == (_cboBrowser.SelectedItem as string));
            if (b is not null) { _browser = b; Config.Set("browser", b.Name); UpdateInfo(); }
        };

        _chkPreview.Text = "Vista previa en vivo";
        _chkPreview.Checked = true;
        _chkPreview.AutoSize = true;
        _chkPreview.Margin = new Padding(0, 6, 20, 10);
        _chkPreview.CheckedChanged += (_, _) => { if (!_chkPreview.Checked) ClearPreviews(); };

        _chkSkipPrimary.Text = "Omitir la pantalla principal (laptop)";
        _chkSkipPrimary.AutoSize = true;
        _chkSkipPrimary.Margin = new Padding(0, 6, 16, 10);
        _chkSkipPrimary.CheckedChanged += (_, _) => { _skipPrimaryTouched = true; ApplySkipPrimary(); SaveLastSession(); };

        _btnAuto.Text = "Abrir en orden";
        _btnLinks.Text = "Enlaces…";
        _btnIdentify.Text = "Identificar monitores";
        _btnCloseAll.Text = "Cerrar todas las ventanas";
        _btnRefresh.Text = "Actualizar";
        _btnProfiles.Text = "Perfiles…";
        foreach (Button b in new[] { _btnAuto, _btnProfiles, _btnLinks, _btnIdentify, _btnCloseAll, _btnRefresh })
        {
            b.AutoSize = true;
            b.Padding = new Padding(10, 4, 10, 4);
            b.Margin = new Padding(0, 0, 8, 0);
        }
        _btnAuto.Click += (_, _) => AutoAssignInOrder();
        _btnProfiles.Click += (_, _) => EditProfiles();
        _btnLinks.Click += (_, _) => EditLinks();
        _btnIdentify.Click += (_, _) => IdentifyMonitors();
        _btnCloseAll.Click += (_, _) => CloseAll();
        _btnRefresh.Click += (_, _) => ReloadMonitors();
        bottom.Controls.AddRange(new Control[] { lblBrowser, _cboBrowser, _chkPreview, _chkSkipPrimary, _btnAuto, _btnProfiles, _btnLinks, _btnIdentify, _btnCloseAll, _btnRefresh });
        bottom.SetFlowBreak(_chkSkipPrimary, true);

        _canvas.Dock = DockStyle.Fill;
        _canvas.BackColor = Color.FromArgb(238, 240, 243);
        _canvas.Padding = new Padding(24);
        _canvas.Resize += (_, _) => RelayoutTiles();

        Controls.Add(_canvas);
        Controls.Add(bottom);
        Controls.Add(hint);
        Controls.Add(_info);
    }

    private void BuildTray()
    {
        _trayIcon = BuildIcon();
        Icon = _trayIcon;

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Abrir panel", null, (_, _) => ShowFromTray()) { Font = new Font(Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        _trayProfiles = new ToolStripMenuItem("Abrir perfil");
        menu.Items.Add(_trayProfiles);
        menu.Opening += (_, _) => FillProfileMenu(_trayProfiles);
        menu.Items.Add(new ToolStripMenuItem("Cerrar todas las ventanas", null, (_, _) => CloseAll()));
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

    private void HideToTray()
    {
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
        TopMost = IsWallActive();
        BringToFront();
        Activate();
    }

    private void ExitApp()
    {
        if (!Visible) ShowFromTray();
        if (_tiles.Any(t => t.Segments.Any(s => s.Launched is not null || s.Pending is not null)))
        {
            DialogResult r = MessageBox.Show(this,
                "Al salir se cerrarán las pantallas de monitoreo y su contenido " +
                "(las ventanas adjuntas vuelven al escritorio, no se cierran). ¿Desea continuar?",
                "Salir de IGP_Soft", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;

            var windows = _tiles.SelectMany(t => t.Segments)
                                .Where(s => s.Launched is not null)
                                .Select(s => s.Launched!)
                                .ToList();
            CloseAllSurfaces();
            foreach (LaunchedWindow window in windows) WindowManager.Close(window);
        }
        _reallyExit = true;
        Close();
    }

    private void EditLinks()
    {
        using var f = new LinksForm();
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            foreach (MonitorTile t in _tiles) t.Invalidate();
            UpdateInfo();
        }
    }

    private void ReloadMonitors()
    {
        CloseIdentifyForms();

        List<MonitorInfo> current = MonitorEnumerator.GetOrdered();
        var currentDevices = current.Select(m => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorTile missing in _tiles.Where(t => !currentDevices.Contains(t.Monitor.DeviceName)).ToList())
            CloseTileWindows(missing);

        var prev = new Dictionary<string, (int seg, List<(LinkDef? Link, LaunchedWindow? Win, object? Pending)> items)>();
        foreach (MonitorTile t in _tiles)
            prev[t.Monitor.DeviceName] = (t.SegCount, t.Segments.Select(s => (s.Link, s.Launched, s.Pending)).ToList());

        foreach (MonitorTile t in _tiles) { _canvas.Controls.Remove(t); t.Dispose(); }
        _tiles.Clear();

        _monitors = current;

        if (!_skipPrimaryTouched)
            _chkSkipPrimary.Checked = _monitors.Count > LinkStore.Links.Count;

        for (int i = 0; i < _monitors.Count; i++)
        {
            MonitorInfo m = _monitors[i];
            var tile = new MonitorTile(i + 1, m) { Excluded = _excludedDevices.Contains(m.DeviceName) };

            if (prev.TryGetValue(m.DeviceName, out var pv))
            {
                tile.BuildSegments(pv.seg);
                for (int k = 0; k < tile.Segments.Count && k < pv.items.Count; k++)
                {
                    tile.Segments[k].Link = pv.items[k].Link;
                    tile.Segments[k].Launched = pv.items[k].Win;
                    tile.Segments[k].Pending = pv.items[k].Pending;
                }
            }

            tile.SegmentActivated += ShowSegmentMenu;
            tile.MonitorMenuRequested += ShowMonitorMenu;
            tile.SegStep += ChangeSegments;
            _tiles.Add(tile);
            _canvas.Controls.Add(tile);

            if (_surfaces.TryGetValue(m.DeviceName, out DisplaySurface? surface) && !surface.IsDisposed)
            {
                surface.Place(m);
                foreach (Segment s in tile.Segments)
                    if (s.Launched is not null)
                        WindowManager.Reposition(s.Launched, ToSurfaceArea(tile, s.Area));
            }
        }

        ApplySkipPrimary();
        RelayoutTiles();
        UpdateInfo();
    }

    private void ApplySkipPrimary()
    {
        foreach (MonitorTile t in _tiles)
        {
            if (!t.Monitor.IsPrimary) continue;
            if (_chkSkipPrimary.Checked)
            {
                CloseTileWindows(t);
                t.Excluded = true;
                _excludedDevices.Add(t.Monitor.DeviceName);
            }
            else
            {
                t.Excluded = false;
                _excludedDevices.Remove(t.Monitor.DeviceName);
            }
            t.Invalidate();
        }
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        if (_browser is null) return;
        int abiertas = _tiles.Sum(t => t.Segments.Count(s => s.Launched is not null));
        int zonas = _tiles.Where(t => !t.Excluded).Sum(t => t.Segments.Count);
        _info.Text = $"Navegador: {_browser.Name}   ·   Monitores: {_monitors.Count}   ·   Zonas: {zonas}" +
                     $"   ·   Enlaces: {LinkStore.Links.Count}   ·   Ventanas abiertas: {abiertas}";
    }

    private void RelayoutTiles()
    {
        if (_tiles.Count == 0) return;

        int minX = _monitors.Min(m => m.X);
        int minY = _monitors.Min(m => m.Y);
        int maxX = _monitors.Max(m => m.X + m.Width);
        int maxY = _monitors.Max(m => m.Y + m.Height);

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

        const int gap = 4;
        foreach (MonitorTile t in _tiles)
        {
            MonitorInfo m = t.Monitor;
            t.Bounds = new Rectangle(
                offX + (int)((m.X - minX) * scale) + gap,
                offY + (int)((m.Y - minY) * scale) + gap,
                Math.Max(120, (int)(m.Width * scale) - gap * 2),
                Math.Max(80, (int)(m.Height * scale) - gap * 2));
        }
    }

    private void CheckClosedWindows()
    {
        bool cambio = false;
        foreach (MonitorTile t in _tiles.ToList())
        {
            foreach (Segment s in t.Segments.ToList())
            {
                if (s.Launched is null || NativeMethods.IsWindow(s.Launched.Hwnd)) continue;

                LaunchedWindow w = s.Launched;
                s.Launched = null;
                if (s.Link is not null) { _openedUrls.Remove(s.Link.Url); s.Link = null; }
                t.Invalidate();
                Task.Run(() => WindowManager.Close(w));
                cambio = true;
            }
            CloseSurfaceIfEmpty(t);
        }
        if (cambio) UpdateInfo();
    }

    private bool IsWallActive()
    {
        return _tiles.Any(t => !t.Excluded && t.Segments.Any(s => s.Launched is not null || s.Pending is not null));
    }

    private void OnWindowMoved(IntPtr hwnd)
    {
        if (_identifyForms.Any(f => !f.IsDisposed)) return;
        foreach (MonitorTile t in _tiles)
            foreach (Segment s in t.Segments)
                if (s.Launched is { } w && w.Hwnd == hwnd)
                {
                    WindowManager.Reposition(w, ToSurfaceArea(t, s.Area));
                    return;
                }
    }

    private void EnforceWindows()
    {
        if (_identifyForms.Any(f => !f.IsDisposed)) return;

        foreach (MonitorTile t in _tiles)
            foreach (Segment s in t.Segments)
                if (s.Launched is not null)
                    WindowManager.Reposition(s.Launched, ToSurfaceArea(t, s.Area));
    }

    private void ApplyPanelStacking()
    {
        if (_identifyForms.Any(f => !f.IsDisposed)) return;

        bool wall = IsWallActive();
        if (wall)
        {
            if (!TopMost) TopMost = true;
            if (Visible && !IsDisposed)
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        else if (TopMost)
        {
            TopMost = false;
        }
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

    private void CapturePreviews()
    {
        if (_capturing || !Visible || WindowState == FormWindowState.Minimized || _tiles.Count == 0) return;
        _capturing = true;

        var jobs = _tiles.Where(t => !t.Excluded && t.Width > 8 && t.Height > 8)
                         .Select(t => (Tile: t, t.Monitor, t.Width)).ToList();

        Task.Run(() =>
        {
            var done = new List<(MonitorTile Tile, Bitmap Bmp)>();
            foreach (var j in jobs)
            {
                try
                {
                    int tw = Math.Clamp(j.Width * 2, 160, 720);
                    int th = Math.Max(1, (int)(tw * (double)j.Monitor.Height / j.Monitor.Width));
                    using var full = new Bitmap(j.Monitor.Width, j.Monitor.Height);
                    using (Graphics gf = Graphics.FromImage(full))
                        gf.CopyFromScreen(j.Monitor.X, j.Monitor.Y, 0, 0, new Size(j.Monitor.Width, j.Monitor.Height));
                    var thumb = new Bitmap(tw, th);
                    using (Graphics gt = Graphics.FromImage(thumb))
                    {
                        gt.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                        gt.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        gt.DrawImage(full, new Rectangle(0, 0, tw, th));
                    }
                    done.Add((j.Tile, thumb));
                }
                catch { }
            }

            void Apply()
            {
                foreach (var (tile, bmp) in done)
                {
                    if (!IsDisposed && _tiles.Contains(tile) && !tile.Excluded)
                    {
                        Bitmap? old = tile.Preview;
                        tile.Preview = bmp;
                        old?.Dispose();
                        tile.Invalidate();
                    }
                    else bmp.Dispose();
                }
                _capturing = false;
            }

            try
            {
                if (IsDisposed) { foreach (var d in done) d.Bmp.Dispose(); _capturing = false; }
                else BeginInvoke(Apply);
            }
            catch { foreach (var d in done) d.Bmp.Dispose(); _capturing = false; }
        });
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        base.WndProc(ref m);
        if (m.Msg == WM_DISPLAYCHANGE)
            BeginInvoke(ReloadMonitors);
    }

    private void ShowMonitorMenu(MonitorTile tile)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel($"Monitor {tile.Number}{(tile.Monitor.IsPrimary ? " (principal)" : "")}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        var incl = new ToolStripMenuItem(tile.Excluded ? "Incluir esta pantalla" : "Omitir esta pantalla") { Checked = tile.Excluded };
        incl.Click += (_, _) => ToggleExcluded(tile);
        menu.Items.Add(incl);
        menu.Show(Cursor.Position);
    }

    private void ShowSegmentMenu(MonitorTile tile, Segment seg)
    {
        if (_browser is null)
        {
            MessageBox.Show(this, "No hay navegador compatible instalado.", "IGP_Soft",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var menu = new ContextMenuStrip();
        string zona = tile.Segments.Count > 1 ? $" · zona {seg.Index + 1}/{tile.Segments.Count}" : "";
        menu.Items.Add(new ToolStripLabel($"Monitor {tile.Number}{zona}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var omitir = new ToolStripMenuItem("Omitir esta pantalla");
        omitir.Click += (_, _) => ToggleExcluded(tile);
        menu.Items.Add(omitir);
        menu.Items.Add(new ToolStripSeparator());

        if (seg.Launched is not null || seg.Pending is not null)
        {
            var cerrar = new ToolStripMenuItem(
                seg.Pending is not null ? "✖  Cancelar apertura"
                : seg.Launched?.Attached == true ? "↩  Soltar ventana (devolverla al escritorio)"
                : "✖  Cerrar esta ventana");
            cerrar.Click += (_, _) => { CloseSeg(tile, seg); SaveLastSession(); };
            menu.Items.Add(cerrar);
            menu.Items.Add(new ToolStripSeparator());
        }

        foreach (LinkDef link in LinkStore.Links)
        {
            bool aqui = seg.Link?.Url == link.Url;
            bool enOtro = _openedUrls.Contains(link.Url) && !aqui;
            var item = new ToolStripMenuItem((aqui ? "" : "Abrir: ") + link.Titulo)
            {
                Checked = aqui,
                Enabled = !enOtro && !aqui && seg.Pending is null,
            };
            if (enOtro) item.Text += "   (ya abierto)";
            LinkDef captured = link;
            item.Click += (_, _) => AssignAndOpen(tile, seg, captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("➕  Elemento personalizado (imagen / video / URL)…") { Enabled = seg.Pending is null };
        custom.Click += (_, _) => AddCustomElement(tile, seg);
        menu.Items.Add(custom);
        menu.Items.Add(BuildAttachMenu(tile, seg));

        menu.Show(Cursor.Position);
    }

    private ToolStripMenuItem BuildAttachMenu(MonitorTile tile, Segment seg)
    {
        var attach = new ToolStripMenuItem("▣  Adjuntar ventana abierta") { Enabled = seg.Pending is null };
        foreach (OpenWindow w in WindowManager.ListOpenWindows())
        {
            string spec = WindowManager.SpecOf(w);
            string title = w.Title.Length > 70 ? w.Title[..67] + "…" : w.Title;
            var item = new ToolStripMenuItem($"{title}   — {w.ProcessName}".Replace("&", "&&"))
            {
                Enabled = !_openedUrls.Contains(spec),
            };
            OpenWindow captured = w;
            item.Click += (_, _) => AssignAndOpen(tile, seg, new LinkDef("▣ " + captured.Title, spec));
            attach.DropDownItems.Add(item);
        }
        if (attach.DropDownItems.Count == 0)
            attach.DropDownItems.Add(new ToolStripMenuItem("(no hay ventanas disponibles)") { Enabled = false });
        return attach;
    }

    private void AddCustomElement(MonitorTile tile, Segment seg)
    {
        if (tile.Excluded) return;
        using var d = new CustomElementForm(tile.Number);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var link = new LinkDef(d.Titulo, d.Target);
        link = d.AddToList ? LinkStore.Add(link) : link;
        AssignAndOpen(tile, seg, link);
    }

    private async void AssignAndOpen(MonitorTile tile, Segment seg, LinkDef link, bool quiet = false)
    {
        bool isWindow = WindowManager.IsWindowSpec(link.Url);
        if (tile.Excluded || seg.Pending is not null || (_browser is null && !isWindow)) return;

        if (_openedUrls.Contains(link.Url) && seg.Link?.Url != link.Url)
        {
            MessageBox.Show(this, $"El enlace «{link.Titulo}» ya está abierto en otra zona.",
                "Enlace en uso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (seg.Launched is not null || seg.Link is not null) CloseSeg(tile, seg);

        DisplaySurface surface;
        try { surface = EnsureSurface(tile); }
        catch (Exception ex)
        {
            CloseSurfaceIfEmpty(tile);
            MessageBox.Show(this, $"No se pudo preparar la pantalla:\n{ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (isWindow)
        {
            LaunchedWindow? attached = WindowManager.Attach(link.Url, ToSurfaceArea(tile, seg.Area), surface.Handle);
            if (attached is null)
            {
                CloseSurfaceIfEmpty(tile);
                if (!quiet)
                    MessageBox.Show(this,
                        $"No se pudo adjuntar «{link.Titulo.TrimStart('▣', ' ')}».\n\n" +
                        "Puede que la ventana se haya cerrado, o que esa aplicación se ejecute como administrador " +
                        "(en ese caso, abra IGP_Soft también como administrador).",
                        "No se pudo adjuntar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            seg.Link = link;
            seg.Launched = attached;
            _openedUrls.Add(link.Url);
            tile.Invalidate();
            UpdateInfo();
            SaveLastSession();
            EnforceWindows();
            ApplyPanelStacking();
            return;
        }

        var token = new object();
        seg.Pending = token;
        seg.Link = link;
        _openedUrls.Add(link.Url);
        tile.Invalidate();
        UpdateInfo();
        SaveLastSession();

        Browser browser = _browser!;
        bool keepData = Profiles.KeepData;
        Rectangle area = ToSurfaceArea(tile, seg.Area);
        string tag = $"{tile.Monitor.ShortName}-{seg.Index}";
        IntPtr parent = surface.Handle;

        Task<LaunchedWindow?> launch = Task.Run(() => WindowManager.LaunchAndPlace(browser, link.Url, area, tag, parent, keepData));
        _pendingLaunches.Add(launch);
        LaunchedWindow? win = null;
        string? error = null;
        try { win = await launch; }
        catch (Exception ex) { error = ex.Message; }
        finally { _pendingLaunches.Remove(launch); }

        (MonitorTile? t, Segment? s) = FindPending(token);
        if (IsDisposed || t is null || s is null)
        {
            if (win is not null) _ = Task.Run(() => WindowManager.Close(win));
            return;
        }

        s.Pending = null;
        if (win is null)
        {
            _openedUrls.Remove(link.Url);
            s.Link = null;
            t.Invalidate();
            UpdateInfo();
            CloseSurfaceIfEmpty(t);
            if (quiet) return;
            MessageBox.Show(this,
                error is not null
                    ? $"No se pudo abrir el navegador:\n{error}"
                    : $"El navegador se inició, pero no se pudo integrar «{link.Titulo}» en la pantalla. " +
                      "La zona quedó libre para intentarlo nuevamente.",
                "No se pudo abrir", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        s.Launched = win;
        t.Invalidate();
        UpdateInfo();
        EnforceWindows();
        ApplyPanelStacking();
    }

    private (MonitorTile? Tile, Segment? Seg) FindPending(object token)
    {
        foreach (MonitorTile t in _tiles)
            foreach (Segment s in t.Segments)
                if (ReferenceEquals(s.Pending, token)) return (t, s);
        return (null, null);
    }

    private void AutoAssignInOrder()
    {
        var targets = new List<(MonitorTile T, Segment S)>();
        foreach (MonitorTile t in _tiles)
            if (!t.Excluded)
                foreach (Segment s in t.Segments)
                    targets.Add((t, s));

        if (targets.Count == 0)
        {
            MessageBox.Show(this, "No hay zonas disponibles (todos los monitores están omitidos).",
                "Abrir en orden", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int n = Math.Min(targets.Count, LinkStore.Links.Count);
        for (int i = 0; i < n; i++)
        {
            (MonitorTile t, Segment s) = targets[i];
            LinkDef link = LinkStore.Links[i];
            if (s.Link?.Url == link.Url || s.Pending is not null) continue;
            if (_openedUrls.Contains(link.Url)) continue;
            AssignAndOpen(t, s, link);
        }
    }

    private void ChangeSegments(MonitorTile tile, int delta)
    {
        int idx = Array.IndexOf(MonitorTile.Steps, tile.SegCount);
        int next = Math.Clamp(idx + delta, 0, MonitorTile.Steps.Length - 1);
        if (next == idx) return;

        SetSegCount(tile, MonitorTile.Steps[next]);
        SaveLastSession();
    }

    private void SetSegCount(MonitorTile tile, int newCount)
    {
        if (newCount == tile.SegCount) return;

        if (newCount < tile.Segments.Count)
            for (int k = newCount; k < tile.Segments.Count; k++)
                if (tile.Segments[k].Launched is not null || tile.Segments[k].Link is not null ||
                    tile.Segments[k].Pending is not null)
                    CloseSeg(tile, tile.Segments[k]);

        tile.BuildSegments(newCount);

        foreach (Segment s in tile.Segments)
            if (s.Launched is not null)
                WindowManager.Reposition(s.Launched, ToSurfaceArea(tile, s.Area));

        UpdateInfo();
    }

    private void CloseSeg(MonitorTile tile, Segment seg)
    {
        if (seg.Launched is not null)
        {
            LaunchedWindow w = seg.Launched;
            if (w.Attached) WindowManager.Release(w);
            else Task.Run(() => WindowManager.Close(w));
            seg.Launched = null;
        }
        seg.Pending = null;
        if (seg.Link is not null)
        {
            _openedUrls.Remove(seg.Link.Url);
            seg.Link = null;
        }
        tile.Invalidate();
        UpdateInfo();
        CloseSurfaceIfEmpty(tile);
    }

    private void CloseTileWindows(MonitorTile tile)
    {
        foreach (Segment s in tile.Segments.ToList())
            if (s.Launched is not null || s.Link is not null || s.Pending is not null)
                CloseSeg(tile, s);
    }

    private void CloseAllWindows()
    {
        foreach (MonitorTile t in _tiles.ToList()) CloseTileWindows(t);
        CloseAllSurfaces();
    }

    private DisplaySurface EnsureSurface(MonitorTile tile)
    {
        if (!_surfaces.TryGetValue(tile.Monitor.DeviceName, out DisplaySurface? surface) || surface.IsDisposed)
        {
            surface = new DisplaySurface(tile.Monitor);
            _surfaces[tile.Monitor.DeviceName] = surface;
            surface.Show();
        }
        surface.Place(tile.Monitor);
        TopMost = true;
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        return surface;
    }

    private static Rectangle ToSurfaceArea(MonitorTile tile, Rectangle screenArea) =>
        new(screenArea.X - tile.Monitor.X, screenArea.Y - tile.Monitor.Y,
            screenArea.Width, screenArea.Height);

    private void CloseSurfaceIfEmpty(MonitorTile tile)
    {
        if (tile.Segments.Any(s => s.Launched is not null || s.Pending is not null)) return;
        if (!_surfaces.Remove(tile.Monitor.DeviceName, out DisplaySurface? surface)) return;
        if (!surface.IsDisposed) surface.Close();
        ApplyPanelStacking();
    }

    private void CloseAllSurfaces()
    {
        foreach (Segment s in _tiles.SelectMany(t => t.Segments))
            if (s.Launched is { Attached: true } w)
                WindowManager.Release(w);
        foreach (DisplaySurface surface in _surfaces.Values.ToList())
            if (!surface.IsDisposed) surface.Close();
        _surfaces.Clear();
    }

    private void CloseInFlightLaunches()
    {
        foreach (Task<LaunchedWindow?> launch in _pendingLaunches.ToList())
        {
            try
            {
                if (launch.Wait(TimeSpan.FromSeconds(20)) && launch.Result is { } w)
                    WindowManager.Close(w);
            }
            catch { }
        }
        _pendingLaunches.Clear();
    }

    private void CloseAll()
    {
        if (_tiles.All(t => t.Segments.All(s => s.Launched is null && s.Pending is null)))
        {
            MessageBox.Show(this, "No hay ventanas abiertas por este programa.", "Cerrar todas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        CloseAllWindows();
        SaveLastSession();
    }

    private void ToggleExcluded(MonitorTile tile)
    {
        if (!tile.Excluded) CloseTileWindows(tile);
        tile.Excluded = !tile.Excluded;
        if (tile.Excluded) _excludedDevices.Add(tile.Monitor.DeviceName);
        else _excludedDevices.Remove(tile.Monitor.DeviceName);
        if (tile.Monitor.IsPrimary)
        {
            _skipPrimaryTouched = true;
            _chkSkipPrimary.Checked = tile.Excluded;
        }
        tile.Invalidate();
        UpdateInfo();
        SaveLastSession();
    }

    private void EditProfiles()
    {
        using var f = new ProfilesForm(Snapshot);
        f.ShowDialog(this);
        if (f.ToOpen is not null) ApplyProfile(f.ToOpen);
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

    private LayoutProfile Snapshot(string name) => new()
    {
        Name = name,
        Browser = _browser?.Name,
        Saved = DateTime.Now,
        Monitors = _tiles.Select(t => new MonitorLayout
        {
            Number = t.Number,
            Device = t.Monitor.DeviceName,
            Primary = t.Monitor.IsPrimary,
            Excluded = t.Excluded,
            Zones = t.SegCount,
            Links = t.Segments.Where(s => s.Link is not null)
                              .Select(s => new ZoneLayout { Zone = s.Index, Titulo = s.Link!.Titulo, Url = s.Link.Url })
                              .ToList(),
        }).ToList(),
    };

    private void SaveLastSession()
    {
        if (!_sessionReady || _applyingProfile) return;
        ProfileStore.Put(Snapshot(ProfileStore.LastSession));
    }

    private void RunStartupProfile(string name)
    {
        LayoutProfile? p = ProfileStore.Get(name);
        if (p is null && !string.IsNullOrWhiteSpace(name) && !ProfileStore.IsLastSession(name))
            _tray.ShowBalloonTip(4000, "IGP_Soft", $"No existe el perfil «{name}».", ToolTipIcon.Warning);

        if (p is not null && p.LinkCount > 0 && _browser is not null)
        {
            ApplyProfile(p);
            HideToTray();
        }
        _sessionReady = true;
        if (p is not null) SaveLastSession();
    }

    private void ApplyProfile(LayoutProfile p)
    {
        if (p.Browser is not null && !p.Browser.Equals(_browser?.Name, StringComparison.OrdinalIgnoreCase) &&
            _browsers.Any(b => b.Name.Equals(p.Browser, StringComparison.OrdinalIgnoreCase)))
            _cboBrowser.SelectedItem = _browsers.First(b => b.Name.Equals(p.Browser, StringComparison.OrdinalIgnoreCase)).Name;
        if (_browser is null) return;

        var plan = new List<(MonitorTile Tile, MonitorLayout Layout)>();
        var used = new HashSet<MonitorTile>();
        foreach (MonitorLayout ml in p.Monitors)
            if (_tiles.FirstOrDefault(t => !used.Contains(t) &&
                    t.Monitor.DeviceName.Equals(ml.Device, StringComparison.OrdinalIgnoreCase)) is { } t)
            { used.Add(t); plan.Add((t, ml)); }
        foreach (MonitorLayout ml in p.Monitors.Where(ml => plan.All(x => x.Layout != ml)))
            if (_tiles.FirstOrDefault(t => !used.Contains(t) && t.Number == ml.Number) is { } t)
            { used.Add(t); plan.Add((t, ml)); }

        _applyingProfile = true;
        try
        {
            foreach (MonitorTile t in _tiles.Where(t => !used.Contains(t))) CloseTileWindows(t);

            var wanted = new List<(MonitorTile Tile, Segment Seg, LinkDef Link)>();
            foreach ((MonitorTile t, MonitorLayout ml) in plan)
            {
                if (t.Excluded != ml.Excluded) ToggleExcluded(t);
                if (t.Excluded) continue;

                int zones = MonitorTile.Steps.FirstOrDefault(n => n >= ml.Zones, MonitorTile.Steps[^1]);
                SetSegCount(t, zones);

                foreach (Segment s in t.Segments)
                {
                    ZoneLayout? z = ml.Links.FirstOrDefault(l => l.Zone == s.Index);
                    LinkDef? link = z is null ? null
                        : LinkStore.Links.FirstOrDefault(l => l.Url.Equals(z.Url, StringComparison.OrdinalIgnoreCase))
                          ?? new LinkDef(z.Titulo, z.Url);
                    if (s.Link?.Url.Equals(link?.Url, StringComparison.OrdinalIgnoreCase) == true) continue;

                    if (s.Link is not null || s.Launched is not null || s.Pending is not null) CloseSeg(t, s);
                    if (link is not null) wanted.Add((t, s, link));
                }
            }

            foreach ((MonitorTile t, Segment s, LinkDef link) in wanted)
                if (!_openedUrls.Contains(link.Url))
                    AssignAndOpen(t, s, link, quiet: true);
        }
        finally { _applyingProfile = false; }

        UpdateInfo();
        SaveLastSession();
    }

    private void IdentifyMonitors()
    {
        CloseIdentifyForms();

        for (int i = 0; i < _monitors.Count; i++)
        {
            MonitorInfo m = _monitors[i];
            bool excl = _excludedDevices.Contains(m.DeviceName);
            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = excl ? Color.FromArgb(120, 120, 120) : Color.FromArgb(0, 120, 215),
                ControlBox = false,
            };
            f.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = excl ? $"{i + 1}\n(omitida)" : (i + 1).ToString(),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", excl ? 48f : 96f, FontStyle.Bold),
            });

            const int w = 300, h = 220;
            f.Show();
            NativeMethods.SetWindowPos(f.Handle, IntPtr.Zero,
                m.X + (m.Width - w) / 2, m.Y + (m.Height - h) / 2, w, h,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);

            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            Form fc = f;
            timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); if (!fc.IsDisposed) fc.Close(); };
            timer.Start();
            _identifyForms.Add(f);
        }
    }

    private void CloseIdentifyForms()
    {
        foreach (Form f in _identifyForms)
            if (!f.IsDisposed) f.Close();
        _identifyForms.Clear();
    }

    private sealed class DisplaySurface : Form
    {
        public DisplaySurface(MonitorInfo monitor)
        {
            Text = $"IGP_Soft — {monitor.ShortName}";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            ControlBox = false;
            BackColor = Color.White;
            TopMost = true;
            Place(monitor);
        }

        public void Place(MonitorInfo monitor)
        {
            Bounds = new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height);
            if (IsHandleCreated)
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
                    monitor.X, monitor.Y, monitor.Width, monitor.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000 | 0x04000000;
                return cp;
            }
        }
    }

    internal sealed class Segment
    {
        public int Index;
        public Rectangle Area;
        public RectangleF TileRect;
        public LinkDef? Link;
        public LaunchedWindow? Launched;
        public object? Pending;
    }

    private sealed class MonitorTile : Panel
    {
        public static readonly int[] Steps = { 1, 2, 3, 4, 6, 8, 9 };

        private const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

        public int Number { get; }
        public MonitorInfo Monitor { get; }
        public bool Excluded { get; set; }
        public Bitmap? Preview { get; set; }
        public int SegCount { get; private set; } = 1;
        public List<Segment> Segments { get; } = new();

        public event Action<MonitorTile, Segment>? SegmentActivated;
        public event Action<MonitorTile>? MonitorMenuRequested;
        public event Action<MonitorTile, int>? SegStep;

        private bool _hover;
        private Rectangle _ctrlRect;

        public MonitorTile(int number, MonitorInfo monitor)
        {
            Number = number;
            Monitor = monitor;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            BuildSegments(1);
        }

        public static (int cols, int rows) Grid(int n) => n switch
        {
            2 => (2, 1),
            3 => (3, 1),
            4 => (2, 2),
            6 => (3, 2),
            8 => (4, 2),
            9 => (3, 3),
            _ => (1, 1),
        };

        public void BuildSegments(int count)
        {
            var old = Segments.ToList();
            Segments.Clear();
            SegCount = count;
            (int cols, int rows) = Grid(count);

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c;
                int sw = Monitor.Width / cols, sh = Monitor.Height / rows;
                int x = Monitor.X + c * sw, y = Monitor.Y + r * sh;
                int w = c == cols - 1 ? Monitor.Width - c * sw : sw;
                int h = r == rows - 1 ? Monitor.Height - r * sh : sh;

                var seg = new Segment { Index = i, Area = new Rectangle(x, y, w, h) };
                if (i < old.Count) { seg.Link = old[i].Link; seg.Launched = old[i].Launched; seg.Pending = old[i].Pending; }
                Segments.Add(seg);
            }
            LayoutSegments();
            Invalidate();
        }

        private void LayoutSegments()
        {
            (int cols, int rows) = Grid(SegCount);
            float sw = (float)Width / cols, sh = (float)Height / rows;
            foreach (Segment s in Segments)
            {
                int c = s.Index % cols, r = s.Index / cols;
                s.TileRect = new RectangleF(c * sw, r * sh, sw, sh);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutSegments();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Preview?.Dispose(); Preview = null; }
            base.Dispose(disposing);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (!Excluded && _ctrlRect.Contains(e.Location))
            {
                int third = _ctrlRect.Width / 3;
                if (e.X < _ctrlRect.X + third) SegStep?.Invoke(this, -1);
                else if (e.X >= _ctrlRect.X + 2 * third) SegStep?.Invoke(this, +1);
                return;
            }

            if (Excluded) { MonitorMenuRequested?.Invoke(this); return; }

            Segment? seg = Segments.FirstOrDefault(s => s.TileRect.Contains(e.Location));
            if (seg is not null) SegmentActivated?.Invoke(this, seg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            bool preview = Preview is not null && !Excluded;
            var full = new Rectangle(0, 0, Width - 1, Height - 1);

            if (preview)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(Preview!, new Rectangle(0, 0, Width, Height));
            }
            else
            {
                using var bg = new SolidBrush(Excluded ? Color.FromArgb(224, 224, 224) : Color.White);
                g.FillRectangle(bg, full);
            }

            if (!Excluded && SegCount > 1)
            {
                (int cols, int rows) = Grid(SegCount);
                float sw = (float)Width / cols, sh = (float)Height / rows;
                using var dark = new Pen(Color.FromArgb(110, 0, 0, 0), 1f);
                using var light = new Pen(Color.FromArgb(preview ? 210 : 150, 255, 255, 255), 1f);
                for (int c = 1; c < cols; c++) { g.DrawLine(dark, c * sw, 0, c * sw, Height); g.DrawLine(light, c * sw + 1, 0, c * sw + 1, Height); }
                for (int r = 1; r < rows; r++) { g.DrawLine(dark, 0, r * sh, Width, r * sh); g.DrawLine(light, 0, r * sh + 1, Width, r * sh + 1); }
            }

            if (!Excluded)
            {
                using var sf = new Font("Segoe UI", 7.5f);
                foreach (Segment s in Segments)
                {
                    Rectangle tr = Rectangle.Round(s.TileRect);
                    var band = new Rectangle(tr.X, tr.Bottom - 16, Math.Max(1, tr.Width), 16);
                    using (var bb = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(bb, band);
                    string txt = s.Pending is not null && s.Link is not null ? "⏳ abriendo " + s.Link.Titulo
                               : s.Link is not null ? "✔ " + s.Link.Titulo
                               : "clic para asignar";
                    TextRenderer.DrawText(g, txt, sf, band,
                        s.Pending is not null ? Color.Khaki
                        : s.Link is not null ? Color.FromArgb(150, 255, 175) : Color.White,
                        C | TextFormatFlags.EndEllipsis);
                }
            }

            Color border = Excluded ? Color.FromArgb(170, 170, 170)
                         : Segments.Any(s => s.Link is not null) ? Color.SeaGreen
                         : _hover ? Color.FromArgb(0, 120, 215)
                         : Color.FromArgb(150, 150, 150);
            using (var pen = new Pen(border, !Excluded && _hover ? 3f : 1.6f))
            {
                if (Excluded) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawRectangle(pen, full);
            }

            var pill = new Rectangle(5, 5, 30, 20);
            using (var pb = new SolidBrush(Color.FromArgb(preview ? 160 : 95, 0, 0, 0)))
                g.FillRectangle(pb, pill);
            using (var nf = new Font("Segoe UI", 10f, FontStyle.Bold))
                TextRenderer.DrawText(g, Monitor.IsPrimary ? Number + "★" : Number.ToString(), nf, pill, Color.White, C);

            if (!Excluded)
            {
                const int w = 66, h = 20;
                _ctrlRect = new Rectangle(Width - w - 5, 5, w, h);
                using (var cb = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                    g.FillRectangle(cb, _ctrlRect);
                int third = w / 3;
                using var cf = new Font("Segoe UI", 10f, FontStyle.Bold);
                TextRenderer.DrawText(g, "−", cf, new Rectangle(_ctrlRect.X, _ctrlRect.Y, third, h), Color.White, C);
                TextRenderer.DrawText(g, SegCount.ToString(), cf, new Rectangle(_ctrlRect.X + third, _ctrlRect.Y, third, h), Color.Gainsboro, C);
                TextRenderer.DrawText(g, "+", cf, new Rectangle(_ctrlRect.X + 2 * third, _ctrlRect.Y, third, h), Color.White, C);
            }
            else
            {
                _ctrlRect = Rectangle.Empty;
                using var f = new Font("Segoe UI", 10f, FontStyle.Bold);
                TextRenderer.DrawText(g, "OMITIDA", f, full, Color.Gray, C);
            }
        }
    }
}
