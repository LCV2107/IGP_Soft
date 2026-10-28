using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IGP_Soft.Pruebas;

internal sealed class PantallasFalsas : IPantallas
{
    public EstadoPantallas Estado { get; set; } = EstadoPantallas.Vacio with
    {
        Seq = 3,
        Equipo = "PC-SALA",
        Navegador = "Edge",
        Monitores = new[]
        {
            new EstadoMonitor("MON-A-1", @"\\.\DISPLAY1", 1, 0, 0, 1920, 1080, true, 1, false, 1, 1, new[] { new ZoneSpec(0, 0) }, null,
                new[] { new EstadoZona(0, EstadosZona.Libre, null, null, false, 0, 0, 1920, 1080) }),
        },
        Enlaces = new[] { new LinkDef("Video local", @"C:\videos\sala.mp4") },
    };

    public List<string> Llamadas { get; } = new();

    public event Action<EstadoPantallas>? Cambio;
    public event Action<Resultado>? Aviso;

    public void Cambiar(EstadoPantallas e)
    {
        Estado = e;
        Cambio?.Invoke(e);
    }

    public void Avisar(Resultado r) => Aviso?.Invoke(r);

    private Task<Resultado> Ok(string llamada)
    {
        lock (Llamadas) Llamadas.Add(llamada);
        return Task.FromResult(Resultado.Bien);
    }

    public Task<Resultado> Abrir(string monitor, int zona, LinkDef enlace) => Ok($"abrir {monitor} {zona} {enlace.Url}");
    public Task<Resultado> Cerrar(string monitor, int zona) => Ok($"cerrar {monitor} {zona}");
    public Task<Resultado> CambiarGrilla(string monitor, GridLayout grilla) => Ok($"grilla {monitor} {grilla.Describe()}");
    public Task<Resultado> DividirTodos(int columnas, int filas) => Ok($"dividir {columnas}x{filas}");
    public Task<Resultado> AlternarOmitir(string monitor) => Ok($"omitir {monitor}");
    public Task<Resultado> OmitirPrincipal(bool omitir) => Ok($"principal {omitir}");
    public Task<Resultado> CerrarMonitor(string monitor) => Ok($"cerrar_monitor {monitor}");
    public Task<Resultado> IntercambiarNumeros(string a, string b) => Ok($"intercambiar {a} {b}");
    public Task<Resultado> NumeracionOriginal() => Ok("numeracion");
    public Task<Resultado> Combinar(List<List<string>> grupos) => Ok($"combinar {string.Join(";", grupos.Select(g => string.Join("+", g)))}");
    public Task<Resultado> Separar(string monitor) => Ok($"separar {monitor}");
    public Task<Resultado> Ampliar(string monitor, int zona) => Ok($"ampliar {monitor} {zona}");
    public Task<Resultado> Restaurar(string? monitor) => Ok($"restaurar {monitor ?? "todos"}");
    public Task<Resultado> AmpliarSiguiente(string monitor, int paso) => Ok($"siguiente {monitor} {paso}");
    public Task<Resultado> AplicarPerfil(LayoutProfile perfil) => Ok($"perfil {perfil.Name}");
    public Task<Resultado> AbrirEnOrden() => Ok("en_orden");
    public Task<Resultado> CerrarTodo() => Ok("cerrar_todo");
    public Task<Resultado> Identificar() => Ok("identificar");
    public Task<Resultado> RecargarMonitores() => Ok("recargar_monitores");
    public Task<Resultado> RecargarTodo() => Ok("recargar_todo");
    public Task<Resultado> ElegirNavegador(string nombre) => Ok($"navegador {nombre}");
    public Task<Resultado> GuardarEnlaces(List<LinkDef> enlaces) => Ok($"enlaces {enlaces.Count}");
    public Task<Resultado> GuardarPerfiles(List<LayoutProfile> perfiles) => Ok($"perfiles {perfiles.Count}");
    public Task<List<VentanaAbierta>> VentanasAbiertas() => Task.FromResult(new List<VentanaAbierta> { new("javaw", "Sistema", "ventana:javaw|Sistema") });
    public Task<List<MonitorFisico>> MonitoresFisicos() => Task.FromResult(new List<MonitorFisico> { new("MON-A-1", 0, 0, 1920, 1080, true) });
    public Task<byte[]?> VistaPrevia(string monitor, int ancho) => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });
}

