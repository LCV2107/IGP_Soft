using System.Diagnostics;

namespace IGP_Soft;

internal sealed record GalleryTarget(string Label, string? Current, Action<LinkDef> Open);

internal sealed class GalleryPage : Page
{
    private static readonly Size ThumbSize = new(160, 110);
    private const string FormatoArrastre = "IGP_Soft.Galeria";

    private sealed record CarpetaItem(string Nombre, string? Clave, int Cantidad)
    {
        public const string Todos = "\u0001todos";
        public const string SinCarpeta = "\u0001sin";
        public const string Recibidos = "\u0001recibidos";
        public bool Propia => Clave is not (Todos or SinCarpeta or Recibidos);
    }

    private readonly Func<IReadOnlyList<GalleryTarget>> _targets;
    private readonly Action<IReadOnlyList<LinkDef>> _organizar;
    private readonly ListView _list = new();
    private readonly ListBox _carpetas = new();
    private readonly ImageList _images = new() { ImageSize = ThumbSize, ColorDepth = ColorDepth.Depth32Bit };
    private readonly ComboBox _filter = new();
    private readonly RoundedTextBox _searchBox = new("Buscar por nombre…", 240);
    private TextBox _search => _searchBox.Box;
    private readonly Label _count = new();
    private readonly PictureBox _preview = new();
    private readonly Label _name = new();
    private readonly Label _details = new();
    private readonly ModernButton _btnOrganizar = new("Organizar en un monitor…", ButtonKind.Primary, "▦");
    private readonly ModernButton _btnOpenIn = new("Mostrar en una zona", ButtonKind.Secondary, "▶");
    private readonly ModernButton _btnMover = new("Mover a carpeta", ButtonKind.Secondary, "▤");
    private readonly ModernButton _btnAddToList = new("Agregar a la lista de enlaces", ButtonKind.Secondary, "☰");
    private readonly ModernButton _btnPlay = new("Abrir con el visor de Windows", ButtonKind.Secondary, "⤢");
    private readonly ModernButton _btnDelete = new("Eliminar de Assets", ButtonKind.Danger, "✕");
    private readonly Dictionary<string, int> _thumbIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _thumbDone = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _thumbsCts;
    private string _carpeta = CarpetaItem.Todos;
    private int _soltarEn = -1;
    private bool _recargando;

