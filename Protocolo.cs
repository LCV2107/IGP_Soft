using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IGP_Soft;

internal static class Protocolo
{
    public const int Version = 1;
    public const int MaxBytes = 1024 * 1024;
    private const int Bufer = 64 * 1024;
    public const string PrefijoTuberia = "IGP_Soft.Control.v1.";
    public const string ArgControlado = "--controlado";
    public const string ArgRemoto = "--remoto";
    public const string ArgTubo = "--tubo=";
    public static readonly TimeSpan Latido = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan SinLatido = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Conexion = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan OrdenPerdida = TimeSpan.FromSeconds(30);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static string TuberiaControlado(int sessionId) => PrefijoTuberia + sessionId;

    public static string TuberiaControlado() => TuberiaControlado(Process.GetCurrentProcess().SessionId);

    public static string VersionApp => Updater.Current.ToString();

    public static JsonNode? Nodo<T>(T value) => JsonSerializer.SerializeToNode(value, Json);

    public static T? Leer<T>(JsonElement e, string nombre)
    {
        foreach (JsonProperty p in e.EnumerateObject())
            if (p.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase))
                return p.Value.Deserialize<T>(Json);
        return default;
    }

    public static string? Texto(JsonElement e, string nombre)
    {
        foreach (JsonProperty p in e.EnumerateObject())
            if (p.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        return null;
    }

    public static long? Numero(JsonElement e, string nombre)
    {
        foreach (JsonProperty p in e.EnumerateObject())
            if (p.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out long v))
                return v;
        return null;
    }

    public static JsonObject Respuesta(long id, Resultado r)
    {
        var o = new JsonObject { ["id"] = id, ["ok"] = r.Ok };
        if (!r.Ok) o["error"] = r.Codigo ?? "error";
        if (r.Mensaje is not null) o["mensaje"] = r.Mensaje;
        return o;
    }

    public static Resultado ComoResultado(JsonElement e)
    {
        bool ok = e.TryGetProperty("ok", out JsonElement okv) && okv.ValueKind == JsonValueKind.True;
        if (ok) return Resultado.Bien;
        return new Resultado(false, Texto(e, "error") ?? "error", null, Texto(e, "mensaje"));
    }

    public static NamedPipeServerStream CrearServidor(string nombre, int instancias)
    {
        try
        {
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
            var sec = new PipeSecurity();
            sec.SetOwner(user);
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            sec.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            return NamedPipeServerStreamAcl.Create(nombre, PipeDirection.InOut, instancias, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, Bufer, Bufer, sec);
        }
        catch
        {
            return new NamedPipeServerStream(nombre, PipeDirection.InOut, instancias, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, Bufer, Bufer);
        }
    }
}

internal readonly record struct Linea(string? Texto, bool DemasiadoGrande, bool Fin);

internal sealed class CanalLineas : IDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _escritura = new(1, 1);
    private readonly byte[] _buffer = new byte[64 * 1024];
    private int _inicio;
    private int _fin;

    public CanalLineas(Stream stream) => _stream = stream;

    public async Task<Linea> LeerAsync(CancellationToken ct)
    {
        using var acumulado = new MemoryStream();
        bool demasiado = false;
        while (true)
        {
            if (_inicio == _fin)
            {
                int n = await _stream.ReadAsync(_buffer.AsMemory(0, _buffer.Length), ct).ConfigureAwait(false);
                if (n <= 0) return new Linea(null, false, true);
                _inicio = 0;
                _fin = n;
            }
            int nl = Array.IndexOf(_buffer, (byte)'\n', _inicio, _fin - _inicio);
            int hasta = nl >= 0 ? nl : _fin;
            int largo = hasta - _inicio;
            if (!demasiado)
            {
                if (acumulado.Length + largo > Protocolo.MaxBytes) demasiado = true;
                else acumulado.Write(_buffer, _inicio, largo);
            }
            _inicio = nl >= 0 ? nl + 1 : _fin;
            if (nl < 0) continue;
            if (demasiado) return new Linea(null, true, false);
            string texto = Encoding.UTF8.GetString(acumulado.GetBuffer(), 0, (int)acumulado.Length).TrimEnd('\r');
            return new Linea(texto, false, false);
        }
    }

    public async Task EscribirAsync(JsonNode mensaje, CancellationToken ct)
    {
        byte[] datos = Encoding.UTF8.GetBytes(mensaje.ToJsonString(Protocolo.Json) + "\n");
        if (datos.Length > Protocolo.MaxBytes) throw new InvalidOperationException("mensaje_demasiado_grande");
        await _escritura.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(datos, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { _escritura.Release(); }
    }

    public void Dispose()
    {
        try { _stream.Dispose(); }
        catch { }
        _escritura.Dispose();
    }
}

internal sealed class ListaBlanca
{
    private readonly Func<string> _assets;
    private readonly Func<IEnumerable<string>> _conocidos;

    public ListaBlanca(Func<string> assets, Func<IEnumerable<string>> conocidos)
    {
        _assets = assets;
        _conocidos = conocidos;
    }

    public static ListaBlanca DelEquipo(IPantallas pantallas) => new(() => AssetStore.Root, () => UrlsDelEquipo(pantallas));

    public static IEnumerable<string> UrlsDelEquipo(IPantallas pantallas) =>
        LinkStore.Links.Select(l => l.Url)
            .Concat(ProfileStore.Items.SelectMany(p => p.Monitors).SelectMany(m => m.Links).Select(z => z.Url))
            .Concat(pantallas.Estado.Monitores.SelectMany(m => m.Zonas).Select(z => z.Url).OfType<string>());

    public bool Permite(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (Permisos.EsSeguro(url, _assets())) return true;
        return _conocidos().Any(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase));
    }

    public bool Permite(LayoutProfile perfil) =>
        perfil.Monitors.SelectMany(m => m.Links).All(z => Permite(z.Url));
}
