using System.IO.Pipes;
using System.Threading.Channels;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IGP_Soft;

internal sealed class ServidorControl : IDisposable
{
    private readonly IPantallas _pantallas;
    private readonly string _nombre;
    private readonly Func<Func<Task<JsonNode>>, Task<JsonNode>> _enUi;
    private readonly ListaBlanca _lista;
    private readonly string _version;
    private readonly RecepcionArchivos? _archivos;
    private readonly Func<IEnumerable<string>>? _usados;
    private readonly CancellationTokenSource _cierre = new();
    private readonly object _gate = new();
    private Sesion? _activa;
    private Task? _bucle;

    public ServidorControl(IPantallas pantallas, string nombre, Func<Func<Task<JsonNode>>, Task<JsonNode>> enUi,
        ListaBlanca lista, string version, RecepcionArchivos? archivos = null, Func<IEnumerable<string>>? usados = null)
    {
        _pantallas = pantallas;
        _nombre = nombre;
        _enUi = enUi;
        _lista = lista;
        _version = version;
        _archivos = archivos;
        _usados = usados;
        _pantallas.Cambio += OnCambio;
        _pantallas.Aviso += OnAviso;
    }

    public static ServidorControl ParaEsteEquipo(IPantallas pantallas, SynchronizationContext ui) =>
        new(pantallas, Protocolo.TuberiaControlado(), f =>
        {
            var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Post(async _ =>
            {
                try { tcs.SetResult(await f()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task;
        }, ListaBlanca.DelEquipo(pantallas), Protocolo.VersionApp, RecepcionArchivos.DelEquipo(), () => ListaBlanca.UrlsDelEquipo(pantallas));

    public event Action<string>? Conectado;
    public event Action? Desconectado;

    public string Nombre => _nombre;
    public bool HayCliente { get { lock (_gate) return _activa is not null; } }

    public void Iniciar() => _bucle = Task.Run(Escuchar);

    private async Task Escuchar()
    {
        while (!_cierre.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try { pipe = Protocolo.CrearServidor(_nombre, 2); }
            catch (Exception ex)
            {
                Diagnostico.Log($"Control: no se pudo crear la tubería {_nombre}: {ex.Message}");
                try { await Task.Delay(2000, _cierre.Token).ConfigureAwait(false); }
                catch { return; }
                continue;
            }
            try { await pipe.WaitForConnectionAsync(_cierre.Token).ConfigureAwait(false); }
            catch
            {
                pipe.Dispose();
                if (_cierre.IsCancellationRequested) return;
                continue;
            }

            var sesion = new Sesion(this, new CanalLineas(pipe));
            bool ocupado;
            lock (_gate)
            {
                ocupado = _activa is not null;
                if (!ocupado) _activa = sesion;
            }
            if (ocupado) _ = RechazarOcupado(sesion);
            else _ = AtenderAsync(sesion);
        }
    }

    private static async Task RechazarOcupado(Sesion s)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await s.Canal.EscribirAsync(new JsonObject { ["id"] = 0, ["ok"] = false, ["error"] = "ocupado" }, cts.Token).ConfigureAwait(false);
        }
        catch { }
        s.Canal.Dispose();
    }

    private async Task AtenderAsync(Sesion s)
    {
        string? equipo = null;
        try
        {
            using var latido = new CancellationTokenSource();
            _ = EnviarLatidos(s, latido.Token);
            while (!_cierre.IsCancellationRequested)
            {
                Task<Linea> leer = s.Canal.LeerAsync(_cierre.Token);
                Task espera = Task.Delay(Protocolo.SinLatido, _cierre.Token);
                if (await Task.WhenAny(leer, espera).ConfigureAwait(false) != leer)
                {
                    Diagnostico.Log("Control: sin latido del cliente, se corta la conexión");
                    break;
                }
                Linea l = await leer.ConfigureAwait(false);
                if (l.Fin) break;
                if (l.DemasiadoGrande)
                {
                    await s.Enviar(new JsonObject { ["id"] = 0, ["ok"] = false, ["error"] = "mensaje_demasiado_grande" }).ConfigureAwait(false);
                    continue;
                }
                JsonElement msg;
                try { msg = JsonDocument.Parse(l.Texto!).RootElement.Clone(); }
                catch
                {
                    await s.Enviar(new JsonObject { ["id"] = 0, ["ok"] = false, ["error"] = "mensaje_invalido" }).ConfigureAwait(false);
                    continue;
                }
                long id = Protocolo.Numero(msg, "id") ?? 0;
                string tipo = Protocolo.Texto(msg, "tipo") ?? "";

                if (tipo == "latido") continue;
                if (tipo == "hola")
                {
                    JsonObject r = Saludo(id, msg);
                    await s.Enviar(r).ConfigureAwait(false);
                    if (r["ok"]?.GetValue<bool>() != true) break;
                    equipo = Protocolo.Texto(msg, "equipo") ?? "otro equipo";
                    s.Saludado = true;
                    Conectado?.Invoke(equipo);
                    await s.Enviar(await _enUi(() => Task.FromResult<JsonNode>(MensajeEstado(_pantallas.Estado))).ConfigureAwait(false)).ConfigureAwait(false);
                    continue;
                }
                if (!s.Saludado)
                {
                    await s.Enviar(Protocolo.Respuesta(id, Resultado.Falla("falta_saludo"))).ConfigureAwait(false);
                    break;
                }
                JsonNode respuesta;
                try
                {
                    respuesta = OrdenesArchivo.Contains(tipo)
                        ? await EjecutarArchivo(id, tipo, msg).ConfigureAwait(false)
                        : await _enUi(() => Ejecutar(id, tipo, msg)).ConfigureAwait(false);
                }
                catch (Exception ex) { respuesta = Protocolo.Respuesta(id, Resultado.Falla("error_interno", ex.Message)); }
                await s.Enviar(respuesta).ConfigureAwait(false);
            }
            latido.Cancel();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        catch (Exception ex) { Diagnostico.Log($"Control: error en la sesión: {ex.Message}"); }
        finally
        {
            _archivos?.Pausar();
            await s.TerminarAsync().ConfigureAwait(false);
            s.Canal.Dispose();
            lock (_gate) if (_activa == s) _activa = null;
            if (equipo is not null) Desconectado?.Invoke();
        }
    }

    private async Task EnviarLatidos(Sesion s, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(Protocolo.Latido, ct).ConfigureAwait(false);
                await s.Enviar(new JsonObject { ["tipo"] = "latido" }).ConfigureAwait(false);
            }
        }
        catch { }
    }

    private JsonObject Saludo(long id, JsonElement msg)
    {
        string app = Protocolo.Texto(msg, "app") ?? "";
        long protocolo = Protocolo.Numero(msg, "protocolo") ?? 0;
        var r = new JsonObject
        {
            ["id"] = id,
            ["app"] = _version,
            ["protocolo"] = Protocolo.Version,
            ["equipo"] = Environment.MachineName,
        };
        if (protocolo != Protocolo.Version)
        {
            r["ok"] = false;
            r["error"] = "protocolo_distinto";
        }
        else if (!string.Equals(app, _version, StringComparison.OrdinalIgnoreCase))
        {
            r["ok"] = false;
            r["error"] = "version_distinta";
        }
        else r["ok"] = true;
        return r;
    }

    private static JsonObject MensajeEstado(EstadoPantallas e) =>
        new() { ["tipo"] = "estado", ["estado"] = Protocolo.Nodo(e) };

    private async Task<JsonNode> Ejecutar(long id, string tipo, JsonElement m)
    {
        string Mon() => Protocolo.Texto(m, "monitor") ?? "";
        int Zona() => (int)(Protocolo.Numero(m, "zona") ?? -1);
        JsonObject R(Resultado r) => Protocolo.Respuesta(id, r);

        switch (tipo)
        {
            case "abrir":
            {
                LinkDef? enlace = Protocolo.Leer<LinkDef>(m, "enlace");
                if (enlace is null || string.IsNullOrWhiteSpace(enlace.Url)) return R(Resultado.Falla("enlace_no_valido"));
                if (!_lista.Permite(enlace.Url)) return R(Resultado.Falla("enlace_no_permitido"));
                return R(await _pantallas.Abrir(Mon(), Zona(), enlace));
            }
            case "cerrar": return R(await _pantallas.Cerrar(Mon(), Zona()));
            case "grilla":
            {
                int cols = (int)(Protocolo.Numero(m, "columnas") ?? 0), rows = (int)(Protocolo.Numero(m, "filas") ?? 0);
                List<ZoneSpec>? zonas = Protocolo.Leer<List<ZoneSpec>>(m, "zonas");
                GridLayout? g = zonas is null ? (cols > 0 && rows > 0 ? GridLayout.Uniform(cols, rows) : null) : GridLayout.TryCreate(cols, rows, zonas);
                return g is null ? R(Resultado.Falla("grilla_no_valida")) : R(await _pantallas.CambiarGrilla(Mon(), g));
            }
            case "dividir_todos":
                return R(await _pantallas.DividirTodos((int)(Protocolo.Numero(m, "columnas") ?? 1), (int)(Protocolo.Numero(m, "filas") ?? 1)));
            case "omitir": return R(await _pantallas.AlternarOmitir(Mon()));
            case "omitir_principal": return R(await _pantallas.OmitirPrincipal(Protocolo.Leer<bool>(m, "valor")));
            case "cerrar_monitor": return R(await _pantallas.CerrarMonitor(Mon()));
            case "intercambiar": return R(await _pantallas.IntercambiarNumeros(Protocolo.Texto(m, "a") ?? "", Protocolo.Texto(m, "b") ?? ""));
            case "numeracion_original": return R(await _pantallas.NumeracionOriginal());
            case "combinar": return R(await _pantallas.Combinar(Protocolo.Leer<List<List<string>>>(m, "grupos") ?? new()));
            case "separar": return R(await _pantallas.Separar(Mon()));
            case "ampliar": return R(await _pantallas.Ampliar(Mon(), Zona()));
            case "restaurar": return R(await _pantallas.Restaurar(Protocolo.Texto(m, "monitor")));
            case "ampliar_siguiente": return R(await _pantallas.AmpliarSiguiente(Mon(), (int)(Protocolo.Numero(m, "paso") ?? 1)));
            case "perfil":
            {
                LayoutProfile? p = Protocolo.Leer<LayoutProfile>(m, "perfil");
                if (p is null) return R(Resultado.Falla("perfil_no_valido"));
                if (!_lista.Permite(p)) return R(Resultado.Falla("enlace_no_permitido"));
                return R(await _pantallas.AplicarPerfil(p));
            }
            case "abrir_en_orden": return R(await _pantallas.AbrirEnOrden());
            case "cerrar_todo": return R(await _pantallas.CerrarTodo());
            case "identificar": return R(await _pantallas.Identificar());
            case "recargar_monitores": return R(await _pantallas.RecargarMonitores());
            case "recargar_todo": return R(await _pantallas.RecargarTodo());
            case "navegador": return R(await _pantallas.ElegirNavegador(Protocolo.Texto(m, "nombre") ?? ""));
            case "guardar_enlaces":
            {
                List<LinkDef> enlaces = Protocolo.Leer<List<LinkDef>>(m, "enlaces") ?? new();
                if (enlaces.Any(e => !_lista.Permite(e.Url))) return R(Resultado.Falla("enlace_no_permitido"));
                return R(await _pantallas.GuardarEnlaces(enlaces));
            }
            case "guardar_perfiles":
            {
                List<LayoutProfile> perfiles = Protocolo.Leer<List<LayoutProfile>>(m, "perfiles") ?? new();
                if (perfiles.Any(p => !_lista.Permite(p))) return R(Resultado.Falla("enlace_no_permitido"));
                return R(await _pantallas.GuardarPerfiles(perfiles));
            }
            case "ventanas":
            {
                JsonObject r = R(Resultado.Bien);
                r["ventanas"] = Protocolo.Nodo(await _pantallas.VentanasAbiertas());
                return r;
            }
            case "monitores_fisicos":
            {
                JsonObject r = R(Resultado.Bien);
                r["monitores"] = Protocolo.Nodo(await _pantallas.MonitoresFisicos());
                return r;
            }
            case "vista_previa":
            {
                int ancho = Math.Clamp((int)(Protocolo.Numero(m, "ancho") ?? 240), 80, 640);
                byte[]? jpeg = await _pantallas.VistaPrevia(Mon(), ancho);
                if (jpeg is null) return R(Resultado.Falla("sin_imagen"));
                JsonObject r = R(Resultado.Bien);
                r["jpeg"] = Convert.ToBase64String(jpeg);
                return r;
            }
            default: return R(Resultado.Falla("orden_desconocida"));
        }
    }

    private static readonly HashSet<string> OrdenesArchivo = new()
    {
        "tengo_archivo", "subir_inicio", "subir_trozo", "subir_fin", "subir_cancelar", "assets", "borrar_asset", "miniatura",
    };

    private async Task<JsonNode> EjecutarArchivo(long id, string tipo, JsonElement m)
    {
        if (_archivos is not { } a) return Protocolo.Respuesta(id, Resultado.Falla("orden_desconocida"));
        return tipo switch
        {
            "tengo_archivo" => await Task.Run(() => a.Tengo(id, m)).ConfigureAwait(false),
            "subir_inicio" => await Task.Run(() => a.Inicio(id, m)).ConfigureAwait(false),
            "subir_trozo" => a.RecibirTrozo(id, m),
            "subir_fin" => await Task.Run(() => a.Fin(id, m)).ConfigureAwait(false),
            "subir_cancelar" => a.Cancelar(id, m),
            "assets" => a.Lista(id, await Usados().ConfigureAwait(false)),
            "borrar_asset" => a.BorrarArchivo(id, Protocolo.Texto(m, "url"), await Usados().ConfigureAwait(false)),
            "miniatura" => await a.Miniatura(id, m).ConfigureAwait(false),
            _ => Protocolo.Respuesta(id, Resultado.Falla("orden_desconocida")),
        };
    }

    private async Task<IReadOnlyCollection<string>> Usados()
    {
        if (_usados is null) return Array.Empty<string>();
        JsonNode n = await _enUi(() => Task.FromResult<JsonNode>(new JsonArray(_usados().Select(u => (JsonNode?)u).ToArray()))).ConfigureAwait(false);
        return n.AsArray().Select(x => x?.GetValue<string>() ?? "").ToList();
    }

    private Sesion? Activa { get { lock (_gate) return _activa; } }

    private EstadoPantallas? _pendiente;
    private bool _programado;

    private void OnCambio(EstadoPantallas e)
    {
        if (Activa is not { Saludado: true } s) return;
        lock (_gate)
        {
            _pendiente = e;
            if (_programado) return;
            _programado = true;
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(60).ConfigureAwait(false);
            EstadoPantallas? enviar;
            lock (_gate)
            {
                enviar = _pendiente;
                _pendiente = null;
                _programado = false;
            }
            if (enviar is not null) await s.Enviar(MensajeEstado(enviar)).ConfigureAwait(false);
        });
    }