public sealed class ProtocoloPruebas : IDisposable
{
    private const string Version = "1.0.2-exp.9";
    private readonly string _tubo = "IGP_Soft.Prueba." + Guid.NewGuid().ToString("N");
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "igp_pruebas_" + Guid.NewGuid().ToString("N"), "Assets");
    private readonly PantallasFalsas _falsas = new();
    private readonly ServidorControl _servidor;

    public ProtocoloPruebas()
    {
        var lista = new ListaBlanca(() => _assets, () => _falsas.Estado.Enlaces.Select(l => l.Url));
        _servidor = new ServidorControl(_falsas, _tubo, f => f(), lista, Version);
        _servidor.Iniciar();
    }

    public void Dispose() => _servidor.Dispose();

    private async Task<PantallasRemotas> Cliente(string version = Version)
    {
        var c = new PantallasRemotas(_tubo, version);
        Resultado r = await c.ConectarAsync();
        Assert.True(r.Ok, r.Mensaje);
        return c;
    }

    private async Task<(CanalLineas Canal, NamedPipeClientStream Pipe)> Crudo(bool saludar)
    {
        var pipe = new NamedPipeClientStream(".", _tubo, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        var canal = new CanalLineas(pipe);
        if (saludar)
        {
            await canal.EscribirAsync(new JsonObject { ["id"] = 1, ["tipo"] = "hola", ["app"] = Version, ["protocolo"] = 1, ["equipo"] = "PRUEBA" }, default);
            Assert.True(Protocolo.ComoResultado(await Respuesta(canal, 1)).Ok);
        }
        return (canal, pipe);
    }

    private static async Task<JsonElement> Respuesta(CanalLineas canal, long id)
    {
        using var cts = new CancellationTokenSource(5000);
        while (true)
        {
            Linea l = await canal.LeerAsync(cts.Token);
            Assert.False(l.Fin, "la conexión se cerró");
            JsonElement e = JsonDocument.Parse(l.Texto!).RootElement.Clone();
            if (Protocolo.Numero(e, "id") == id && e.TryGetProperty("ok", out _)) return e;
        }
    }

    [Fact]
    public async Task Saludo_y_estado_completo_al_conectar()
    {
        using PantallasRemotas c = await Cliente();
        Assert.Equal(Environment.MachineName, c.Equipo);
        Assert.Equal("PC-SALA", c.Estado.Equipo);
        Assert.Equal(3, c.Estado.Seq);
        Assert.Equal("MON-A-1", c.Estado.Monitores[0].Id);
        Assert.Equal(EstadosZona.Libre, c.Estado.Monitores[0].Zonas[0].Estado);
    }

    [Fact]
    public async Task Las_ordenes_llegan_al_equipo_controlado()
    {
        using PantallasRemotas c = await Cliente();
        Assert.True((await c.Abrir("MON-A-1", 0, new LinkDef("Sismo", "https://ultimosismo.igp.gob.pe/ultimo-sismo"))).Ok);
        Assert.True((await c.CambiarGrilla("MON-A-1", GridLayout.Uniform(2, 2))).Ok);
        Assert.True((await c.Combinar(new() { new() { "MON-A-1", "MON-B-2" } })).Ok);
        Assert.True((await c.Restaurar(null)).Ok);
        Assert.True((await c.AbrirEnOrden()).Ok);
        Assert.Single(await c.VentanasAbiertas());
        Assert.Equal(new byte[] { 1, 2, 3 }, await c.VistaPrevia("MON-A-1", 240));
        Assert.Contains("abrir MON-A-1 0 https://ultimosismo.igp.gob.pe/ultimo-sismo", _falsas.Llamadas);
        Assert.Contains("grilla MON-A-1 2×2", _falsas.Llamadas);
        Assert.Contains("combinar MON-A-1+MON-B-2", _falsas.Llamadas);
        Assert.Contains("restaurar todos", _falsas.Llamadas);
        Assert.Contains("en_orden", _falsas.Llamadas);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData(@"\\servidor\compartido\x.mp4")]
    [InlineData("ftp://servidor/x")]
    [InlineData("Assets/../IGP_Soft.config.txt")]
    public async Task Rechaza_enlaces_no_permitidos(string url)
    {
        using PantallasRemotas c = await Cliente();
        Resultado r = await c.Abrir("MON-A-1", 0, new LinkDef("x", url));
        Assert.Equal("enlace_no_permitido", r.Codigo);
        Assert.DoesNotContain(_falsas.Llamadas, l => l.StartsWith("abrir"));
    }

    [Fact]
    public async Task Acepta_enlaces_que_ya_estan_en_la_lista_del_equipo()
    {
        using PantallasRemotas c = await Cliente();
        Assert.True((await c.Abrir("MON-A-1", 0, new LinkDef("Video", @"C:\videos\sala.mp4"))).Ok);
    }

    [Fact]
    public async Task Rechaza_perfiles_y_listas_con_enlaces_no_permitidos()
    {
        using PantallasRemotas c = await Cliente();
        var perfil = new LayoutProfile { Name = "x", Monitors = { new MonitorLayout { Links = { new ZoneLayout { Url = @"C:\x.exe" } } } } };
        Assert.Equal("enlace_no_permitido", (await c.AplicarPerfil(perfil)).Codigo);
        Assert.Equal("enlace_no_permitido", (await c.GuardarEnlaces(new() { new("x", @"D:\malo.bat") })).Codigo);
        Assert.True((await c.GuardarEnlaces(new() { new("Sismo", "https://ultimosismo.igp.gob.pe") })).Ok);
    }

    [Fact]
    public async Task Versiones_distintas_no_se_conectan()
    {
        using var c = new PantallasRemotas(_tubo, "9.9.9");
        Resultado r = await c.ConectarAsync();
        Assert.False(r.Ok);
        Assert.Equal("version_distinta", r.Codigo);
        Assert.Contains("9.9.9", r.Mensaje);
        Assert.Contains(Version, r.Mensaje);
    }

    [Fact]
    public async Task El_segundo_cliente_recibe_ocupado()
    {
        using PantallasRemotas primero = await Cliente();
        using var segundo = new PantallasRemotas(_tubo, Version);
        Resultado r = await segundo.ConectarAsync();
        Assert.Equal("ocupado", r.Codigo);
        Assert.True((await primero.Identificar()).Ok);
    }

    [Fact]
    public async Task Orden_desconocida_no_corta_la_conexion()
    {
        (CanalLineas canal, NamedPipeClientStream pipe) = await Crudo(saludar: true);
        using (pipe)
        using (canal)
        {
            await canal.EscribirAsync(new JsonObject { ["id"] = 5, ["tipo"] = "girar", ["extra"] = 1 }, default);
            Assert.Equal("orden_desconocida", Protocolo.Texto(await Respuesta(canal, 5), "error"));
            await canal.EscribirAsync(new JsonObject { ["id"] = 6, ["tipo"] = "identificar", ["campoNuevo"] = "x" }, default);
            Assert.True(Protocolo.ComoResultado(await Respuesta(canal, 6)).Ok);
        }
    }

    [Fact]
    public async Task Sin_saludo_se_rechaza()
    {
        (CanalLineas canal, NamedPipeClientStream pipe) = await Crudo(saludar: false);
        using (pipe)
        using (canal)
        {
            await canal.EscribirAsync(new JsonObject { ["id"] = 2, ["tipo"] = "cerrar_todo" }, default);
            Assert.Equal("falta_saludo", Protocolo.Texto(await Respuesta(canal, 2), "error"));
        }
        Assert.Empty(_falsas.Llamadas);
    }

    [Fact]
    public async Task Mensaje_de_mas_de_un_mega_se_descarta()
    {
        (CanalLineas canal, NamedPipeClientStream pipe) = await Crudo(saludar: true);
        using (pipe)
        using (canal)
        {
            byte[] grande = System.Text.Encoding.UTF8.GetBytes("{\"id\":7,\"tipo\":\"abrir\",\"x\":\"" + new string('a', Protocolo.MaxBytes) + "\"}\n");
            await pipe.WriteAsync(grande).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            await pipe.FlushAsync();
            Assert.Equal("mensaje_demasiado_grande", Protocolo.Texto(await Respuesta(canal, 0), "error"));
            await canal.EscribirAsync(new JsonObject { ["id"] = 8, ["tipo"] = "identificar" }, default);
            Assert.True(Protocolo.ComoResultado(await Respuesta(canal, 8)).Ok);
        }
    }

    [Fact]
    public async Task Los_cambios_y_avisos_llegan_al_panel_remoto()
    {
        using PantallasRemotas c = await Cliente();
        var estado = new TaskCompletionSource<EstadoPantallas>();
        var aviso = new TaskCompletionSource<Resultado>();
        c.Cambio += e => { if (e.Seq == 10) estado.TrySetResult(e); };
        c.Aviso += r => aviso.TrySetResult(r);

        _falsas.Cambiar(_falsas.Estado with { Seq = 9 });
        _falsas.Cambiar(_falsas.Estado with { Seq = 10, Navegador = "Firefox" });
        _falsas.Avisar(new Resultado(false, "enlace_en_uso", "Enlace en uso", "Ya está abierto.", Nivel.Info));

        Assert.Same(estado.Task, await Task.WhenAny(estado.Task, Task.Delay(5000)));
        Assert.Equal("Firefox", (await estado.Task).Navegador);
        Assert.Same(aviso.Task, await Task.WhenAny(aviso.Task, Task.Delay(5000)));
        Assert.Equal("enlace_en_uso", (await aviso.Task).Codigo);
    }

    [Fact]
    public async Task El_equipo_controlado_sabe_quien_se_conecta_y_cuando_se_va()
    {
        var conectado = new TaskCompletionSource<string>();
        var desconectado = new TaskCompletionSource();
        _servidor.Conectado += e => conectado.TrySetResult(e);
        _servidor.Desconectado += () => desconectado.TrySetResult();

        PantallasRemotas c = await Cliente();
        Assert.Equal(Environment.MachineName, await conectado.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        c.Dispose();
        await desconectado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(_servidor.HayCliente);
    }

    [Fact]
    public async Task Se_puede_volver_a_conectar_y_recibir_el_estado()
    {
        using (PantallasRemotas c = await Cliente()) Assert.Equal(3, c.Estado.Seq);
        await Task.Delay(300);
        using PantallasRemotas otra = await Cliente();
        Assert.Equal("PC-SALA", otra.Estado.Equipo);
    }

    [Theory]
    [InlineData("https://ultimosismo.igp.gob.pe/ultimo-sismo", true)]
    [InlineData("http://192.168.1.10/panel", true)]
    [InlineData("Assets/videos/sala.mp4", true)]
    [InlineData(@"Assets\imagen.png", true)]
    [InlineData("ventana:javaw|Sistema", true)]
    [InlineData(@"Assets\..\..\Windows\win.ini", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void Lista_blanca(string url, bool permitido)
    {
        var lista = new ListaBlanca(() => _assets, () => Array.Empty<string>());
        Assert.Equal(permitido, lista.Permite(url));
    }

    [Fact]
    public async Task Canal_lee_lineas_partidas_y_con_retorno_de_carro()
    {
        var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"a\":1}\r\n{\"b\":\"ñ\"}\n" + new string('x', 70000) + "\n"));
        var canal = new CanalLineas(ms);
        Assert.Equal("{\"a\":1}", (await canal.LeerAsync(default)).Texto);
        Assert.Equal("{\"b\":\"ñ\"}", (await canal.LeerAsync(default)).Texto);
        Assert.Equal(70000, (await canal.LeerAsync(default)).Texto!.Length);
        Assert.True((await canal.LeerAsync(default)).Fin);
    }
}
