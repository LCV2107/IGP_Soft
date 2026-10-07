namespace IGP_Soft;

internal sealed class ExportPage : Page
{
    private readonly Func<bool> _closeAllForRestore;
    private readonly Action _afterRestore;
    private readonly ToggleSwitch _chkSessions = new("Sesiones guardadas (cookies e inicios de sesión) — son datos sensibles");
    private readonly ToggleSwitch _chkProgram = new("El programa (IGP_Soft.exe) dentro del .zip, para usarlo directo en otra PC");
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly List<ModernButton> _actions = new();

    public ExportPage(Func<bool> closeAllForRestore, Action afterRestore)
        : base("Exportar y copias", "Lleva tus enlaces, perfiles, ajustes e imágenes a otra PC, o guárdalos por seguridad.")
    {
        _closeAllForRestore = closeAllForRestore;
        _afterRestore = afterRestore;

        var grid = new TableLayoutPanel
        {
            Location = new Point(28, 4), Size = new Size(752, 168), ColumnCount = 2, RowCount = 2, BackColor = Theme.Background,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.Controls.Add(Action("⇩", "Comprimir en .zip", "Un solo archivo para guardar o enviar.", ExportZip, primary: true), 0, 0);
        grid.Controls.Add(Action("▣", "Versión portable", "Carpeta con el programa y todo lo necesario para otra PC.", ExportPortable), 1, 0);
        grid.Controls.Add(Action("⊡", "Copia de seguridad", "Carpeta nueva con fecha y hora. Nunca sobrescribe nada.", Backup), 0, 1);
        grid.Controls.Add(Action("⟲", "Restaurar", "Recupera todo desde un .zip, una copia o una versión portable.", Restore), 1, 1);

        var options = new Card { Location = new Point(28, 188), Size = new Size(740, 128), Padding = new Padding(20, 14, 20, 12) };
        var optTitle = new Label { Text = "Incluir también", Font = Theme.Section, ForeColor = Theme.Text, Dock = DockStyle.Top, Height = 30, BackColor = Theme.Surface };
        _chkSessions.Dock = DockStyle.Top;
        _chkProgram.Dock = DockStyle.Top;
        options.Controls.Add(_chkProgram);
        options.Controls.Add(_chkSessions);
        options.Controls.Add(optTitle);

        _progress.SetBounds(30, 334, 736, 8);
        _status.SetBounds(28, 350, 740, 44);
        _status.ForeColor = Theme.TextMuted;

        Body.Controls.AddRange(new Control[] { grid, options, _progress, _status });
    }

    private ModernButton Action(string glyph, string text, string description, Func<Task> action, bool primary = false)
    {
        var b = new ModernButton(text, primary ? ButtonKind.Primary : ButtonKind.Secondary, glyph)
        {
            Description = description, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 12), Radius = 12,
        };
        b.Click += async (_, _) => await action();
        _actions.Add(b);
        return b;
    }