    public GalleryPage(Func<IReadOnlyList<GalleryTarget>> targets, Action<IReadOnlyList<LinkDef>> organizar)
        : base("Galería", "Imágenes, videos y documentos guardados en la carpeta Assets. Arrástralos aquí para agregarlos.")
    {
        _targets = targets;
        _organizar = organizar;
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
        _filter.Items.AddRange(new object[] { "Todo", "Imágenes", "Videos", "Documentos y otros" });
        _filter.SelectedIndex = 0;
        _filter.Width = 150;
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
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Surface;
        _list.Font = Theme.Small;
        _list.SelectedIndexChanged += (_, _) => { if (!_recargando) ShowSelected(); };
        _list.DoubleClick += (_, _) => Play();
        _list.ItemDrag += (_, _) =>
        {
            List<string> sel = SelectedPaths;
            if (sel.Count > 0) _list.DoDragDrop(new DataObject(FormatoArrastre, sel.ToArray()), DragDropEffects.Move);
        };
        _list.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right && SelectedPaths.Count > 0) ShowItemMenu(e.Location); };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) DeleteSelected();
            else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem i in _list.Items) i.Selected = true; }
        };

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
            _carpeta = c.Clave!;
            Reload();
        };
        _carpetas.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int i = _carpetas.IndexFromPoint(e.Location);
            if (i >= 0) _carpetas.SelectedIndex = i;
            ShowFolderMenu(e.Location);
        };
        _carpetas.DragOver += (_, e) =>
        {
            int i = _carpetas.IndexFromPoint(_carpetas.PointToClient(new Point(e.X, e.Y)));
            bool ok = e.Data?.GetDataPresent(FormatoArrastre) == true && i >= 0 && _carpetas.Items[i] is CarpetaItem c && (c.Propia || c.Clave == CarpetaItem.SinCarpeta);
            e.Effect = ok ? DragDropEffects.Move : DragDropEffects.None;
            int nuevo = ok ? i : -1;
            if (nuevo != _soltarEn) { _soltarEn = nuevo; _carpetas.Invalidate(); }
        };
        _carpetas.DragLeave += (_, _) => { _soltarEn = -1; _carpetas.Invalidate(); };
        _carpetas.DragDrop += (_, e) =>
        {
            int i = _soltarEn;
            _soltarEn = -1;
            if (i < 0 || e.Data?.GetData(FormatoArrastre) is not string[] paths || _carpetas.Items[i] is not CarpetaItem c) return;
            MoveTo(paths, c.Propia ? c.Clave : null);
        };
        ModernButton nueva = new("Nueva carpeta", ButtonKind.Secondary, "＋") { Dock = DockStyle.Bottom, Height = 36 };
        nueva.Click += (_, _) => NewFolder(null);
        folders.Controls.Add(_carpetas);
        folders.Controls.Add(nueva);
        folders.Controls.Add(foldersTitle);

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

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 6, 0, 0), BackColor = Theme.Surface, AutoScroll = true };
        ModernButton[] botones = { _btnOrganizar, _btnOpenIn, _btnMover, _btnAddToList, _btnPlay, _btnDelete };
        foreach (ModernButton b in botones)
        {
            b.Size = new Size(304, 38);
            b.Margin = new Padding(0, 0, 0, 6);
            b.TextAlign = ContentAlignment.MiddleLeft;
        }
        _btnOrganizar.Click += (_, _) => Organize();
        _btnOpenIn.Click += (_, _) => ShowTargets();
        _btnMover.Click += (_, _) => ShowMoveMenu(_btnMover, new Point(0, _btnMover.Height));
        _btnAddToList.Click += (_, _) => AddToList();
        _btnPlay.Click += (_, _) => Play();
        _btnDelete.Click += (_, _) => DeleteSelected();
        actions.Controls.AddRange(botones);

        right.Controls.Add(actions);
        right.Controls.Add(_details);
        right.Controls.Add(_name);
        right.Controls.Add(_preview);

        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 32, TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Theme.TextMuted, Text = "ⓘ  Ctrl o Mayús para elegir varios. Arrástralos sobre una carpeta para moverlos. Doble clic lo abre con el visor de Windows.",
        };

        var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12) };
        listCard.Controls.Add(_list);
        Body.Controls.Add(listCard);
        Body.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 14, BackColor = Theme.Background });
        Body.Controls.Add(folders);
        Body.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Background });
        Body.Controls.Add(right);
        Body.Controls.Add(hint);
        Body.Controls.Add(top);

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) await Import(files);
        };
        AssetStore.Moved += GaleriaCarpetas.Renombrados;
        Disposed += (_, _) =>
        {
            AssetStore.Moved -= GaleriaCarpetas.Renombrados;
            _thumbsCts?.Cancel();
            _preview.Image?.Dispose();
            _images.Dispose();
        };
    }

    public override void OnPageShown() => Reload();

    private List<string> SelectedPaths => _list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToList();

    private string? SelectedPath => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as string : null;

    private static string Relative(string full) => Path.Combine(AssetStore.FolderName, Path.GetRelativePath(AssetStore.Root, full));

    private static bool EsRecibido(string full) =>
        Relative(full).StartsWith(Path.Combine(AssetStore.FolderName, RecepcionArchivos.Carpeta) + "\\", StringComparison.OrdinalIgnoreCase);

    private bool EnCarpeta(string full) => _carpeta switch
    {
        CarpetaItem.Todos => true,
        CarpetaItem.Recibidos => EsRecibido(full),
        CarpetaItem.SinCarpeta => GaleriaCarpetas.CarpetaDe(Relative(full)) is null && !EsRecibido(full),
        _ => string.Equals(GaleriaCarpetas.CarpetaDe(Relative(full)), _carpeta, StringComparison.CurrentCultureIgnoreCase),
    };

    private void Reload(IReadOnlyCollection<string>? select = null)
    {
        select ??= SelectedPaths;
        _thumbsCts?.Cancel();

        List<string> all = Directory.Exists(AssetStore.Root)
            ? Directory.EnumerateFiles(AssetStore.Root, "*", SearchOption.AllDirectories).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList()
            : new List<string>();
        GaleriaCarpetas.Limpiar(all.Select(Relative));
        LoadFolders(all);

        string q = _search.Text.Trim();
        List<string> files = all.Where(EnCarpeta)
                     .Where(f => q.Length == 0 || Path.GetFileName(f).Contains(q, StringComparison.CurrentCultureIgnoreCase))
                     .Where(f => _filter.SelectedIndex switch
                     {
                         1 => Thumbnails.KindOf(f) == MediaKind.Image,
                         2 => Thumbnails.KindOf(f) == MediaKind.Video,
                         3 => Thumbnails.KindOf(f) == MediaKind.Other,
                         _ => true,
                     }).ToList();

        var wanted = new HashSet<string>(select, StringComparer.OrdinalIgnoreCase);
        _recargando = true;
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
            if (wanted.Contains(f)) item.Selected = true;
        }
        _list.EndUpdate();
        _recargando = false;

        _count.Text = all.Count == 0 ? "La galería está vacía" : files.Count == all.Count ? $"{all.Count} elemento(s)" : $"{files.Count} de {all.Count}";
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
        ShowSelected();
        LoadThumbnails(files.Where(f => !_thumbDone.Contains(f)).ToList());
    }

    private void LoadFolders(List<string> all)
    {
        var items = new List<CarpetaItem>
        {
            new("Todos", CarpetaItem.Todos, all.Count),
            new("Sin carpeta", CarpetaItem.SinCarpeta, all.Count(f => GaleriaCarpetas.CarpetaDe(Relative(f)) is null && !EsRecibido(f))),
        };
        items.AddRange(GaleriaCarpetas.Carpetas.Select(c => new CarpetaItem(c, c, GaleriaCarpetas.Cantidad(c))));
        int recibidos = all.Count(EsRecibido);
        if (recibidos > 0) items.Add(new CarpetaItem("Recibidos del otro equipo", CarpetaItem.Recibidos, recibidos));
        if (items.All(i => !string.Equals(i.Clave, _carpeta, StringComparison.CurrentCultureIgnoreCase))) _carpeta = CarpetaItem.Todos;

        _recargando = true;
        _carpetas.BeginUpdate();
        _carpetas.Items.Clear();
        foreach (CarpetaItem i in items) _carpetas.Items.Add(i);
        _carpetas.SelectedIndex = items.FindIndex(i => string.Equals(i.Clave, _carpeta, StringComparison.CurrentCultureIgnoreCase));
        _carpetas.EndUpdate();
        _recargando = false;
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
        string glyph = c.Clave switch { CarpetaItem.Todos => "▦", CarpetaItem.SinCarpeta => "▢", CarpetaItem.Recibidos => "⇩", _ => "▤" };
        Color fore = sel && e.Index != _soltarEn ? Color.White : Theme.Text;
        TextRenderer.DrawText(g, $"{glyph}  {c.Nombre}", c.Propia ? Theme.Body : Theme.BodyBold, new Rectangle(r.X + 8, r.Y, r.Width - 48, r.Height), fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, c.Cantidad.ToString(), Theme.Small, new Rectangle(r.Right - 40, r.Y, 32, r.Height),
            sel && e.Index != _soltarEn ? Color.White : Theme.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private void ShowFolderMenu(Point at)
    {
        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripMenuItem("＋  Nueva carpeta…", null, (_, _) => NewFolder(null)));
        if (_carpetas.SelectedItem is CarpetaItem { Propia: true } c)
        {
            menu.Items.Add(new ToolStripMenuItem("✎  Renombrar…", null, (_, _) => RenameFolder(c.Clave!)));
            menu.Items.Add(new ToolStripMenuItem("✕  Eliminar carpeta", null, (_, _) => DeleteFolder(c.Clave!)));
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_carpetas, at);
    }

    private string? AskName(string titulo, string inicial, string? actual)
    {
        using var f = new NombreForm(titulo, inicial, n => GaleriaCarpetas.Validar(n, actual));
        return f.ShowDialog(this) == DialogResult.OK ? f.Nombre : null;
    }

    private void NewFolder(IReadOnlyList<string>? mover)
    {
        if (AskName("Nueva carpeta", "", null) is not { } nombre || !GaleriaCarpetas.Crear(nombre)) return;
        if (mover is { Count: > 0 }) GaleriaCarpetas.Mover(mover.Select(Relative), nombre);
        else _carpeta = nombre.Trim();
        Reload();
    }

    private void RenameFolder(string carpeta)
    {
        if (AskName("Renombrar carpeta", carpeta, carpeta) is not { } nombre || !GaleriaCarpetas.Renombrar(carpeta, nombre)) return;
        if (string.Equals(_carpeta, carpeta, StringComparison.CurrentCultureIgnoreCase)) _carpeta = nombre.Trim();
        Reload();
    }

    private void DeleteFolder(string carpeta)
    {
        int n = GaleriaCarpetas.Cantidad(carpeta);
        if (MessageBox.Show(this, $"¿Eliminar la carpeta «{carpeta}»?" + (n > 0 ? $"\n\nSus {n} archivo(s) no se borran: pasan a «Sin carpeta»." : ""),
                "Eliminar carpeta", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        GaleriaCarpetas.Eliminar(carpeta);
        _carpeta = CarpetaItem.Todos;
        Reload();
    }

    private void MoveTo(IReadOnlyCollection<string> paths, string? carpeta)
    {
        GaleriaCarpetas.Mover(paths.Select(Relative), carpeta);
        Reload(paths);
    }

    private void ShowMoveMenu(Control owner, Point at)
    {
        List<string> sel = SelectedPaths;
        if (sel.Count == 0) return;
        var menu = Theme.Menu();
        FillMoveItems(menu.Items, sel);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(owner, at);
    }

    private void FillMoveItems(ToolStripItemCollection items, List<string> sel)
    {
        string? comun = sel.Select(p => GaleriaCarpetas.CarpetaDe(Relative(p))).Distinct().Count() == 1 ? GaleriaCarpetas.CarpetaDe(Relative(sel[0])) : "\u0002";
        foreach (string c in GaleriaCarpetas.Carpetas)
        {
            string destino = c;
            items.Add(new ToolStripMenuItem("▤  " + c.Replace("&", "&&"), null, (_, _) => MoveTo(sel, destino)) { Checked = comun == c });
        }
        if (GaleriaCarpetas.Carpetas.Count > 0) items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem("▢  Sin carpeta", null, (_, _) => MoveTo(sel, null)) { Checked = comun is null });
        items.Add(new ToolStripMenuItem("＋  Nueva carpeta…", null, (_, _) => NewFolder(sel)));
    }

    private void ShowItemMenu(Point at)
    {
        List<string> sel = SelectedPaths;
        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripMenuItem("▦  Organizar en un monitor…", null, (_, _) => Organize()) { Enabled = _targets().Count > 0 });
        if (sel.Count == 1)
            menu.Items.Add(new ToolStripMenuItem("▶  Mostrar en una zona…", null, (_, _) => ShowTargets()) { Enabled = _btnOpenIn.Enabled });
        var mover = new ToolStripMenuItem("▤  Mover a");
        FillMoveItems(mover.DropDownItems, sel);
        menu.Items.Add(mover);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("✕  Eliminar de Assets", null, (_, _) => DeleteSelected()));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_list, at);
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

    private static bool Mostrable(string path) => Documentos.Nombre(path) is not null || Thumbnails.KindOf(path) != MediaKind.Other;

    private void ShowSelected()
    {
        List<string> sel = SelectedPaths;
        string? path = SelectedPath;
        Image? old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        bool hayZonas = _targets().Count > 0;
        _btnOrganizar.Enabled = sel.Count > 0 && hayZonas;
        _btnMover.Enabled = _btnDelete.Enabled = sel.Count > 0;
        _btnOpenIn.Enabled = path is not null && hayZonas && Mostrable(path);
        _btnPlay.Enabled = path is not null;

        if (sel.Count == 0)
        {
            _name.Text = "";
            _details.Text = "Agrega imágenes, videos o documentos con «➕ Agregar archivos…» o arrastrándolos aquí.";
            _btnAddToList.Enabled = false;
            return;
        }

        if (path is null)
        {
            long total = sel.Sum(p => { try { return new FileInfo(p).Length; } catch { return 0L; } });
            int fuera = sel.Count(p => !LinkStore.Links.Any(l => l.Url.Equals(Relative(p), StringComparison.OrdinalIgnoreCase)));
            _name.Text = $"{sel.Count} elementos seleccionados";
            _details.Text = $"{FormatSize(total)} en total\n" +
                            (fuera > 0 ? $"{fuera} no están en la lista de enlaces" : "Todos están en la lista de enlaces") + "\n" +
                            "Usa «Organizar en un monitor…» para repartirlos en sus zonas.";
            _btnAddToList.Enabled = fuera > 0;
            _btnAddToList.Text = fuera > 0 ? $"Agregar {fuera} a la lista de enlaces" : "Ya están en la lista de enlaces";
            _btnAddToList.Invalidate();
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
        string? carpeta = GaleriaCarpetas.CarpetaDe(Relative(path));
        _details.Text = $"{kind}{dims} · {FormatSize(info.Length)}\n" +
                        $"Agregado: {info.LastWriteTime:dd/MM/yyyy HH:mm}" + (carpeta is null ? "" : $" · Carpeta: {carpeta}") + "\n" +
                        (uses > 0 ? $"En uso en {uses} enlace(s) o perfil(es)" : "No se usa en enlaces ni perfiles") + "\n" +
                        Relative(path);
        _btnAddToList.Enabled = !inList;
        _btnAddToList.Text = inList ? "Ya está en la lista de enlaces" : "Agregar a la lista de enlaces";
        _btnAddToList.Invalidate();
    }

    private void Organize()
    {
        List<string> sel = SelectedPaths;
        if (sel.Count == 0) return;
        _organizar(sel.Select(LinkFor).ToList());
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

    private static LinkDef LinkFor(string path)
    {
        string rel = Relative(path);
        return LinkStore.Links.FirstOrDefault(l => l.Url.Equals(rel, StringComparison.OrdinalIgnoreCase))
               ?? new LinkDef(Path.GetFileNameWithoutExtension(path), rel);
    }

    private void AddToList()
    {
        List<string> sel = SelectedPaths;
        foreach (string path in sel)
            if (!LinkStore.Links.Any(l => l.Url.Equals(Relative(path), StringComparison.OrdinalIgnoreCase)))
                LinkStore.Add(new LinkDef(Path.GetFileNameWithoutExtension(path), Relative(path)));
        Reload(sel);
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
        List<string> sel = SelectedPaths;
        if (sel.Count == 0) return;
        int uses = sel.Sum(p => UsageCount(Relative(p)));
        string que = sel.Count == 1 ? $"«{Path.GetFileName(sel[0])}»" : $"estos {sel.Count} archivos";
        string msg = $"¿Eliminar {que} de la carpeta Assets?" +
                     (uses > 0 ? $"\n\nSe usa(n) en {uses} enlace(s) o perfil(es): esas zonas ya no podrán mostrarlo(s)." : "");
        if (MessageBox.Show(this, msg, "Eliminar", MessageBoxButtons.OKCancel,
                uses > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK)
            return;
        Image? old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        var errores = new List<string>();
        foreach (string path in sel)
        {
            try
            {
                File.Delete(path);
                _thumbIndex.Remove(path);
                _thumbDone.Remove(path);
            }
            catch (Exception ex) { errores.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        if (errores.Count > 0)
            MessageBox.Show(this, "No se pudo eliminar:\n" + string.Join("\n", errores), "Eliminar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Reload(Array.Empty<string>());
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

        List<string> nuevos = imported.Where(AssetStore.IsAssetPath).ToList();
        CarpetaItem? actual = _carpetas.SelectedItem as CarpetaItem;
        if (actual is { Propia: true }) GaleriaCarpetas.Mover(nuevos, actual.Clave);
        Reload(nuevos.Select(AssetStore.Resolve).ToList());
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

internal sealed class NombreForm : Form
{
    private readonly RoundedTextBox _box = new("Nombre de la carpeta", 456);

    public string Nombre => _box.Box.Text.Trim();

    public NombreForm(string titulo, string inicial, Func<string, string?> validar)
    {
        Theme.StyleWindow(this, darkCaption: false);
        Text = titulo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(504, 150);
        _box.Location = new Point(14, 20);
        _box.Box.Text = inicial;
        _box.Box.MaxLength = GaleriaCarpetas.MaxNombre;
        var ok = new ModernButton("Aceptar", ButtonKind.Primary) { DialogResult = DialogResult.OK, Location = new Point(282, 98), Size = new Size(110, 38) };
        var cancel = new ModernButton("Cancelar") { DialogResult = DialogResult.Cancel, Location = new Point(400, 98), Size = new Size(92, 38) };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { _box, ok, cancel });
        Shown += (_, _) => { _box.Box.Focus(); _box.Box.SelectAll(); };
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK || validar(Nombre) is not { } error) return;
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        };
    }
}
