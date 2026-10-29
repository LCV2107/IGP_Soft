using System.Diagnostics;

namespace IGP_Soft;

internal sealed record GalleryTarget(string Label, string? Current, Action<LinkDef> Open);

internal sealed class GalleryPage : Page
{
    private static readonly Size ThumbSize = new(160, 110);

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
    private readonly ModernButton _btnPlay = new("Abrir con el visor de Windows", ButtonKind.Secondary, "⤢");
    private readonly ModernButton _btnDelete = new("Eliminar de Assets", ButtonKind.Danger, "✕");
    private readonly Dictionary<string, int> _thumbIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _thumbDone = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _thumbsCts;

    public GalleryPage(Func<IReadOnlyList<GalleryTarget>> targets)
        : base("Galería", "Imágenes y videos guardados en la carpeta Assets. Arrástralos aquí para agregarlos.")
    {
        _targets = targets;
        AllowDrop = true;

        ModernButton btnImport = ToolButton("＋  Agregar archivos", () => { }, primary: true);
        btnImport.Click += async (_, _) => await ImportWithDialog();
        ModernButton btnFolder = ToolButton("▤  Abrir carpeta", () =>
        {
            Directory.CreateDirectory(AssetStore.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AssetStore.Root}\"") { UseShellExecute = true });
        });
        btnFolder.Margin = new Padding(0, 0, 18, 0);
        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.FlatStyle = FlatStyle.Flat;
        _filter.Font = Theme.Body;
        _filter.Items.AddRange(new object[] { "Todo", "Imágenes", "Videos", "Otros" });
        _filter.SelectedIndex = 0;
        _filter.Width = 120;
        _filter.Margin = new Padding(0, 7, 8, 0);
        _filter.SelectedIndexChanged += (_, _) => Reload();
        _searchBox.Margin = new Padding(0, 0, 12, 0);
        _search.TextChanged += (_, _) => Reload();
        _count.AutoSize = true;
        _count.ForeColor = Theme.TextMuted;
        _count.Margin = new Padding(0, 11, 0, 0);
        FlowLayoutPanel top = ToolBar(btnImport, btnFolder, _filter, _searchBox, _count);

        _list.Dock = DockStyle.Fill;
        _list.View = View.LargeIcon;
        _list.LargeImageList = _images;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Surface;
        _list.Font = Theme.Small;
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _list.DoubleClick += (_, _) => Play();

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
        foreach (ModernButton b in new[] { _btnOpenIn, _btnAddToList, _btnPlay, _btnDelete })
        {
            b.Size = new Size(304, 38);
            b.Margin = new Padding(0, 0, 0, 6);
            b.TextAlign = ContentAlignment.MiddleLeft;
        }
        _btnOpenIn.Click += (_, _) => ShowTargets();
        _btnAddToList.Click += (_, _) => AddToList();
        _btnPlay.Click += (_, _) => Play();
        _btnDelete.Click += (_, _) => DeleteSelected();
        actions.Controls.AddRange(new Control[] { _btnOpenIn, _btnAddToList, _btnPlay, _btnDelete });

        right.Controls.Add(actions);
        right.Controls.Add(_details);
        right.Controls.Add(_name);
        right.Controls.Add(_preview);

        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 32, TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Theme.TextMuted, Text = "ⓘ  Clic en un elemento para ver su vista previa y opciones. Doble clic lo abre con el visor de Windows.",
        };

        var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12) };
        listCard.Controls.Add(_list);
        Body.Controls.Add(listCard);
        Body.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Background });
        Body.Controls.Add(right);
        Body.Controls.Add(hint);
        Body.Controls.Add(top);

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) await Import(files);
        };
        Disposed += (_, _) =>
        {
            _thumbsCts?.Cancel();
            _preview.Image?.Dispose();
            _images.Dispose();
        };
    }

    public override void OnPageShown() => Reload();

    private string? SelectedPath => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as string : null;

    private static string Relative(string full) => Path.Combine(AssetStore.FolderName, Path.GetRelativePath(AssetStore.Root, full));

    private void Reload(string? select = null)
    {
        select ??= SelectedPath;
        _thumbsCts?.Cancel();

        List<string> files = Directory.Exists(AssetStore.Root)
            ? Directory.EnumerateFiles(AssetStore.Root, "*", SearchOption.AllDirectories).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList()
            : new List<string>();
        int total = files.Count;

        string q = _search.Text.Trim();
        files = files.Where(f => q.Length == 0 || Path.GetFileName(f).Contains(q, StringComparison.CurrentCultureIgnoreCase))
                     .Where(f => _filter.SelectedIndex switch
                     {
                         1 => Thumbnails.KindOf(f) == MediaKind.Image,
                         2 => Thumbnails.KindOf(f) == MediaKind.Video,
                         3 => Thumbnails.KindOf(f) == MediaKind.Other,
                         _ => true,
                     }).ToList();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (string f in files)
        {
            if (!_thumbIndex.ContainsKey(f))
            {
                _images.Images.Add(Thumbnails.Compose(null, f, ThumbSize));
                _thumbIndex[f] = _images.Images.Count - 1;
            }
            var item = new ListViewItem(Path.GetFileName(f), _thumbIndex[f]) { Tag = f };
            if (UsageCount(Relative(f)) > 0) item.ForeColor = Theme.Success;
            _list.Items.Add(item);
            if (string.Equals(f, select, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
        }
        _list.EndUpdate();

        _count.Text = total == 0 ? "La galería está vacía" : files.Count == total ? $"{total} elemento(s)" : $"{files.Count} de {total}";
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
        ShowSelected();
        LoadThumbnails(files.Where(f => !_thumbDone.Contains(f)).ToList());
    }

    private void LoadThumbnails(List<string> files)
    {
        if (files.Count == 0) return;
        var cts = new CancellationTokenSource();
        _thumbsCts = cts;
        var thread = new Thread(() =>
        {
            foreach (string f in files)
            {
                if (cts.IsCancellationRequested) return;
                Bitmap thumb;
                using (Image? src = Thumbnails.Best(f, 256)) thumb = Thumbnails.Compose(src, f, ThumbSize);
                try
                {
                    BeginInvoke(() =>
                    {
                        if (IsDisposed || cts.IsCancellationRequested || !_thumbIndex.TryGetValue(f, out int idx)) { thumb.Dispose(); return; }
                        _images.Images[idx] = thumb;
                        _thumbDone.Add(f);
                        _list.Invalidate();
                    });
                }
                catch { thumb.Dispose(); return; }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void ShowSelected()
    {
        string? path = SelectedPath;
        Image? old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        foreach (Button b in new[] { _btnOpenIn, _btnAddToList, _btnPlay, _btnDelete }) b.Enabled = path is not null;

        if (path is null)
        {
            _name.Text = "";
            _details.Text = "Agrega imágenes, videos o documentos con «➕ Agregar archivos…» o arrastrándolos aquí.";
            return;
        }

        _name.Text = Path.GetFileName(path);
        _preview.Image = Thumbnails.Best(path, 512);

        var info = new FileInfo(path);
        string kind = Documentos.Nombre(path) ?? Thumbnails.KindOf(path) switch
        {
            MediaKind.Image => "Imagen",
            MediaKind.Video => "Video",
            _ => "Archivo",
        };
        string dims = _preview.Image is not null && Thumbnails.CanLoadDirectly(path) ? $" · {_preview.Image.Width} × {_preview.Image.Height}" : "";
        int uses = UsageCount(Relative(path));
        bool inList = LinkStore.Links.Any(l => l.Url.Equals(Relative(path), StringComparison.OrdinalIgnoreCase));
        _details.Text = $"{kind}{dims} · {FormatSize(info.Length)}\n" +
                        $"Agregado: {info.LastWriteTime:dd/MM/yyyy HH:mm}\n" +
                        (uses > 0 ? $"En uso en {uses} enlace(s) o perfil(es)" : "No se usa en enlaces ni perfiles") + "\n" +
                        Relative(path);
        _btnAddToList.Enabled = !inList;
        _btnAddToList.Text = inList ? "Ya está en la lista de enlaces" : "Agregar a la lista de enlaces";
        _btnAddToList.Invalidate();
        _btnOpenIn.Enabled = _targets().Count > 0 && Thumbnails.KindOf(path) != MediaKind.Other;
    }

    private void ShowTargets()
    {
        if (SelectedPath is not string path) return;
        var menu = Theme.Menu();
        LinkDef link = LinkFor(path);
        foreach (GalleryTarget t in _targets())
        {
            string text = t.Current is null ? t.Label : $"{t.Label}   (ahora: {t.Current})";
            GalleryTarget captured = t;
            menu.Items.Add(text.Replace("&", "&&"), null, (_, _) => captured.Open(link));
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_btnOpenIn, new Point(0, _btnOpenIn.Height));
    }

    private LinkDef LinkFor(string path)
    {
        string rel = Relative(path);
        return LinkStore.Links.FirstOrDefault(l => l.Url.Equals(rel, StringComparison.OrdinalIgnoreCase))
               ?? new LinkDef(Path.GetFileNameWithoutExtension(path), rel);
    }

    private void AddToList()
    {
        if (SelectedPath is not string path) return;
        LinkStore.Add(new LinkDef(Path.GetFileNameWithoutExtension(path), Relative(path)));
        Reload(path);
    }

    private void Play()
    {
        if (SelectedPath is not string path) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo abrir el archivo:\n{ex.Message}", "Galería", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelected()
    {
        if (SelectedPath is not string path) return;
        int uses = UsageCount(Relative(path));
        string msg = $"¿Eliminar «{Path.GetFileName(path)}» de la carpeta Assets?" +
                     (uses > 0 ? $"\n\nSe usa en {uses} enlace(s) o perfil(es): esas zonas ya no podrán mostrarlo." : "");
        if (MessageBox.Show(this, msg, "Eliminar", MessageBoxButtons.OKCancel,
                uses > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK)
            return;
        try
        {
            Image? old = _preview.Image;
            _preview.Image = null;
            old?.Dispose();
            File.Delete(path);
            _thumbIndex.Remove(path);
            _thumbDone.Remove(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo eliminar:\n{ex.Message}", "Eliminar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Reload();
    }

    private async Task ImportWithDialog()
    {
        using var d = new OpenFileDialog
        {
            Title = "Agregar a la galería",
            Multiselect = true,
            Filter = TiposPermitidos.Filtro + "|Todos los archivos|*.*",
        };
        if (d.ShowDialog(this) == DialogResult.OK) await Import(d.FileNames);
    }

    private async Task Import(IEnumerable<string> files)
    {
        List<string> list = files.Where(File.Exists).ToList();
        if (list.Count == 0) return;
        UseWaitCursor = true;
        List<string> imported;
        try { imported = await Task.Run(() => list.Select(AssetStore.Import).ToList()); }
        finally { UseWaitCursor = false; }

        string? last = imported.LastOrDefault(AssetStore.IsAssetPath);
        Reload(last is null ? null : AssetStore.Resolve(last));
    }

    private static int UsageCount(string rel) =>
        LinkStore.Links.Count(l => l.Url.Equals(rel, StringComparison.OrdinalIgnoreCase)) +
        ProfileStore.Items.Count(p => p.Monitors.Any(m => m.Links.Any(z => z.Url.Equals(rel, StringComparison.OrdinalIgnoreCase))));

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}
