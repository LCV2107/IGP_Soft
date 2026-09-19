namespace IGP_Soft;

/// <summary>Editor de la lista de enlaces (se guarda junto al ejecutable).</summary>
internal sealed class LinksForm : Form
{
    private readonly DataGridView _grid = new();

    public LinksForm()
    {
        Text = "Enlaces de monitoreo";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 460);
        MinimumSize = new Size(560, 320);
        Font = new Font("Segoe UI", 9f);
        ShowInTaskbar = false;
        TopMost = true;   // el panel es topmost; sin esto el diálogo queda detrás

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(10, 6, 10, 6),
            ForeColor = Color.DimGray,
            Text = "Agrega los enlaces que quieras (una fila por enlace). Luego, en el panel, " +
                   "cada monitor puede abrir cualquiera de ellos.\nArchivo: " + LinkStore.FilePath,
        };

        var colT = new DataGridViewTextBoxColumn
        {
            HeaderText = "Título", Width = 220, MaxInputLength = 120,
        };
        var colU = new DataGridViewTextBoxColumn
        {
            HeaderText = "URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MaxInputLength = 2000,
        };
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.Columns.AddRange(colT, colU);
        foreach (LinkDef l in LinkStore.Links) _grid.Rows.Add(l.Titulo, l.Url);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(8) };
        var up = new Button { Text = "▲ Subir", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        var down = new Button { Text = "▼ Bajar", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        var browse = new Button { Text = "Archivo…", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        var del = new Button { Text = "Eliminar fila", AutoSize = true, Margin = new Padding(0, 0, 24, 0) };
        var ok = new Button { Text = "Guardar", AutoSize = true, DialogResult = DialogResult.OK, Margin = new Padding(0, 0, 6, 0) };
        var cancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        up.Click += (_, _) => MoveRow(-1);
        down.Click += (_, _) => MoveRow(1);
        browse.Click += (_, _) => BrowseIntoRow();
        del.Click += (_, _) =>
        {
            if (_grid.CurrentRow is { IsNewRow: false } r) _grid.Rows.Remove(r);
        };
        bar.Controls.AddRange(new Control[] { up, down, browse, del, ok, cancel });

        Controls.Add(_grid);
        Controls.Add(bar);
        Controls.Add(hint);
        AcceptButton = ok;
        CancelButton = cancel;

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;

            var items = new List<LinkDef>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (r.IsNewRow) continue;
                string t = Convert.ToString(r.Cells[0].Value)?.Trim() ?? "";
                string u = Convert.ToString(r.Cells[1].Value)?.Trim() ?? "";
                if (u.Length > 0) items.Add(new LinkDef(t, u));
            }

            if (items.Count == 0)
            {
                MessageBox.Show(this, "Debe quedar al menos un enlace con URL.", "Enlaces",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }

            LinkStore.Replace(items);
        };
    }

    private void BrowseIntoRow()
    {
        using var d = new OpenFileDialog
        {
            Title = "Elegir imagen o video",
            Filter = "Imágenes y videos|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp;*.svg;*.avif;*.jfif;" +
                     "*.mp4;*.webm;*.ogv;*.ogg;*.mov;*.m4v;*.mkv|Todos los archivos|*.*",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        int i = _grid.CurrentCell?.RowIndex ?? -1;
        if (i < 0 || _grid.Rows[i].IsNewRow) i = _grid.Rows.Add();
        _grid.Rows[i].Cells[1].Value = d.FileName;
        if (string.IsNullOrWhiteSpace(Convert.ToString(_grid.Rows[i].Cells[0].Value)))
            _grid.Rows[i].Cells[0].Value = Path.GetFileNameWithoutExtension(d.FileName);
        _grid.CurrentCell = _grid.Rows[i].Cells[0];
    }

    private void MoveRow(int dir)
    {
        int i = _grid.CurrentCell?.RowIndex ?? -1;
        int j = i + dir;
        if (i < 0 || j < 0 || j >= _grid.Rows.Count) return;
        if (_grid.Rows[i].IsNewRow || _grid.Rows[j].IsNewRow) return;

        (object? t, object? u) = (_grid.Rows[i].Cells[0].Value, _grid.Rows[i].Cells[1].Value);
        _grid.Rows[i].Cells[0].Value = _grid.Rows[j].Cells[0].Value;
        _grid.Rows[i].Cells[1].Value = _grid.Rows[j].Cells[1].Value;
        _grid.Rows[j].Cells[0].Value = t;
        _grid.Rows[j].Cells[1].Value = u;
        _grid.CurrentCell = _grid.Rows[j].Cells[0];
    }
}
