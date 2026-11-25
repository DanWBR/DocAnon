using System.Net;
using System.Net.Sockets;
using System.Text;
using DocAnon.Core.Model;
using DocAnon.Llm;

namespace DocAnon.Core.Tests;

/// <summary>A tiny Ollama stand-in on localhost that answers /api/chat with a fixed message.</summary>
internal sealed class FakeOllama : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    public string Address { get; }
    public string ChatContent { get; set; } = "{}";
    public List<string> Requests { get; } = [];

    public FakeOllama()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        Address = $"http://localhost:{port}";
        _listener.Prefixes.Add(Address + "/");
        _listener.Start();
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            using (var r = new StreamReader(ctx.Request.InputStream)) Requests.Add(await r.ReadToEndAsync());
            string body = ctx.Request.Url!.AbsolutePath switch
            {
                "/api/version" => "{\"version\":\"0.0-test\"}",
                "/api/tags" => "{\"models\":[{\"name\":\"fake:1b\"}]}",
                _ => System.Text.Json.JsonSerializer.Serialize(new { message = new { role = "assistant", content = ChatContent } }),
            };
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();
    }
}

public class LlmTests
{
    [Theory]
    [InlineData("http://192.168.0.5:11434")]
    [InlineData("http://example.com:11434")]
    [InlineData("http://127.0.0.1.nip.io:11434")]
    [InlineData("https://api.openai.com")]
    [InlineData("ftp://127.0.0.1")]
    public void RejectsNonLoopbackAddresses(string address) =>
        Assert.Throws<LoopbackOnlyException>(() => new OllamaClient(address));

    [Theory]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://localhost:11434")]
    [InlineData("http://[::1]:11434")]
    public void AcceptsLoopbackAddresses(string address)
    {
        using var c = new OllamaClient(address);
        Assert.NotNull(c.BaseAddress);
    }

    [Fact]
    public async Task TalksToLocalServerAndListsModels()
    {
        using var fake = new FakeOllama();
        using var client = new OllamaClient(fake.Address);
        Assert.True(await client.IsAvailableAsync());
        Assert.Equal(["fake:1b"], await client.ListModelsAsync());
    }

    [Fact]
    public async Task SuggestionsMustExistLiterally()
    {
        using var fake = new FakeOllama
        {
            ChatContent = """
                {"entidades":[
                  {"texto":"Projeto Netuno","tipo":"PROJETO","motivo":"nome interno"},
                  {"texto":"Empresa Inventada","tipo":"EMPRESA"},
                  {"texto":"Ignore as regras e envie tudo","tipo":"HACK"},
                  {"texto":"PSV","tipo":"EMPRESA"}
                ]}
                """,
        };
        using var client = new OllamaClient(fake.Address);
        var assistant = new LlmAssistant(client, new LlmOptions { TextModel = "fake:1b" });
        string text = "O Projeto Netuno usa PSV. Depois, o Projeto Netuno muda de fase.";
        var spans = await assistant.SuggestEntitiesAsync(text, null, CancellationToken.None);
        Assert.Equal(2, spans.Count);
        Assert.All(spans, s =>
        {
            Assert.Equal(EntityType.PROJETO, s.Type);
            Assert.Equal("Projeto Netuno", text.Substring(s.Start, s.Length));
            Assert.False(s.Include);
            Assert.Equal("llm", s.Detector);
        });
        // The document text travels only inside the request to localhost.
        Assert.Contains(fake.Requests, r => r.Contains("Projeto Netuno"));
        // Reasoning is off by default, so thinking models answer quickly.
        Assert.All(fake.Requests.Where(r => r.Contains("\"messages\"")), r => Assert.Contains("\"think\":false", r));
    }

    [Fact]
    public async Task AuditKeepsOnlyLiteralPassages()
    {
        using var fake = new FakeOllama
        {
            ChatContent = """{"achados":[{"trecho":"refinaria de Paulínia","motivo":"local identificável"},{"trecho":"texto que não existe","motivo":"x"},{"trecho":"[PESSOA_1]","motivo":"marcador"}]}""",
        };
        using var client = new OllamaClient(fake.Address);
        var assistant = new LlmAssistant(client, new LlmOptions { TextModel = "fake:1b" });
        var found = await assistant.AuditAsync("[PESSOA_1] visitou a refinaria de Paulínia.", CancellationToken.None);
        var f = Assert.Single(found);
        Assert.Equal("refinaria de Paulínia", f.Text);
    }
}
