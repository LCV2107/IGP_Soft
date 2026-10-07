namespace IGP_Soft;

internal sealed class LinksPage : Page
{
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private bool _loading;

    public event Action? Changed;

    public LinksPage() : base("Enlaces", "Lo que puede mostrarse en cada zona. Los cambios se guardan solos.")
    {
        _status.AutoSize = true;
        _status.Margin = new Padding(8, 10, 0, 0);
        _status.ForeColor = Theme.Success;
        _status.Font = Theme.BodyBold;
        ModernButton delete = ToolButton("✕  Eliminar", DeleteRow);
        delete.Kind = ButtonKind.Danger;
        FlowLayoutPanel bar = ToolBar(
            ToolButton("＋  Imagen o video de mi PC", AddFile, primary: true),
            ToolButton("↑  Subir", () => MoveRow(-1)),
            ToolButton("↓  Bajar", () => MoveRow(1)),
            delete,
            _status);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        Theme.StyleGrid(_grid);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "TÍTULO", Width = 280, MaxInputLength = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "DIRECCIÓN WEB O ARCHIVO  ·  escribe en la última fila para agregar uno nuevo",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MaxInputLength = 2000,
        });
        _grid.CellEndEdit += (_, _) => Save();
        _grid.UserDeletedRow += (_, _) => Save();
        _grid.DataError += (_, e) => e.ThrowException = false;

        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 34, TextAlign = ContentAlignment.BottomLeft, ForeColor = Theme.TextMuted,
            Text = "ⓘ  Las imágenes y videos de tu PC se copian a la carpeta Assets. El orden de la lista es el que usa «Abrir en orden».",
        };

        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 12) };
        card.Controls.Add(_grid);
        Body.Controls.Add(card);
        Body.Controls.Add(hint);
        Body.Controls.Add(bar);
    }

    public override void OnPageShown() => Reload();

    private void Reload()
    {
        _loading = true;
        _grid.Rows.Clear();
        foreach (LinkDef l in LinkStore.Links) _grid.Rows.Add(l.Titulo, l.Url);
        _loading = false;
        _status.Text = "";
    }

    private void Save()
    {
        if (_loading) return;
        var rows = new List<(DataGridViewRow Row, LinkDef Link)>();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            string t = Convert.ToString(r.Cells[0].Value)?.Trim() ?? "";
            string u = Convert.ToString(r.Cells[1].Value)?.Trim() ?? "";
            if (u.Length > 0) rows.Add((r, new LinkDef(t, u)));
        }

        if (rows.Count == 0)
        {
            _status.ForeColor = Theme.Warning;
            _status.Text = "Debe quedar al menos un enlace con dirección.";
            return;
        }

        if (rows.Any(x => AssetStore.IsExternalFile(x.Link.Url)))
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var map = AssetStore.ImportAll(rows.Select(x => x.Link.Url));
                rows = rows.Select(x => map.TryGetValue(x.Link.Url, out string? to) ? (x.Row, x.Link with { Url = to }) : x).ToList();
            }
            finally { Cursor = Cursors.Default; }
        }

        LinkStore.Replace(rows.Select(x => x.Link));

        _loading = true;
        for (int i = 0; i < rows.Count && i < LinkStore.Links.Count; i++)
        {
            LinkDef saved = LinkStore.Links[i];
            DataGridViewRow r = rows[i].Row;
            if (!Equals(r.Cells[0].Value as string, saved.Titulo)) r.Cells[0].Value = saved.Titulo;
            if (!Equals(r.Cells[1].Value as string, saved.Url)) r.Cells[1].Value = saved.Url;
        }
        _loading = false;

        _status.ForeColor = Theme.Success;
        _status.Text = "✓ Guardado";
        Changed?.Invoke();
    }

    private void AddFile()
    {
        using var d = new OpenFileDialog
        {
            Title = "Elegir imágenes o videos",
            Multiselect = true,
            Filter = "Imágenes y videos|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp;*.svg;*.avif;*.jfif;" +
                     "*.mp4;*.webm;*.ogv;*.ogg;*.mov;*.m4v;*.mkv|Todos los archivos|*.*",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        _loading = true;
        foreach (string file in d.FileNames)
            _grid.Rows.Add(Path.GetFileNameWithoutExtension(file), file);
        _loading = false;
        Save();
    }

    private void DeleteRow()
    {
        if (_grid.CurrentRow is not { IsNewRow: false } r) return;
        _grid.Rows.Remove(r);
        Save();
    }

    private void MoveRow(int dir)
    {
        int i = _grid.CurrentCell?.RowIndex ?? -1;
        int j = i + dir;
        if (i < 0 || j < 0 || j >= _grid.Rows.Count) return;
        if (_grid.Rows[i].IsNewRow || _grid.Rows[j].IsNewRow) return;

        _loading = true;
        (object? t, object? u) = (_grid.Rows[i].Cells[0].Value, _grid.Rows[i].Cells[1].Value);
        _grid.Rows[i].Cells[0].Value = _grid.Rows[j].Cells[0].Value;
        _grid.Rows[i].Cells[1].Value = _grid.Rows[j].Cells[1].Value;
        _grid.Rows[j].Cells[0].Value = t;
        _grid.Rows[j].Cells[1].Value = u;
        _grid.CurrentCell = _grid.Rows[j].Cells[0];
        _loading = false;
        Save();
    }
}