    private void OnAviso(Resultado r)
    {
        if (Activa is not { Saludado: true } s || r.Mensaje is null) return;
        _ = s.Enviar(new JsonObject
        {
            ["tipo"] = "aviso",
            ["codigo"] = r.Codigo,
            ["titulo"] = r.Titulo,
            ["mensaje"] = r.Mensaje,
            ["nivel"] = r.Nivel.ToString().ToLowerInvariant(),
        });
    }

    public void Dispose()
    {
        _pantallas.Cambio -= OnCambio;
        _pantallas.Aviso -= OnAviso;
        Sesion? s = Activa;
        if (s is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                s.Canal.EscribirAsync(new JsonObject { ["tipo"] = "adios" }, cts.Token).Wait(1000);
            }
            catch { }
        }
        _cierre.Cancel();
        s?.Canal.Dispose();
        try { _bucle?.Wait(2000); }
        catch { }
        _archivos?.Dispose();
    }

    private sealed class Sesion
    {
        private readonly ServidorControl _owner;
        private readonly Channel<JsonNode> _salida = Channel.CreateUnbounded<JsonNode>(new UnboundedChannelOptions { SingleReader = true });

        public Sesion(ServidorControl owner, CanalLineas canal)
        {
            _owner = owner;
            Canal = canal;
            _escritor = Task.Run(Escribir);
        }

        private readonly Task _escritor;

        public CanalLineas Canal { get; }
        public bool Saludado { get; set; }

        public Task Enviar(JsonNode mensaje)
        {
            _salida.Writer.TryWrite(mensaje);
            return Task.CompletedTask;
        }

        public async Task TerminarAsync()
        {
            _salida.Writer.TryComplete();
            try { await _escritor.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { }
        }

        private async Task Escribir()
        {
            try
            {
                await foreach (JsonNode m in _salida.Reader.ReadAllAsync(_owner._cierre.Token).ConfigureAwait(false))
                {
                    try { await Canal.EscribirAsync(m, _owner._cierre.Token).ConfigureAwait(false); }
                    catch (InvalidOperationException) { Diagnostico.Log("Control: un mensaje superó 1 MB y no se envió"); }
                }
            }
            catch { }
        }
    }
}
