using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DocAnon.Llm;

public sealed class LoopbackOnlyException(string message) : InvalidOperationException(message);

/// <summary>
/// Talks to an Ollama server on this computer. The guard runs twice: the configured address must
/// name a loopback host, and every socket checks the IP it actually connects to, so a DNS answer
/// that points elsewhere cannot redirect the traffic. System proxies are ignored.
/// </summary>
public sealed partial class OllamaClient : IDisposable
{
    private readonly HttpClient _http;

    public Uri BaseAddress { get; }

    public OllamaClient(string baseAddress = "http://127.0.0.1:11434", TimeSpan? timeout = null)
    {
        BaseAddress = ValidateAddress(baseAddress);
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = ConnectLoopbackOnly,
        };
        _http = new HttpClient(handler) { BaseAddress = BaseAddress, Timeout = timeout ?? TimeSpan.FromMinutes(10) };
    }

    /// <summary>Accepts only http(s) URLs whose host is localhost or a loopback IP literal.</summary>
    public static Uri ValidateAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new LoopbackOnlyException($"Endereço inválido: {address}");
        string host = uri.IdnHost.Trim('[', ']');
        bool ok = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                  || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
        if (!ok) throw new LoopbackOnlyException($"O DocAnon só conversa com o Ollama neste computador (127.0.0.1, ::1 ou localhost), não com {uri.Host}.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    private static async ValueTask<Stream> ConnectLoopbackOnly(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct).ConfigureAwait(false);
        var loopback = addresses.Where(IPAddress.IsLoopback).ToList();
        if (loopback.Count == 0 || loopback.Count != addresses.Length)
            throw new LoopbackOnlyException($"{ctx.DnsEndPoint.Host} não resolve só para endereços locais; a conexão foi recusada.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(loopback[0], ctx.DnsEndPoint.Port), ct).ConfigureAwait(false);
            if (socket.RemoteEndPoint is not IPEndPoint remote || !IPAddress.IsLoopback(remote.Address))
                throw new LoopbackOnlyException("A conexão não terminou em um endereço local.");
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var r = await _http.GetAsync("api/version", ct).ConfigureAwait(false);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            return false;
        }
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var doc = await _http.GetFromJsonAsync<JsonObject>("api/tags", ct).ConfigureAwait(false);
        return doc?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList() ?? [];
    }

    /// <summary>
    /// One non-streaming chat turn. When <paramref name="schema"/> is given, Ollama constrains the
    /// answer to that JSON schema. Images go to vision models as base64 PNG.
    /// </summary>
    public async Task<string> ChatAsync(string model, string system, string user, JsonNode? schema = null, IEnumerable<byte[]>? images = null,
        int contextTokens = 8192, CancellationToken ct = default)
    {
        var userMsg = new JsonObject { ["role"] = "user", ["content"] = user };
        if (images is not null)
        {
            var arr = new JsonArray();
            foreach (var img in images) arr.Add(Convert.ToBase64String(img));
            if (arr.Count > 0) userMsg["images"] = arr;
        }
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, userMsg),
            ["options"] = new JsonObject { ["temperature"] = 0, ["num_ctx"] = contextTokens },
        };
        if (schema is not null) body["format"] = schema.DeepClone();
        // Reasoning models think before answering by default. For extraction tasks the thinking
        // makes them several times slower and can use up the reply, so it is turned off unless asked.
        if (!Think) body["think"] = false;
        var (status, raw) = await PostAsync(body, ct).ConfigureAwait(false);
        if (status == 400 && !Think && raw.Contains("think", StringComparison.OrdinalIgnoreCase))
        {
            body.Remove("think");
            (status, raw) = await PostAsync(body, ct).ConfigureAwait(false);
        }
        if (status is < 200 or > 299) throw new HttpRequestException($"Ollama respondeu {status}: {raw}");
        var node = JsonNode.Parse(raw);
        string content = node?["message"]?["content"]?.GetValue<string>() ?? "";
        return ThinkRx().Replace(content, "").Trim();
    }

    /// <summary>Whether reasoning models may think before answering. Off by default.</summary>
    public bool Think { get; set; }

    private async Task<(int Status, string Body)> PostAsync(JsonObject body, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync("api/chat", body, ct).ConfigureAwait(false);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkRx();

    public void Dispose() => _http.Dispose();
}
