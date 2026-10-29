using System.IO.Compression;
using System.Text;

namespace IGP_Soft.Pruebas;

internal static class Ooxml
{
    public static byte[] Crear(string principal, bool macros = false, bool tipoMacro = false)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            string tipo = tipoMacro
                ? "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml"
                : "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
            Escribir(zip, "[Content_Types].xml",
                $"<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/{principal}\" ContentType=\"{tipo}\"/></Types>");
            Escribir(zip, principal, "<?xml version=\"1.0\"?><documento/>");
            if (macros) Escribir(zip, Path.GetDirectoryName(principal)!.Replace('\\', '/') + "/vbaProject.bin", "VBA");
        }
        return ms.ToArray();
    }

    private static void Escribir(ZipArchive zip, string nombre, string texto)
    {
        using var w = new StreamWriter(zip.CreateEntry(nombre).Open(), new UTF8Encoding(false));
        w.Write(texto);
    }

    public static byte[] Pdf(int paginas)
    {
        var objetos = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, paginas).Select(i => $"{3 + i} 0 R"))}] /Count {paginas} >>",
        };
        for (int i = 0; i < paginas; i++) objetos.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] >>");

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objetos.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objetos[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n");
        foreach (int o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}

public sealed class DocumentosPruebas : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "igp_documentos_" + Guid.NewGuid().ToString("N"));

    public DocumentosPruebas() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { }
    }

    private string Guardar(string nombre, byte[] datos)
    {
        string ruta = Path.Combine(_dir, nombre);
        File.WriteAllBytes(ruta, datos);
        return ruta;
    }

    [Theory]
    [InlineData("charla.pdf", TipoDocumento.Pdf)]
    [InlineData(@"Assets\Remoto\abc123-clase.PPTX", TipoDocumento.PowerPoint)]
    [InlineData("show.ppsx", TipoDocumento.PowerPoint)]
    [InlineData("informe.docx", TipoDocumento.Word)]
    [InlineData("informe.docm", TipoDocumento.Ninguno)]
    [InlineData("viejo.ppt", TipoDocumento.Ninguno)]
    [InlineData("https://igp.gob.pe/a.pdf", TipoDocumento.Ninguno)]
    [InlineData("ventana:POWERPNT|clase.pptx", TipoDocumento.Ninguno)]
    [InlineData("video.mp4", TipoDocumento.Ninguno)]
    internal void Reconoce_el_tipo_de_documento(string url, TipoDocumento esperado) =>
        Assert.Equal(esperado, Documentos.Tipo(url));

    [Fact]
    public void Word_y_PowerPoint_sin_macros_pasan()
    {
        Assert.True(TiposPermitidos.Archivo(Guardar("a.docx", Ooxml.Crear("word/document.xml"))));
        Assert.True(TiposPermitidos.Archivo(Guardar("a.pptx", Ooxml.Crear("ppt/presentation.xml"))));
        Assert.True(TiposPermitidos.Archivo(Guardar("a.ppsx", Ooxml.Crear("ppt/presentation.xml"))));
    }

    [Fact]
    public void Documentos_con_macros_o_falsos_no_pasan()
    {
        Assert.False(TiposPermitidos.Archivo(Guardar("m.pptx", Ooxml.Crear("ppt/presentation.xml", macros: true))));
        Assert.False(TiposPermitidos.Archivo(Guardar("t.pptx", Ooxml.Crear("ppt/presentation.xml", tipoMacro: true))));
        Assert.False(TiposPermitidos.Archivo(Guardar("w.docx", Ooxml.Crear("ppt/presentation.xml"))));
        Assert.False(TiposPermitidos.Archivo(Guardar("x.docx", new byte[] { 0x4D, 0x5A, 0x90, 0x00 })));
        Assert.False(TiposPermitidos.Archivo(Guardar("z.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 })));
        Assert.False(TiposPermitidos.Archivo(Guardar("m.docm", Ooxml.Crear("word/document.xml"))));
        Assert.False(TiposPermitidos.Extension("viejo.ppt"));
    }

    [Fact]
    public void El_nombre_real_decide_el_tipo()
    {
        string ruta = Guardar("sin-extension.parcial", Ooxml.Crear("word/document.xml"));
        Assert.True(TiposPermitidos.Archivo(ruta, "informe.docx"));
        Assert.False(TiposPermitidos.Archivo(ruta, "informe.pptx"));
    }

    [Fact]
    public async Task Lee_y_dibuja_cada_pagina_del_pdf()
    {
        var doc = await Documentos.CargarPdf(Guardar("tres.pdf", Ooxml.Pdf(3)));
        Assert.Equal(3u, doc.PageCount);
        using Bitmap? bmp = await VisorPdf.Dibujar(doc, 2, new Size(800, 800));
        Assert.NotNull(bmp);
        Assert.Equal(800, bmp!.Width);
        Assert.Equal(600, bmp.Height);
        Assert.Equal(Color.FromArgb(255, 255, 255), bmp.GetPixel(400, 300));
    }

    [Theory]
    [InlineData(400, 300, 800, 800, 800, 600)]
    [InlineData(400, 300, 1920, 540, 720, 540)]
    [InlineData(0, 0, 100, 50, 100, 50)]
    public void La_pagina_se_ajusta_a_la_zona(double w, double h, int aw, int ah, int ew, int eh) =>
        Assert.Equal(new Size(ew, eh), VisorPdf.Ajustar(w, h, new Size(aw, ah)));

    [Fact]
    public void Un_pdf_danado_no_se_abre_en_el_visor() =>
        Assert.ThrowsAny<Exception>(() => Documentos.CargarPdf(Guardar("roto.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 basura"))).GetAwaiter().GetResult());

    [Fact]
    public void El_video_trae_sus_controles()
    {
        string html = MediaWrapper.VideoHtml("file:///C:/v.mp4");
        Assert.Contains("src=\"file:///C:/v.mp4\" autoplay muted loop", html);
        foreach (string id in new[] { "play", "atras", "adelante", "avance", "sonido", "volumen", "velocidad", "bucle" })
            Assert.Contains($"id=\"{id}\"", html);
        Assert.Contains("playbackRate", html);
    }
}
