using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SupportAtos.Api.Options;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;
using SupportAtos.Infrastructure.Persistence;

namespace SupportAtos.Qa.Tests;

// Copia o conteúdo durante SendAsync: o dispatcher descarta request/response ao retornar.
// Nenhum socket é aberto e nenhum header secreto é incluído nas evidências.
public record CapturedWebhook(string Method, string Destination, string? ContentType,
    bool ApiKeyValid, int ApiKeyHeaderCount, string Body, int ResponseStatus);

public sealed class SlaRf06Handler(string expectedKey) : HttpMessageHandler
{
    private readonly List<CapturedWebhook> _requests = [];
    private readonly Queue<HttpStatusCode> _responses = new();
    public CapturedWebhook[] Requests => _requests.ToArray();

    public void Respond(params HttpStatusCode[] statuses)
    {
        _responses.Clear();
        foreach (var status in statuses) _responses.Enqueue(status);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var keys = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.ToArray() : [];
        var status = _responses.Count > 0 ? _responses.Dequeue() : HttpStatusCode.Accepted;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Add(new(request.Method.Method, request.RequestUri!.ToString(),
            request.Content?.Headers.ContentType?.MediaType, keys.Length == 1 && keys[0] == expectedKey,
            keys.Length, body, (int)status));
        return new HttpResponseMessage(status);
    }
}

public sealed class SlaRf06Application(string connection) : WebApplicationFactory<Program>
{
    public const string Destination = "https://sla-rf06.example.invalid/events";
    private const string Issuer = "sla-rf06-qa";
    private const string Audience = "supportatos-sla-rf06";
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly string _portalKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _webhookKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public SlaRf06Handler Capture { get; private set; } = null!;
    public QaDatabaseErrors Errors { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connection);
        builder.UseSetting("ApiKeys:Portal:PrimaryHash", ApiKeyHasher.Hash(_portalKey));
        builder.UseSetting("ApiKeys:Portal:SecondaryHash", "");
        builder.UseSetting("ApiKeys:Automacao:PrimaryHash", ApiKeyHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
        builder.UseSetting("ApiKeys:Automacao:SecondaryHash", "");
        builder.UseSetting("Sso:Issuer", Issuer);
        builder.UseSetting("Sso:Audience", Audience);
        builder.UseSetting("Sso:PublicKeyPem", _rsa.ExportSubjectPublicKeyInfoPem());
        builder.UseSetting("Sso:JwksUrl", "");
        builder.UseSetting("Webhooks:PortalUrl", Destination);
        builder.UseSetting("Webhooks:ApiKey", _webhookKey);
        builder.UseSetting("Webhooks:MaxAttempts", "3");
        builder.UseSetting("Webhooks:TimeoutSeconds", "2");
        builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddProvider(Errors); });
        builder.ConfigureServices(services =>
        {
            Capture = new SlaRf06Handler(_webhookKey);
            // Abrange todos os clientes da factory, inclusive o cliente nomeado "webhooks".
            // Substitui apenas o transporte: mantém HttpClient, serialização e dispatcher reais.
            services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = Capture));
        });
    }

    public void Authenticate(HttpRequestMessage request, bool agent)
    {
        if (!agent) { request.Headers.Add("X-Api-Key", _portalKey); return; }
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, [
            new Claim("sub", "62001"), new Claim("name", "Agente QA SLA"),
            new Claim("email", "agente-sla@qa.example.invalid"), new Claim("grupo_empresa_id", "72001"),
            new Claim("grupo_empresa_nome", "Equipe QA SLA"), new Claim("role", "Agente")
        ], now.AddSeconds(-5), now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    public string Sanitize(string text) => text.Replace(_portalKey, "[SEGREDO]").Replace(_webhookKey, "[SEGREDO]");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _rsa.Dispose();
    }
}

public sealed class SlaRf06Case(QaPostgres postgres)
{
    private string? _database;
    private HttpClient? _client;
    public SlaRf06Application? Application { get; private set; }
    public SlaRf06Handler Capture => Application!.Capture;
    public List<object> HttpEvidence { get; } = [];
    public List<object> SqlEvidence { get; } = [];
    public string Database => _database ?? "não criado";
    private string Connection => postgres.Connection(_database!);

