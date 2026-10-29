namespace IGP_Soft;

internal sealed class EnviosRemotos
{
    private readonly IArchivosRemotos _archivos;
    private readonly List<CancellationTokenSource> _activos = new();

    public EnviosRemotos(IArchivosRemotos archivos) => _archivos = archivos;

    public IArchivosRemotos Archivos => _archivos;

    public event Action<string, double>? Progreso;
    public event Action? Terminado;

    public bool Ocupado { get { lock (_activos) return _activos.Count > 0; } }

    public async Task<(Resultado R, string? Url)> Enviar(string ruta, Action<double>? alAvanzar = null)
    {
        string nombre = Path.GetFileName(ruta);
        var cts = new CancellationTokenSource();
        lock (_activos) _activos.Add(cts);
        var progreso = new Progress<double>(f =>
        {
            alAvanzar?.Invoke(f);
            Progreso?.Invoke(nombre, f);
        });
        try
        {
            Progreso?.Invoke(nombre, 0);
            return await _archivos.Subir(ruta, progreso, cts.Token);
        }
        finally
        {
            lock (_activos) _activos.Remove(cts);
            cts.Dispose();
            Terminado?.Invoke();
        }
    }

    public void CancelarTodo()
    {
        lock (_activos)
            foreach (CancellationTokenSource c in _activos)
            {
                try { c.Cancel(); }
                catch { }
            }
    }

    public static string Porcentaje(double f) => $"{Math.Clamp((int)Math.Round(f * 100), 0, 100)} %";
}

internal sealed class GaleriaRemotaPage : Page
{
    private static readonly Size ThumbSize = new(160, 110);

    private readonly EnviosRemotos _envios;
    private readonly Func<IReadOnlyList<GalleryTarget>> _targets;
    private readonly ListView _list = new();
    private readonly ImageList _images = new() { ImageSize = ThumbSize, ColorDepth = ColorDepth.Depth32Bit };
    private readonly ComboBox _filter = new();
    private readonly RoundedTextBox _searchBox = new("Buscar por nombre…", 240);
    private TextBox _search => _searchBox.Box;
    private readonly Label _count = new();
    private readonly PictureBox _preview = new();
    private readonly Label _name = new();
    private readonly Label _details = new();
    private readonly ModernButton _btnOpenIn = new("Mostrar en una zona", ButtonKind.Primary, "▶");
    private readonly ModernButton _btnAddToList = new("Agregar a la lista de enlaces", ButtonKind.Secondary, "☰");
    private readonly ModernButton _btnDelete = new("Eliminar de la reserva", ButtonKind.Danger, "✕");
    private readonly Panel _progressBar = new();
    private readonly Label _progressText = new();
    private readonly ProgressBar _progress = new();
    private readonly Dictionary<string, int> _thumbIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _thumbDone = new(StringComparer.OrdinalIgnoreCase);
    private List<ArchivoRemoto> _files = new();
    private CancellationTokenSource? _thumbsCts;
    private int _previewSeq;