    private async Task ExportZip()
    {
        if (!OfferToMoveExternalFiles()) return;
        using var d = new SaveFileDialog
        {
            Title = "Guardar .zip", Filter = "Archivo .zip|*.zip", FileName = ProjectFiles.DefaultZipName(),
            InitialDirectory = LastFolder(),
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        RememberFolder(Path.GetDirectoryName(d.FileName));

        List<ProjectFile> files = ProjectFiles.Collect(_chkProgram.Checked, _chkSessions.Checked);
        TransferResult? r = await Run(files.Count, p => ProjectFiles.ZipTo(files, d.FileName, p));
        if (r is not null) Done("Se creó el .zip", r);
    }

    private async Task ExportPortable()
    {
        if (!OfferToMoveExternalFiles()) return;
        string? parent = AskFolder("Elija dónde crear la carpeta portable");
        if (parent is null) return;

        string dest = ProjectFiles.NewFolder(parent, "IGP_Soft_Portable");
        List<ProjectFile> files = ProjectFiles.Collect(includeProgram: true, _chkSessions.Checked);
        TransferResult? r = await Run(files.Count, p => ProjectFiles.CopyTo(files, dest, p));
        if (r is not null) Done("Se creó la versión portable", r);
    }

    private async Task Backup()
    {
        if (!OfferToMoveExternalFiles()) return;
        string? parent = AskFolder("Elija dónde guardar la copia de seguridad");
        if (parent is null) return;

        string dest = ProjectFiles.NewFolder(parent, "IGP_Soft_Copia");
        List<ProjectFile> files = ProjectFiles.Collect(includeProgram: false, _chkSessions.Checked);
        TransferResult? r = await Run(files.Count, p => ProjectFiles.CopyTo(files, dest, p));
        if (r is not null) Done("Se creó la copia de seguridad", r);
    }

    private async Task Restore()
    {
        using var menu = Theme.Menu();
        ToolStripItem fromZip = menu.Items.Add("Desde un archivo .zip…");
        ToolStripItem fromFolder = menu.Items.Add("Desde una carpeta (copia o versión portable)…");
        ToolStripItem? chosen = null;
        menu.ItemClicked += (_, e) => chosen = e.ClickedItem;
        var closed = new TaskCompletionSource();
        menu.Closed += (_, _) => closed.TrySetResult();
        menu.Show(_actions[^1], new Point(0, _actions[^1].Height));
        await closed.Task;

        string? source = null;
        if (chosen == fromZip)
        {
            using var d = new OpenFileDialog { Title = "Elegir .zip", Filter = "Archivo .zip|*.zip", InitialDirectory = LastFolder() };
            if (d.ShowDialog(this) == DialogResult.OK) source = d.FileName;
        }
        else if (chosen == fromFolder)
            source = AskFolder("Elija la carpeta de la copia o versión portable", remember: false);
        if (source is null) return;
        if (Directory.Exists(source) && string.Equals(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Esa es la carpeta actual del programa. Elija una copia o versión portable.", "Restaurar",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int count;
        try { count = ProjectFiles.CountRestorable(source); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo leer el origen:\n{ex.Message}", "Restaurar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (count == 0)
        {
            MessageBox.Show(this, "Ahí no hay enlaces, perfiles, ajustes ni Assets de IGP_Soft.", "Restaurar",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"Se restaurarán {count} archivo(s) y se reemplazarán los actuales.\n\n" +
                "Antes se guarda una copia automática de lo actual en la carpeta «Copias de seguridad», " +
                "y se cierran todas las pantallas abiertas. ¿Continuar?",
                "Restaurar", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        if (!_closeAllForRestore()) return;

        bool zip = File.Exists(source);
        TransferResult? r = await Run(count, p =>
        {
            ProjectFiles.BackupBeforeRestore();
            return zip ? ProjectFiles.RestoreFromZip(source, p) : ProjectFiles.RestoreFromFolder(source, p);
        });
        if (r is null) return;
        _afterRestore();
        Done("Restauración terminada", r);
    }

    private bool OfferToMoveExternalFiles()
    {
        List<string> external = AssetStore.ExternalFilesInUse();
        if (external.Count == 0) return true;

        DialogResult answer = MessageBox.Show(this,
            $"Hay {external.Count} imagen(es) o video(s) guardados fuera de la carpeta Assets, " +
            "así que no se incluirían.\n\n¿Copiarlos a Assets ahora? (Recomendado)",
            "Archivos fuera de Assets", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes)
        {
            UseWaitCursor = true;
            try { AssetStore.MoveExternalFilesInUse(); }
            finally { UseWaitCursor = false; }
        }
        return true;
    }

    private async Task<TransferResult?> Run(int total, Func<IProgress<int>, TransferResult> work)
    {
        foreach (ModernButton b in _actions) b.Enabled = false;
        _progress.Maximum = Math.Max(1, total);
        _progress.Value = 0;
        _status.ForeColor = Theme.TextMuted;
        _status.Text = "Trabajando…";
        var progress = new Progress<int>(n => _progress.Value = Math.Min(_progress.Maximum, n));
        try
        {
            return await Task.Run(() => work(progress));
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = "No se pudo completar: " + ex.Message;
            return null;
        }
        finally
        {
            foreach (ModernButton b in _actions) b.Enabled = true;
        }
    }

    private void Done(string what, TransferResult r)
    {
        _progress.Value = _progress.Maximum;
        _status.ForeColor = r.Skipped > 0 ? Theme.Warning : Theme.Success;
        _status.Text = $"{what}: {r.Copied} archivo(s)." +
                       (r.Skipped > 0 ? $" {r.Skipped} no se pudieron copiar (en uso)." : "") +
                       $"\n{r.Destination}";
    }

    private string? AskFolder(string description, bool remember = true)
    {
        using var d = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, InitialDirectory = LastFolder() };
        if (d.ShowDialog(this) != DialogResult.OK) return null;
        if (ProjectFiles.IsInsideProgramFolder(d.SelectedPath) && remember)
        {
            MessageBox.Show(this, "Elija una carpeta que no esté dentro de la carpeta del programa.", "Carpeta no válida",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        if (remember) RememberFolder(d.SelectedPath);
        return d.SelectedPath;
    }

    private static string LastFolder()
    {
        string? f = Config.Get("carpetaExportar");
        return f is not null && Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static void RememberFolder(string? folder)
    {
        if (!string.IsNullOrEmpty(folder)) Config.Set("carpetaExportar", folder);
    }
}