    public async Task Start()
    {
        if (postgres.InitializationError is not null) throw new InvalidOperationException(postgres.InitializationError);
        _database = await postgres.CreateDatabase();
        await using (var db = new SupportAtosDbContext(new DbContextOptionsBuilder<SupportAtosDbContext>().UseNpgsql(Connection).Options))
        {
            await db.Database.MigrateAsync();
            if (!(await db.Database.GetAppliedMigrationsAsync()).Any()) throw new InvalidOperationException("Migrations não aplicadas.");
            db.SlaCategorias.Add(SlaCategoria.Criar("QA-SLA", "Integração", Prioridade.Alta, 60, 480));
            await db.SaveChangesAsync();
        }
        Application = new SlaRf06Application(Connection);
        _client = Application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = Application.Services.CreateScope();
        var actual = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().Database.GetConnectionString();
        if (new NpgsqlConnectionStringBuilder(actual).ConnectionString != new NpgsqlConnectionStringBuilder(Connection).ConnectionString)
            throw new InvalidOperationException("Host não está usando o banco exclusivo.");
    }

    public Task<(int Status, JsonElement Body)> Open(string category = "Integração") => Send(HttpMethod.Post, "/api/chamados", new
    {
        usuario = new { id = 61001, nome = "Cliente QA SLA", email = "cliente-sla@qa.example.invalid" },
        grupoEmpresa = new { id = 71001, nome = "Grupo QA SLA" },
        produto = "QA-SLA", categoria = category, titulo = "Integração SLA RF06",
        descricao = "Descrição sintética", mensagemInicial = "Mensagem sintética"
    });

    public Task<(int Status, JsonElement Body)> Status(string code, string status) =>
        Send(HttpMethod.Patch, $"/api/chamados/{code}/status", new { novoStatus = status }, agent: true);

    public Task<(int Status, JsonElement Body)> Interact(string code) =>
        Send(HttpMethod.Post, $"/api/chamados/{code}/interacoes", new { mensagem = "Resposta pública de QA", tipo = "Publica" }, agent: true);

    private async Task<(int Status, JsonElement Body)> Send(HttpMethod method, string path, object payload, bool agent = false)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload) };
        Application!.Authenticate(request, agent);
        using var response = await _client!.SendAsync(request);
        var raw = Application.Sanitize(await response.Content.ReadAsStringAsync());
        HttpEvidence.Add(new { Method = method.Method, Path = path, Payload = payload,
            Authentication = agent ? "JWT RS256 efêmero" : "API Key efêmera", Status = (int)response.StatusCode,
            Response = raw, SqlErrors = Application.Errors.Drain() });
        using var json = JsonDocument.Parse(raw);
        return ((int)response.StatusCode, json.RootElement.Clone());
    }

    // Npgsql em conexão independente do DbContext da requisição; consultas somente a tabelas fixas.
    public async Task<Dictionary<string, JsonElement>> Snapshot(string label)
    {
        await using var c = new NpgsqlConnection(Connection);
        await c.OpenAsync();
        var snapshot = new Dictionary<string, JsonElement>();
        foreach (var table in new[] { "sla_config", "chamado", "interacao", "usuario", "grupo_empresa", "provisionamento_audit" })
        {
            await using var command = new NpgsqlCommand($"SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY t.id),'[]'::jsonb)::text FROM {table} t", c);
            using var json = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            snapshot.Add(table, json.RootElement.Clone());
        }
        SqlEvidence.Add(new { Label = label, Query = "SELECT jsonb_agg(to_jsonb(t) ORDER BY id) por tabela", Tables = snapshot });
        return snapshot;
    }

    public async Task Stop()
    {
        try { _client?.Dispose(); if (Application is not null) await Application.DisposeAsync(); }
        finally { if (_database is not null) await postgres.DropDatabase(_database); }
    }

    public string Sanitize(Exception exception) => Application?.Sanitize(postgres.Sanitize(exception)) ?? postgres.Sanitize(exception);
}
