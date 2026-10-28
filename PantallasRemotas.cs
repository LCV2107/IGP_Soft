using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IGP_Soft;

internal sealed class PantallasRemotas : IPantallas, IDisposable
{
    private readonly string _tubo;
    private readonly string _version;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pendientes = new();
    private readonly CancellationTokenSource _cierre = new();
    private CanalLineas? _canal;
    private CancellationTokenSource? _sesion;
    private long _siguiente;
    private DateTime _ultimo = DateTime.UtcNow;
    private TaskCompletionSource<bool>? _primerEstado;
    private volatile bool _cerrado;

    public PantallasRemotas(string tubo, string? version = null)
    {
        _tubo = tubo;
        _version = version ?? Protocolo.VersionApp;
    }

    public EstadoPantallas Estado { get; private set; } = EstadoPantallas.Vacio;

    public event Action<EstadoPantallas>? Cambio;
    public event Action<Resultado>? Aviso;
    public event Action? Reconectando;
    public event Action? Reconectado;
    public event Action<string>? Desconectado;

    public string Equipo { get; private set; } = "";

    public async Task<Resultado> ConectarAsync()
    {
        Resultado r = await ConectarUnaVez(Protocolo.Conexion).ConfigureAwait(false);
        if (r.Ok) _ = Task.Run(Vigilar);
        return r;
    }

    private async Task<Resultado> ConectarUnaVez(TimeSpan limite)
    {
        var pipe = new NamedPipeClientStream(".", _tubo, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var cts = new CancellationTokenSource(limite);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            pipe.Dispose();
            return new Resultado(false, "sin_conexion", "Control remoto",
                "No se pudo conectar con el IGP_Soft del otro equipo.", Nivel.Error);
        }

        _sesion?.Cancel();
        _sesion = CancellationTokenSource.CreateLinkedTokenSource(_cierre.Token);
        _canal?.Dispose();
        _canal = new CanalLineas(pipe);
        _ultimo = DateTime.UtcNow;
        _primerEstado = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = _sesion.Token;
        _ = Task.Run(() => LeerBucle(_canal, token));
        _ = Task.Run(() => Latidos(_canal, token));

        JsonElement hola;
        try
        {
            hola = await Pedir(new JsonObject
            {
                ["tipo"] = "hola",
                ["app"] = _version,
                ["protocolo"] = Protocolo.Version,
                ["equipo"] = Environment.MachineName,
            }, Protocolo.Conexion).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Cortar();
            return new Resultado(false, "sin_respuesta", "Control remoto", "El otro equipo no respondió al saludo.", Nivel.Error);
        }
        catch (Exception ex)
        {
            Cortar();
            return new Resultado(false, ex.Message == "ocupado" ? "ocupado" : "sin_conexion", "Control remoto",
                ex.Message == "ocupado" ? "El otro equipo ya está siendo controlado desde otro lugar." : "Se perdió la conexión durante el saludo.",
                Nivel.Error);
        }

        Resultado r = Protocolo.ComoResultado(hola);
        if (!r.Ok)
        {
            Cortar();
            string otra = Protocolo.Texto(hola, "app") ?? "?";
            string msg = r.Codigo switch
            {
                "version_distinta" => $"Las versiones de IGP_Soft no coinciden: este equipo tiene la {_version} y el otro la {otra}. " +
                                      "Instala la misma versión en los dos equipos.",
                "protocolo_distinto" => "El otro equipo usa una versión de IGP_Soft incompatible para el control remoto.",
                "ocupado" => "El otro equipo ya está siendo controlado desde otro lugar.",
                _ => "El otro equipo rechazó la conexión.",
            };
            return new Resultado(false, r.Codigo, "Control remoto", msg, Nivel.Error);
        }
        Equipo = Protocolo.Texto(hola, "equipo") ?? "";

        Task primer = _primerEstado.Task;
        if (await Task.WhenAny(primer, Task.Delay(Protocolo.Conexion)).ConfigureAwait(false) != primer)
        {
            Cortar();
            return new Resultado(false, "sin_estado", "Control remoto", "El otro equipo no envió su estado.", Nivel.Error);
        }
        return Resultado.Bien;
    }

    private void Cortar()
    {
        _sesion?.Cancel();
        _canal?.Dispose();
        _canal = null;
        foreach (var kv in _pendientes)
            if (_pendientes.TryRemove(kv.Key, out var tcs)) tcs.TrySetException(new IOException("desconectado"));
    }

