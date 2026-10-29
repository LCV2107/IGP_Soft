using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IGP_Soft.Pruebas;

public sealed class ArchivosPruebas : IDisposable
{
    private const string Version = "1.0.2-exp.9";
    private readonly string _tubo = "IGP_Soft.Prueba." + Guid.NewGuid().ToString("N");
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "igp_archivos_" + Guid.NewGuid().ToString("N"));
    private readonly string _assets;
    private readonly string _temporal;
    private readonly PantallasFalsas _falsas = new();
    private readonly List<string> _usados = new();
    private readonly ServidorControl _servidor;
    private long _libre = long.MaxValue;
    private long _limite = 50L * 1024 * 1024;

    public ArchivosPruebas()
    {
        Huellas.Ruta = null;
        _assets = Path.Combine(_raiz, "Assets");
        _temporal = Path.Combine(_raiz, "subidas");
        Directory.CreateDirectory(_assets);
        var lista = new ListaBlanca(() => _assets, () => _falsas.Estado.Enlaces.Select(l => l.Url));
        var archivos = new RecepcionArchivos(_assets, _temporal, () => _limite, () => _libre);
        _servidor = new ServidorControl(_falsas, _tubo, f => f(), lista, Version, archivos, () => _usados.ToList());
        _servidor.Iniciar();
    }

    public void Dispose()
    {
        _servidor.Dispose();
        try { Directory.Delete(_raiz, true); }
        catch { }
    }

    private string Reserva => Path.Combine(_assets, RecepcionArchivos.Carpeta);

    private static byte[] Mp4(int bytes, int semilla = 1)
    {
        byte[] d = new byte[bytes];
        new Random(semilla).NextBytes(d);
        byte[] cab = { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' };
        cab.CopyTo(d, 0);
        return d;
    }

    private string Local(string nombre, byte[] datos)
    {
        string dir = Path.Combine(_raiz, "local");
        Directory.CreateDirectory(dir);
        string ruta = Path.Combine(dir, nombre);
        File.WriteAllBytes(ruta, datos);
        return ruta;
    }

    private static string Sha(byte[] d) => Convert.ToHexString(SHA256.HashData(d)).ToLowerInvariant();

    private async Task<PantallasRemotas> Cliente()
    {
        var c = new PantallasRemotas(_tubo, Version);
        Resultado r = await c.ConectarAsync();
        Assert.True(r.Ok, r.Mensaje);
        return c;
    }

    private async Task<(CanalLineas Canal, NamedPipeClientStream Pipe)> Crudo()
    {
        var pipe = new NamedPipeClientStream(".", _tubo, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        var canal = new CanalLineas(pipe);
        await canal.EscribirAsync(new JsonObject { ["id"] = 1, ["tipo"] = "hola", ["app"] = Version, ["protocolo"] = 1, ["equipo"] = "PRUEBA" }, default);
        Assert.True(Protocolo.ComoResultado(await Respuesta(canal, 1)).Ok);
        return (canal, pipe);
    }

    private static async Task<JsonElement> Respuesta(CanalLineas canal, long id)
    {
        using var cts = new CancellationTokenSource(10000);
        while (true)
        {
            Linea l = await canal.LeerAsync(cts.Token);
            Assert.False(l.Fin, "la conexión se cerró");
            JsonElement e = JsonDocument.Parse(l.Texto!).RootElement.Clone();
            if (Protocolo.Numero(e, "id") == id && e.TryGetProperty("ok", out _)) return e;
        }
    }

    private static async Task<JsonElement> Orden(CanalLineas canal, long id, JsonObject o)
    {
        o["id"] = id;
        await canal.EscribirAsync(o, default);
        return await Respuesta(canal, id);
    }

    [Theory]
    [InlineData("a.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData("a.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData("a.gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]
    [InlineData("a.mp4", new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70 }, true)]
    [InlineData("a.mov", new byte[] { 0, 0, 0, 0x08, 0x77, 0x69, 0x64, 0x65 }, true)]
    [InlineData("a.webm", new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, true)]
    [InlineData("a.ogv", new byte[] { 0x4F, 0x67, 0x67, 0x53 }, true)]
    [InlineData("a.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, true)]
    [InlineData("a.jpg", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, false)]
    [InlineData("a.mp4", new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0 }, false)]
    [InlineData("a.exe", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, false)]
    [InlineData("a.html", new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E }, false)]
    public void Firmas_de_contenido(string nombre, byte[] cabecera, bool valido) =>
        Assert.Equal(valido, TiposPermitidos.Contenido(nombre, cabecera));

    [Fact]
    public void Svg_solo_si_es_svg()
    {
        Assert.True(TiposPermitidos.Contenido("a.svg", Encoding.UTF8.GetBytes("\uFEFF<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"/>")));
        Assert.False(TiposPermitidos.Contenido("a.svg", Encoding.UTF8.GetBytes("MZ\u0090 no es svg")));
    }

    [Theory]
    [InlineData("volcán.mp4", "volcan.mp4")]
    [InlineData(@"..\..\Windows\malo.exe.jpg", "malo.exe.jpg")]
    [InlineData("C:/x/Mapa de peligros (final).PNG", "Mapa-de-peligros-final.png")]
    [InlineData("¿?.pdf", "archivo.pdf")]
    [InlineData("a..b.jpg", "a.b.jpg")]
    public void Nombres_saneados(string entrada, string esperado) =>
        Assert.Equal(esperado, TiposPermitidos.NombreSeguro(entrada));

    [Theory]
    [InlineData("Assets/x.html", false)]
    [InlineData(@"Assets\Remoto\abc123-x.pdf", true)]
    [InlineData(@"Assets\Remoto\abc123-x.exe", false)]
    [InlineData(@"Assets\Remoto\abc.parcial", false)]
    public void Lista_blanca_de_assets_por_tipo(string url, bool permitido) =>
        Assert.Equal(permitido, new ListaBlanca(() => _assets, () => Array.Empty<string>()).Permite(url));

    [Fact]
    public async Task Sube_un_video_en_varios_trozos_y_queda_en_la_reserva()
    {
        byte[] datos = Mp4(1_300_000);
        string ruta = Local("volcán.mp4", datos);
        using PantallasRemotas c = await Cliente();
        var avances = new List<double>();
        (Resultado r, string? url) = await c.Subir(ruta, new SyncProgress(avances.Add), default);

        Assert.True(r.Ok, r.Mensaje);
        Assert.Equal(Path.Combine("Assets", "Remoto", Sha(datos)[..6] + "-volcan.mp4"), url);
        Assert.Equal(datos, File.ReadAllBytes(Path.Combine(_raiz, url!)));
        Assert.Equal(1.0, avances[^1]);
        Assert.Empty(Directory.GetFiles(_temporal, "*.parcial"));
        Assert.True((await c.Abrir("MON-A-1", 0, new LinkDef("Volcán", url!))).Ok);
    }

    [Fact]
    public async Task El_mismo_archivo_no_se_vuelve_a_enviar()
    {
        byte[] datos = Mp4(700_000, 2);
        using PantallasRemotas c = await Cliente();
        (Resultado r1, string? url1) = await c.Subir(Local("a.mp4", datos), null, default);
        Assert.True(r1.Ok, r1.Mensaje);
        DateTime fecha = File.GetLastWriteTimeUtc(Path.Combine(_raiz, url1!));

        var avances = new List<double>();
        (Resultado r2, string? url2) = await c.Subir(Local("copia con otro nombre.mp4", datos), new SyncProgress(avances.Add), default);
        Assert.True(r2.Ok, r2.Mensaje);
        Assert.Equal(url1, url2);
        Assert.Equal(new[] { 1.0 }, avances);
        Assert.Equal(fecha, File.GetLastWriteTimeUtc(Path.Combine(_raiz, url2!)));
        Assert.Single(Directory.GetFiles(Reserva));
    }

    [Fact]
    public async Task Una_subida_cortada_sigue_desde_donde_quedo()
    {
        byte[] datos = Mp4(RecepcionArchivos.Trozo * 3 + 1000, 3);
        string sha = Sha(datos);
        (CanalLineas canal, NamedPipeClientStream pipe) = await Crudo();
        using (pipe)
        using (canal)
        {
            JsonElement ini = await Orden(canal, 2, new JsonObject { ["tipo"] = "subir_inicio", ["sha256"] = sha, ["nombre"] = "sala.mp4", ["bytes"] = datos.Length });
            Assert.True(Protocolo.ComoResultado(ini).Ok);
            JsonElement t = await Orden(canal, 3, new JsonObject
            {
                ["tipo"] = "subir_trozo", ["subida"] = Protocolo.Texto(ini, "subida"), ["desde"] = 0,
                ["datos"] = Convert.ToBase64String(datos, 0, RecepcionArchivos.Trozo),
            });
            Assert.Equal(RecepcionArchivos.Trozo, Protocolo.Numero(t, "recibido"));
        }
        await Task.Delay(300);

        using PantallasRemotas c = await Cliente();
        var avances = new List<double>();
        (Resultado r, string? url) = await c.Subir(Local("sala.mp4", datos), new SyncProgress(avances.Add), default);
        Assert.True(r.Ok, r.Mensaje);
        Assert.True(avances[0] >= 0.25, $"empezó en {avances[0]}");
        Assert.Equal(datos, File.ReadAllBytes(Path.Combine(_raiz, url!)));
    }

    [Fact]
    public async Task Huella_distinta_se_rechaza_y_no_queda_nada()
    {
        byte[] datos = Mp4(10_000, 4);
        (CanalLineas canal, NamedPipeClientStream pipe) = await Crudo();
        using (pipe)
        using (canal)
        {
            JsonElement ini = await Orden(canal, 2, new JsonObject { ["tipo"] = "subir_inicio", ["sha256"] = new string('a', 64), ["nombre"] = "x.mp4", ["bytes"] = datos.Length });
            string? subida = Protocolo.Texto(ini, "subida");
            await Orden(canal, 3, new JsonObject { ["tipo"] = "subir_trozo", ["subida"] = subida, ["desde"] = 0, ["datos"] = Convert.ToBase64String(datos) });
            JsonElement fin = await Orden(canal, 4, new JsonObject { ["tipo"] = "subir_fin", ["subida"] = subida });
            Assert.Equal("huella_distinta", Protocolo.Texto(fin, "error"));
        }
        Assert.False(Directory.Exists(Reserva) && Directory.GetFiles(Reserva).Length > 0);
        Assert.Empty(Directory.GetFiles(_temporal, "*.parcial"));
    }

    [Fact]
    public async Task Un_programa_renombrado_no_pasa()
    {
        byte[] exe = new byte[5000];
        exe[0] = 0x4D;
        exe[1] = 0x5A;
        using PantallasRemotas c = await Cliente();
        Assert.Equal("contenido_no_valido", (await c.Subir(Local("foto.jpg", exe), null, default)).R.Codigo);
        Assert.Equal("tipo_no_permitido", (await c.Subir(Local("calc.exe", exe), null, default)).R.Codigo);

        c.Dispose();
        await Task.Delay(300);
        (CanalLineas canal, NamedPipeClientStream pipe) = await Crudo();
        using (pipe)
        using (canal)
        {
            JsonElement ini = await Orden(canal, 2, new JsonObject { ["tipo"] = "subir_inicio", ["sha256"] = Sha(exe), ["nombre"] = "foto.jpg", ["bytes"] = exe.Length });
            JsonElement t = await Orden(canal, 3, new JsonObject { ["tipo"] = "subir_trozo", ["subida"] = Protocolo.Texto(ini, "subida"), ["desde"] = 0, ["datos"] = Convert.ToBase64String(exe) });
            Assert.Equal("contenido_no_valido", Protocolo.Texto(t, "error"));
            JsonElement prog = await Orden(canal, 4, new JsonObject { ["tipo"] = "subir_inicio", ["sha256"] = Sha(exe), ["nombre"] = "calc.exe", ["bytes"] = exe.Length });
            Assert.Equal("tipo_no_permitido", Protocolo.Texto(prog, "error"));
        }
        Assert.Empty(Directory.GetFiles(_temporal, "*.parcial"));
    }

    [Fact]
    public async Task Trozos_fuera_de_orden_se_rechazan()
    {
        byte[] datos = Mp4(RecepcionArchivos.Trozo + 10, 5);
        (CanalLineas canal, NamedPipeClientStream pipe) = await Crudo();
        using (pipe)
        using (canal)
        {
            JsonElement ini = await Orden(canal, 2, new JsonObject { ["tipo"] = "subir_inicio", ["sha256"] = Sha(datos), ["nombre"] = "x.mp4", ["bytes"] = datos.Length });
            JsonElement t = await Orden(canal, 3, new JsonObject
            {
                ["tipo"] = "subir_trozo", ["subida"] = Protocolo.Texto(ini, "subida"), ["desde"] = 100,
                ["datos"] = Convert.ToBase64String(datos, 100, 10),
            });
            Assert.Equal("fuera_de_orden", Protocolo.Texto(t, "error"));
            Assert.Equal(0, Protocolo.Numero(t, "recibido"));
        }
    }

    [Fact]
    public async Task Sin_espacio_y_demasiado_grande()
    {
        using PantallasRemotas c = await Cliente();
        _libre = 10;
        Assert.Equal("sin_espacio", (await c.Subir(Local("a.mp4", Mp4(5000, 6)), null, default)).R.Codigo);
        _libre = long.MaxValue;
        _limite = 1000;
        (Resultado r, _) = await c.Subir(Local("b.mp4", Mp4(5000, 7)), null, default);
        Assert.Equal("demasiado_grande", r.Codigo);
        Assert.Contains("MB", r.Mensaje);
    }

    [Fact]
    public async Task Cancelar_a_medias_no_deja_restos()
    {
        byte[] datos = Mp4(RecepcionArchivos.Trozo * 12, 8);
        using PantallasRemotas c = await Cliente();
        using var cts = new CancellationTokenSource();
        (Resultado r, string? url) = await c.Subir(Local("largo.mp4", datos), new SyncProgress(f => { if (f > 0.1) cts.Cancel(); }), cts.Token);
        Assert.Equal("cancelado", r.Codigo);
        Assert.Null(url);
        for (int i = 0; i < 50 && Directory.GetFiles(_temporal, "*.parcial").Length > 0; i++) await Task.Delay(100);
        Assert.Empty(Directory.GetFiles(_temporal, "*.parcial"));
        Assert.True(!Directory.Exists(Reserva) || Directory.GetFiles(Reserva).Length == 0);
        Assert.True((await c.Identificar()).Ok);
    }

    [Fact]
    public async Task Lista_y_borrado_respetan_el_uso()
    {
        byte[] datos = Mp4(3000, 9);
        File.WriteAllBytes(Path.Combine(_assets, "propio.mp4"), Mp4(2000, 10));
        File.WriteAllText(Path.Combine(_assets, "nota.txt"), "no se lista");
        using PantallasRemotas c = await Cliente();
        (Resultado r, string? url) = await c.Subir(Local("recibido.mp4", datos), null, default);
        Assert.True(r.Ok, r.Mensaje);
        _usados.Add(url!.Replace('\\', '/'));

        List<ArchivoRemoto>? lista = await c.Archivos();
        Assert.NotNull(lista);
        Assert.Equal(2, lista!.Count);
        ArchivoRemoto recibido = lista.Single(a => a.Reserva);
        Assert.Equal(1, recibido.Usos);
        Assert.Equal(datos.Length, recibido.Bytes);
        Assert.False(lista.Single(a => !a.Reserva).Reserva);

        Assert.Equal("en_uso", (await c.BorrarArchivo(url)).Codigo);
        Assert.Equal("solo_reserva", (await c.BorrarArchivo(Path.Combine("Assets", "propio.mp4"))).Codigo);
        Assert.Equal("archivo_no_encontrado", (await c.BorrarArchivo(@"Assets\..\IGP_Soft.config.txt")).Codigo);
        _usados.Clear();
        Assert.True((await c.BorrarArchivo(url)).Ok);
        Assert.False(File.Exists(Path.Combine(_raiz, url)));
        Assert.True(File.Exists(Path.Combine(_assets, "propio.mp4")));
    }

    [Fact]
    public void Las_huellas_se_recuerdan_por_tamano_y_fecha()
    {
        string ruta = Local("h.mp4", Mp4(4000, 11));
        string a = Huellas.De(ruta);
        Assert.Equal(a, Huellas.De(ruta));
        File.WriteAllBytes(ruta, Mp4(4000, 12));
        File.SetLastWriteTimeUtc(ruta, DateTime.UtcNow.AddMinutes(1));
        Assert.NotEqual(a, Huellas.De(ruta));
    }

    private sealed class SyncProgress : IProgress<double>
    {
        private readonly Action<double> _a;
        public SyncProgress(Action<double> a) => _a = a;
        public void Report(double value) { lock (this) _a(value); }
    }
}
