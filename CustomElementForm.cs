namespace IGP_Soft;

/// <summary>Diálogo para abrir un elemento suelto (imagen, video o URL) en un monitor.</summary>
internal sealed class CustomElementForm : Form
{
    private readonly TextBox _titulo = new();
    private readonly TextBox _target = new();
    private readonly CheckBox _add = new();

    public string Titulo => _titulo.Text.Trim();
    public string Target => _target.Text.Trim();
    public bool AddToList => _add.Checked;

    public CustomElementForm(int monitorNumber)
    {
        Text = $"Elemento para el monitor {monitorNumber}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(470, 200);
        Font = new Font("Segoe UI", 9f);

        var l1 = new Label { Text = "Título (opcional):", AutoSize = true, Location = new Point(12, 14) };
        _titulo.SetBounds(12, 33, 446, 24);

        var l2 = new Label { Text = "Archivo o URL (imagen, video o página web):", AutoSize = true, Location = new Point(12, 66) };
        _target.SetBounds(12, 85, 350, 24);
        var browse = new Button { Text = "Examinar…", Location = new Point(370, 84), Size = new Size(88, 26) };
        browse.Click += (_, _) => Browse();

        _add.Text = "Agregar también a la lista de enlaces";
        _add.Checked = true;
        _add.SetBounds(12, 120, 446, 22);

        var ok = new Button { Text = "Abrir", DialogResult = DialogResult.OK, Location = new Point(298, 158), Size = new Size(76, 30) };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(382, 158), Size = new Size(76, 30) };
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { l1, _titulo, l2, _target, browse, _add, ok, cancel });

        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && Target.Length == 0)
            {
                MessageBox.Show(this, "Indica un archivo o una URL.", "Elemento",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        };
    }

    private void Browse()
    {
        using var d = new OpenFileDialog
        {
            Title = "Elegir imagen o video",
            Filter = "Imágenes y videos|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp;*.svg;*.avif;*.jfif;" +
                     "*.mp4;*.webm;*.ogv;*.ogg;*.mov;*.m4v;*.mkv|Todos los archivos|*.*",
        };
        if (d.ShowDialog(this) == DialogResult.OK)
        {
            _target.Text = d.FileName;
            if (_titulo.Text.Trim().Length == 0)
                _titulo.Text = Path.GetFileNameWithoutExtension(d.FileName);
        }
    }
}