    private async Task LeerBucle(CanalLineas canal, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Linea l = await canal.LeerAsync(ct).ConfigureAwait(false);
                if (l.Fin) break;
                if (l.DemasiadoGrande || l.Texto is null) continue;
                _ultimo = DateTime.UtcNow;
                JsonElement msg;
                try { msg = JsonDocument.Parse(l.Texto).RootElement.Clone(); }
                catch { continue; }

                if (Protocolo.Numero(msg, "id") is long id && msg.TryGetProperty("ok", out _))
                {
                    if (id == 0 && Protocolo.Texto(msg, "error") == "ocupado")
                    {
                        foreach (var kv in _pendientes)
                            if (_pendientes.TryRemove(kv.Key, out var t)) t.TrySetException(new IOException("ocupado"));
                        continue;
                    }
                    if (_pendientes.TryRemove(id, out var tcs)) tcs.TrySetResult(msg);
                    continue;
                }

                switch (Protocolo.Texto(msg, "tipo"))
                {
                    case "estado":
                        EstadoPantallas? e = Protocolo.Leer<EstadoPantallas>(msg, "estado");
                        if (e is not null && (e.Seq >= Estado.Seq || e.Equipo != Estado.Equipo))
                        {
                            Estado = e;
                            Cambio?.Invoke(e);
                        }
                        _primerEstado?.TrySetResult(true);
                        break;
                    case "aviso":
                        Aviso?.Invoke(new Resultado(false, Protocolo.Texto(msg, "codigo"), Protocolo.Texto(msg, "titulo"),
                            Protocolo.Texto(msg, "mensaje"), Protocolo.Texto(msg, "nivel") switch
                            {
                                "error" => Nivel.Error,
                                "aviso" => Nivel.Aviso,
                                _ => Nivel.Info,
                            }));
                        break;
                    case "adios":
                        return;
                }
            }
        }
        catch { }
        finally
        {
            if (!ct.IsCancellationRequested) Cortar();
        }
    }

    private async Task Latidos(CanalLineas canal, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(Protocolo.Latido, ct).ConfigureAwait(false);
                await canal.EscribirAsync(new JsonObject { ["tipo"] = "latido" }, ct).ConfigureAwait(false);
            }
        }
        catch { }
    }

    private async Task Vigilar()
    {
        while (!_cierre.IsCancellationRequested)
        {
            try { await Task.Delay(1000, _cierre.Token).ConfigureAwait(false); }
            catch { return; }
            bool perdido = _canal is null || DateTime.UtcNow - _ultimo > Protocolo.SinLatido;
            if (!perdido) continue;
            Cortar();
            if (_cerrado) return;
            Reconectando?.Invoke();
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            Resultado r = Resultado.Falla("sin_conexion");
            while (reloj.Elapsed < Protocolo.Conexion && !_cierre.IsCancellationRequested)
            {
                r = await ConectarUnaVez(Protocolo.Conexion - reloj.Elapsed).ConfigureAwait(false);
                if (r.Ok || r.Codigo is "version_distinta" or "protocolo_distinto" or "ocupado") break;
                try { await Task.Delay(500, _cierre.Token).ConfigureAwait(false); }
                catch { return; }
            }
            if (r.Ok)
            {
                Reconectado?.Invoke();
                continue;
            }
            _cerrado = true;
            Desconectado?.Invoke(r.Mensaje ?? "Se perdió la conexión con el otro equipo.");
            return;
        }
    }

    private async Task<JsonElement> Pedir(JsonObject orden, TimeSpan? limite = null)
    {
        CanalLineas canal = _canal ?? throw new IOException("desconectado");
        long id = Interlocked.Increment(ref _siguiente);
        orden["id"] = id;
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendientes[id] = tcs;
        try { await canal.EscribirAsync(orden, _cierre.Token).ConfigureAwait(false); }
        catch (InvalidOperationException)
        {
            _pendientes.TryRemove(id, out _);
            throw new InvalidOperationException("mensaje_demasiado_grande");
        }
        Task fin = await Task.WhenAny(tcs.Task, Task.Delay(limite ?? Protocolo.OrdenPerdida)).ConfigureAwait(false);
        if (fin != tcs.Task)
        {
            _pendientes.TryRemove(id, out _);
            throw new TimeoutException();
        }
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task<(Resultado R, JsonElement? Datos)> Orden(string tipo, JsonObject? datos = null)
    {
        var o = datos ?? new JsonObject();
        o["tipo"] = tipo;
        Resultado r;
        JsonElement? e = null;
        try
        {
            e = await Pedir(o).ConfigureAwait(false);
            r = Protocolo.ComoResultado(e.Value);
        }
        catch (TimeoutException) { r = Resultado.Falla("sin_respuesta"); }
        catch (InvalidOperationException) { r = Resultado.Falla("mensaje_demasiado_grande"); }
        catch { r = Resultado.Falla("desconectado"); }

        string? mensaje = r.Codigo switch
        {
            "enlace_no_permitido" => "Por seguridad, en el control remoto solo se pueden abrir direcciones web (http o https), " +
                                     "archivos de la carpeta Assets del otro equipo o enlaces que ya estén en su lista.",
            "sin_respuesta" => "El otro equipo no respondió a tiempo. Revisa la conexión.",
            "desconectado" => "No hay conexión con el otro equipo.",
            "mensaje_demasiado_grande" => "La orden es demasiado grande para enviarla.",
            _ => null,
        };
        if (mensaje is not null) Aviso?.Invoke(new Resultado(false, r.Codigo, "Control remoto", mensaje, Nivel.Aviso));
        return (r, e);
    }

    private async Task<Resultado> Simple(string tipo, JsonObject? datos = null) => (await Orden(tipo, datos).ConfigureAwait(false)).R;

    private static JsonObject Datos(params (string Clave, JsonNode? Valor)[] campos)
    {
        var o = new JsonObject();
        foreach (var (k, v) in campos) o[k] = v;
        return o;
    }

    public Task<Resultado> Abrir(string monitor, int zona, LinkDef enlace) =>
        Simple("abrir", Datos(("monitor", monitor), ("zona", zona), ("enlace", Protocolo.Nodo(enlace))));

    public Task<Resultado> Cerrar(string monitor, int zona) => Simple("cerrar", Datos(("monitor", monitor), ("zona", zona)));

    public Task<Resultado> CambiarGrilla(string monitor, GridLayout grilla) =>
        Simple("grilla", Datos(("monitor", monitor), ("columnas", grilla.Cols), ("filas", grilla.Rows), ("zonas", Protocolo.Nodo(grilla.Zones.ToList()))));

    public Task<Resultado> DividirTodos(int columnas, int filas) => Simple("dividir_todos", Datos(("columnas", columnas), ("filas", filas)));

    public Task<Resultado> AlternarOmitir(string monitor) => Simple("omitir", Datos(("monitor", monitor)));

    public Task<Resultado> OmitirPrincipal(bool omitir) => Simple("omitir_principal", Datos(("valor", omitir)));

    public Task<Resultado> CerrarMonitor(string monitor) => Simple("cerrar_monitor", Datos(("monitor", monitor)));

    public Task<Resultado> IntercambiarNumeros(string a, string b) => Simple("intercambiar", Datos(("a", a), ("b", b)));

    public Task<Resultado> NumeracionOriginal() => Simple("numeracion_original");

    public Task<Resultado> Combinar(List<List<string>> grupos) => Simple("combinar", Datos(("grupos", Protocolo.Nodo(grupos))));

    public Task<Resultado> Separar(string monitor) => Simple("separar", Datos(("monitor", monitor)));

    public Task<Resultado> Ampliar(string monitor, int zona) => Simple("ampliar", Datos(("monitor", monitor), ("zona", zona)));

    public Task<Resultado> Restaurar(string? monitor) => Simple("restaurar", monitor is null ? null : Datos(("monitor", monitor)));

    public Task<Resultado> AmpliarSiguiente(string monitor, int paso) => Simple("ampliar_siguiente", Datos(("monitor", monitor), ("paso", paso)));

    public Task<Resultado> AplicarPerfil(LayoutProfile perfil) => Simple("perfil", Datos(("perfil", Protocolo.Nodo(perfil))));

    public Task<Resultado> AbrirEnOrden() => Simple("abrir_en_orden");

    public Task<Resultado> CerrarTodo() => Simple("cerrar_todo");

    public Task<Resultado> Identificar() => Simple("identificar");

    public Task<Resultado> RecargarMonitores() => Simple("recargar_monitores");

    public Task<Resultado> RecargarTodo() => Simple("recargar_todo");

    public Task<Resultado> ElegirNavegador(string nombre) => Simple("navegador", Datos(("nombre", nombre)));

    public Task<Resultado> GuardarEnlaces(List<LinkDef> enlaces) => Simple("guardar_enlaces", Datos(("enlaces", Protocolo.Nodo(enlaces))));

    public Task<Resultado> GuardarPerfiles(List<LayoutProfile> perfiles) => Simple("guardar_perfiles", Datos(("perfiles", Protocolo.Nodo(perfiles))));

    public async Task<List<VentanaAbierta>> VentanasAbiertas()
    {
        (Resultado r, JsonElement? e) = await Orden("ventanas").ConfigureAwait(false);
        return r.Ok && e is { } v ? Protocolo.Leer<List<VentanaAbierta>>(v, "ventanas") ?? new() : new();
    }

    public async Task<List<MonitorFisico>> MonitoresFisicos()
    {
        (Resultado r, JsonElement? e) = await Orden("monitores_fisicos").ConfigureAwait(false);
        return r.Ok && e is { } v ? Protocolo.Leer<List<MonitorFisico>>(v, "monitores") ?? new() : new();
    }

    public async Task<byte[]?> VistaPrevia(string monitor, int ancho)
    {
        if (_canal is null) return null;
        JsonElement e;
        try { e = await Pedir(Datos(("tipo", "vista_previa"), ("monitor", monitor), ("ancho", ancho))).ConfigureAwait(false); }
        catch { return null; }
        if (!Protocolo.ComoResultado(e).Ok) return null;
        try { return Convert.FromBase64String(Protocolo.Texto(e, "jpeg") ?? ""); }
        catch { return null; }
    }

    public void Dispose()
    {
        _cerrado = true;
        _cierre.Cancel();
        Cortar();
    }
}