    public GaleriaRemotaPage(EnviosRemotos envios, Func<IReadOnlyList<GalleryTarget>> targets)
        : base("Galería", "Imágenes, videos y documentos del otro equipo. Arrastra archivos de este equipo aquí para enviarlos a su reserva.")
    {
        _envios = envios;
        _targets = targets;
        AllowDrop = true;

        ModernButton btnSend = ToolButton("＋  Enviar a la reserva…", () => { }, primary: true);
        btnSend.Click += async (_, _) => await SendWithDialog();
        ModernButton btnRefresh = ToolButton("⟳  Actualizar", () => _ = Reload());
        btnRefresh.Margin = new Padding(0, 0, 18, 0);
        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.FlatStyle = FlatStyle.Flat;
        _filter.Font = Theme.Body;
        _filter.Items.AddRange(new object[] { "Todo", "Recibidos de otro equipo", "Propios del otro equipo" });
        _filter.SelectedIndex = 0;
        _filter.Width = 200;
        _filter.Margin = new Padding(0, 7, 8, 0);
        _filter.SelectedIndexChanged += (_, _) => Fill();
        _searchBox.Margin = new Padding(0, 0, 12, 0);
        _search.TextChanged += (_, _) => Fill();
        _count.AutoSize = true;
        _count.ForeColor = Theme.TextMuted;
        _count.Margin = new Padding(0, 11, 0, 0);
        FlowLayoutPanel top = ToolBar(btnSend, btnRefresh, _filter, _searchBox, _count);

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Height = 44;
        _progressBar.BackColor = Theme.Background;
        _progressBar.Visible = false;
        _progressText.AutoSize = false;
        _progressText.Location = new Point(0, 4);
        _progressText.Size = new Size(520, 20);
        _progressText.ForeColor = Theme.Text;
        _progressText.Font = Theme.BodyBold;
        _progress.Location = new Point(0, 28);
        _progress.Size = new Size(520, 8);
        var cancel = new ModernButton("Cancelar", ButtonKind.Ghost, "✕") { Location = new Point(536, 2), Size = new Size(110, 34) };
        cancel.Click += (_, _) => _envios.CancelarTodo();
        _progressBar.Controls.AddRange(new Control[] { _progressText, _progress, cancel });

        _list.Dock = DockStyle.Fill;
        _list.View = View.LargeIcon;
        _list.LargeImageList = _images;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Surface;
        _list.Font = Theme.Small;
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        var right = new Card { Dock = DockStyle.Right, Width = 340, Padding = new Padding(16) };
        _preview.Dock = DockStyle.Top;
        _preview.Height = 170;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Theme.SurfaceAlt;
        _name.Dock = DockStyle.Top;
        _name.Height = 44;
        _name.Font = Theme.Section;
        _name.ForeColor = Theme.Text;
        _name.BackColor = Theme.Surface;
        _name.TextAlign = ContentAlignment.BottomLeft;
        _name.AutoEllipsis = true;
        _details.Dock = DockStyle.Top;
        _details.Height = 84;
        _details.ForeColor = Theme.TextMuted;
        _details.BackColor = Theme.Surface;
        _details.Font = Theme.Small;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 6, 0, 0), BackColor = Theme.Surface };
        foreach (ModernButton b in new[] { _btnOpenIn, _btnAddToList, _btnDelete })
        {
            b.Size = new Size(304, 38);
            b.Margin = new Padding(0, 0, 0, 6);
            b.TextAlign = ContentAlignment.MiddleLeft;
        }
        _btnOpenIn.Click += (_, _) => ShowTargets();
        _btnAddToList.Click += (_, _) => AddToList();
        _btnDelete.Click += async (_, _) => await DeleteSelected();
        actions.Controls.AddRange(new Control[] { _btnOpenIn, _btnAddToList, _btnDelete });

        right.Controls.Add(actions);
        right.Controls.Add(_details);
        right.Controls.Add(_name);
        right.Controls.Add(_preview);

        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 32, TextAlign = ContentAlignment.BottomLeft, ForeColor = Theme.TextMuted,
            Text = "ⓘ  Cada archivo se envía una sola vez: si el otro equipo ya lo tiene, se usa al instante. Los programas no se pueden enviar.",
        };

        var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12) };
        listCard.Controls.Add(_list);
        Body.Controls.Add(listCard);
        Body.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Background });
        Body.Controls.Add(right);
        Body.Controls.Add(hint);
        Body.Controls.Add(_progressBar);
        Body.Controls.Add(top);

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) await Send(files);
        };
        _envios.Progreso += OnProgress;
        _envios.Terminado += OnFinished;
        Disposed += (_, _) =>
        {
            _envios.Progreso -= OnProgress;
            _envios.Terminado -= OnFinished;
            _thumbsCts?.Cancel();
            _preview.Image?.Dispose();
            _images.Dispose();
        };
    }

    public override void OnPageShown() => _ = Reload();

    private ArchivoRemoto? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ArchivoRemoto : null;

    private void OnProgress(string nombre, double f)
    {
        if (IsDisposed) return;
        _progressBar.Visible = true;
        _progressText.Text = $"Enviando «{nombre}» al otro equipo…  {EnviosRemotos.Porcentaje(f)}";
        _progress.Value = Math.Clamp((int)Math.Round(f * 100), 0, 100);
    }

    private void OnFinished()
    {
        if (IsDisposed) return;
        void Apply()
        {
            if (!_envios.Ocupado) _progressBar.Visible = false;
            if (Visible) _ = Reload();
        }
        if (InvokeRequired) BeginInvoke(Apply);
        else Apply();
    }

    private async Task Reload(string? select = null)
    {
        select ??= Selected?.Url;
        List<ArchivoRemoto>? files = await _envios.Archivos.Archivos();
        if (IsDisposed) return;
        if (files is null)
        {
            _count.Text = "No se pudo leer la galería del otro equipo";
            return;
        }
        _files = files;
        Fill(select);
    }

    private void Fill(string? select = null)
    {
        select ??= Selected?.Url;
        _thumbsCts?.Cancel();

        string q = _search.Text.Trim();
        List<ArchivoRemoto> shown = _files
            .Where(f => q.Length == 0 || f.Nombre.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .Where(f => _filter.SelectedIndex switch
            {
                1 => f.Reserva,
                2 => !f.Reserva,
                _ => true,
            }).ToList();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (ArchivoRemoto f in shown)
        {
            if (!_thumbIndex.ContainsKey(f.Url))
            {
                _images.Images.Add(Thumbnails.Compose(null, f.Nombre, ThumbSize));
                _thumbIndex[f.Url] = _images.Images.Count - 1;
            }
            var item = new ListViewItem(f.Nombre, _thumbIndex[f.Url]) { Tag = f };
            if (f.Usos > 0) item.ForeColor = Theme.Success;
            _list.Items.Add(item);
            if (string.Equals(f.Url, select, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
        }
        _list.EndUpdate();

        _count.Text = _files.Count == 0 ? "La galería del otro equipo está vacía"
            : shown.Count == _files.Count ? $"{_files.Count} elemento(s)" : $"{shown.Count} de {_files.Count}";
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
        ShowSelected();
        _ = LoadThumbnails(shown.Where(f => !_thumbDone.Contains(f.Url)).ToList());
    }

    private async Task LoadThumbnails(List<ArchivoRemoto> files)
    {
        if (files.Count == 0) return;
        var cts = new CancellationTokenSource();
        _thumbsCts = cts;
        foreach (ArchivoRemoto f in files)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            byte[]? jpeg = await _envios.Archivos.Miniatura(f.Url, 256);
            if (cts.IsCancellationRequested || IsDisposed) return;
            using Image? src = Decode(jpeg);
            Bitmap thumb = Thumbnails.Compose(src, f.Nombre, ThumbSize);
            if (!_thumbIndex.TryGetValue(f.Url, out int idx)) { thumb.Dispose(); continue; }
            _images.Images[idx] = thumb;
            _thumbDone.Add(f.Url);
            _list.Invalidate();
        }
    }

    private static Image? Decode(byte[]? jpeg)
    {
        if (jpeg is null) return null;
        try { return Image.FromStream(new MemoryStream(jpeg)); }
        catch { return null; }
    }

    private async void ShowSelected()
    {
        ArchivoRemoto? f = Selected;
        int seq = ++_previewSeq;
        Image? old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        foreach (Button b in new[] { _btnOpenIn, _btnAddToList, _btnDelete }) b.Enabled = f is not null;

        if (f is null)
        {
            _name.Text = "";
            _details.Text = "Envía imágenes, videos, PDF, Word o PowerPoint con «＋ Enviar a la reserva…» o arrastrándolos aquí.";
            return;
        }

        _name.Text = f.Nombre;
        string kind = Documentos.Nombre(f.Nombre) ?? Thumbnails.KindOf(f.Nombre) switch
            {
                MediaKind.Image => "Imagen",
                MediaKind.Video => "Video",
                _ => "Archivo",
            };
        _details.Text = $"{kind} · {FormatSize(f.Bytes)}\n" +
                        $"Fecha: {f.Fecha:dd/MM/yyyy HH:mm}\n" +
                        (f.Usos > 0 ? $"En uso en {f.Usos} enlace(s), perfil(es) o zona(s)" : "No se usa en enlaces, perfiles ni zonas") + "\n" +
                        (f.Reserva ? "Recibido de otro equipo" : "Propio del otro equipo");
        bool inList = LinkStore.Links.Any(l => SameUrl(l.Url, f.Url));
        _btnAddToList.Enabled = !inList;
        _btnAddToList.Text = inList ? "Ya está en la lista de enlaces" : "Agregar a la lista de enlaces";
        _btnAddToList.Invalidate();
        _btnOpenIn.Enabled = _targets().Count > 0;
        _btnDelete.Enabled = f.Reserva;
        _btnDelete.Text = f.Reserva ? "Eliminar de la reserva" : "Solo se eliminan los recibidos";
        _btnDelete.Invalidate();

        Image? big = Decode(await _envios.Archivos.Miniatura(f.Url, 512));
        if (seq != _previewSeq || IsDisposed) { big?.Dispose(); return; }
        _preview.Image = big;
    }

    private static bool SameUrl(string a, string b) =>
        string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private LinkDef LinkFor(ArchivoRemoto f) =>
        LinkStore.Links.FirstOrDefault(l => SameUrl(l.Url, f.Url)) ?? new LinkDef(Path.GetFileNameWithoutExtension(f.Nombre), f.Url);

    private void ShowTargets()
    {
        if (Selected is not { } f) return;
        var menu = Theme.Menu();
        LinkDef link = LinkFor(f);
        foreach (GalleryTarget t in _targets())
        {
            string text = t.Current is null ? t.Label : $"{t.Label}   (ahora: {t.Current})";
            GalleryTarget captured = t;
            menu.Items.Add(text.Replace("&", "&&"), null, (_, _) => captured.Open(link));
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_btnOpenIn, new Point(0, _btnOpenIn.Height));
    }

    private void AddToList()
    {
        if (Selected is not { } f) return;
        LinkStore.Add(new LinkDef(Path.GetFileNameWithoutExtension(f.Nombre), f.Url));
        _btnAddToList.Enabled = false;
        _btnAddToList.Text = "Ya está en la lista de enlaces";
        _btnAddToList.Invalidate();
    }

    private async Task DeleteSelected()
    {
        if (Selected is not { Reserva: true } f) return;
        if (MessageBox.Show(this, $"¿Eliminar «{f.Nombre}» de la reserva del otro equipo?\n\nSi lo necesitas otra vez, habrá que volver a enviarlo.",
                "Eliminar", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        Resultado r = await _envios.Archivos.BorrarArchivo(f.Url);
        if (!r.Ok && r.Mensaje is not null)
            MessageBox.Show(this, r.Mensaje, "Eliminar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _thumbDone.Remove(f.Url);
        await Reload();
    }

    private async Task SendWithDialog()
    {
        using var d = new OpenFileDialog
        {
            Title = "Enviar a la reserva del otro equipo",
            Multiselect = true,
            Filter = TiposPermitidos.Filtro,
        };
        if (d.ShowDialog(this) == DialogResult.OK) await Send(d.FileNames);
    }

    private async Task Send(IEnumerable<string> files)
    {
        string? last = null;
        var errores = new List<string>();
        foreach (string file in files.Where(File.Exists))
        {
            (Resultado r, string? url) = await _envios.Enviar(file);
            if (r.Ok) last = url;
            else if (r.Codigo == "cancelado") break;
            else if (r.Mensaje is not null) errores.Add(r.Mensaje);
        }
        if (IsDisposed) return;
        await Reload(last);
        if (errores.Count > 0)
            MessageBox.Show(this, string.Join("\n\n", errores.Distinct()), "Enviar archivos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}
