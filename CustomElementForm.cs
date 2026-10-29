namespace IGP_Soft;

internal sealed class CustomElementForm : Form
{
    private readonly RoundedTextBox _tituloBox = new("Opcional. Por ejemplo: «Mapa de peligros»", 476);
    private readonly RoundedTextBox _targetBox = new("https://… o elige un archivo de tu PC", 370);
    private readonly ToggleSwitch _add = new("Agregar también a la lista de enlaces");

    public string Titulo => _tituloBox.Box.Text.Trim();
    public string Target => _targetBox.Box.Text.Trim();
    public bool AddToList => _add.Checked;

    private readonly string? _filtro;

    public CustomElementForm(int monitorNumber, string? filtro = null)
    {
        _filtro = filtro;
        Theme.StyleWindow(this, darkCaption: false);
        Text = $"Mostrar en el monitor {monitorNumber}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(524, 300);

        var card = new Card { Location = new Point(14, 14), Size = new Size(496, 216), Padding = new Padding(0) };
        var title = new Label { Text = "Imagen, video, documento o página web", Font = Theme.Section, ForeColor = Theme.Text, AutoSize = true, Location = new Point(20, 16), BackColor = Theme.Surface };
        var l1 = new Label { Text = "Título", ForeColor = Theme.TextMuted, AutoSize = true, Location = new Point(20, 48), BackColor = Theme.Surface };
        _tituloBox.Location = new Point(10, 68);
        var l2 = new Label { Text = "Archivo o dirección web", ForeColor = Theme.TextMuted, AutoSize = true, Location = new Point(20, 112), BackColor = Theme.Surface };
        _targetBox.Location = new Point(10, 132);
        var browse = new ModernButton("Examinar", ButtonKind.Secondary, "▤") { Location = new Point(388, 133), Size = new Size(98, 36) };
        browse.Click += (_, _) => Browse();
        _add.Checked = true;
        _add.Location = new Point(10, 176);
        _add.Width = 470;
        card.Controls.AddRange(new Control[] { title, l1, _tituloBox, l2, _targetBox, browse, _add });

        var ok = new ModernButton("Mostrar", ButtonKind.Primary, "▶") { DialogResult = DialogResult.OK, Location = new Point(300, 248), Size = new Size(110, 38) };
        var cancel = new ModernButton("Cancelar") { DialogResult = DialogResult.Cancel, Location = new Point(418, 248), Size = new Size(92, 38) };
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { card, ok, cancel });

        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && Target.Length == 0)
            {
                MessageBox.Show(this, "Indica un archivo o una dirección web.", "Mostrar",
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
            Filter = _filtro ?? TiposPermitidos.Filtro + "|Todos los archivos|*.*",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _targetBox.Box.Text = d.FileName;
        if (_tituloBox.Box.Text.Trim().Length == 0)
            _tituloBox.Box.Text = Path.GetFileNameWithoutExtension(d.FileName);
    }
}
