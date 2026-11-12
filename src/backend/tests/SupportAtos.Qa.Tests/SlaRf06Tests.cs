using System.Net;
using System.Text.Json;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SupportAtos.Qa.Tests;

[Collection("QA isolado")]
public sealed class SlaRf06Tests(QaPostgres postgres, ITestOutputHelper output)
{
    [Fact(DisplayName = "SLA-01 abertura calcula e persiste prazos"), Trait("Group", "SLA-RF06")]
    public Task Sla01() => Run("SLA-01", "RF07; TR v3 §2.2", async c =>
    {
        var response = await c.Open();
        Assert.Equal(201, response.Status);
        var snapshot = await c.Snapshot("após abertura");
        var ticket = Assert.Single(snapshot["chamado"].EnumerateArray());
        var sla = Assert.Single(snapshot["sla_config"].EnumerateArray());
        Assert.Equal(sla.GetProperty("id").GetInt32(), ticket.GetProperty("sla_config_id").GetInt32());
        Assert.Equal("Alta", ticket.GetProperty("prioridade").GetString());
        var created = Date(ticket, "criado_em");
        Assert.Equal(created.AddMinutes(sla.GetProperty("tempo_resposta_min").GetInt32()), Date(ticket, "sla_prazo_resposta"));
        Assert.Equal(created.AddMinutes(sla.GetProperty("tempo_resolucao_min").GetInt32()), Date(ticket, "sla_prazo_resolucao"));
        AssertDate(response.Body.GetProperty("slaPrazoResposta").GetDateTime(), Date(ticket, "sla_prazo_resposta"));
        AssertDate(response.Body.GetProperty("slaPrazo").GetDateTime(), Date(ticket, "sla_prazo_resolucao"));
        Assert.Single(snapshot["interacao"].EnumerateArray());
    });

    [Fact(DisplayName = "SLA-02 ausência de SLA não cria chamado ou interação"), Trait("Group", "SLA-RF06")]
    public Task Sla02() => Run("SLA-02", "RF07; regra de validação implementada", async c =>
    {
        var before = await c.Snapshot("antes da rejeição; identidade nova ausente");
        var response = await c.Open("Sem configuração");
        var after = await c.Snapshot("após rejeição; efeitos em usuário/grupo registrados separadamente");
        Assert.Equal(400, response.Status);
        Assert.Contains("Nenhuma categoria de SLA", response.Body.GetRawText());
        Assert.Equal(before["chamado"].GetRawText(), after["chamado"].GetRawText());
        Assert.Equal(before["interacao"].GetRawText(), after["interacao"].GetRawText());
        Assert.Empty(c.Capture.Requests);
        output.WriteLine($"SLA-02 efeitos de provisionamento: usuários {before["usuario"].GetArrayLength()} → {after["usuario"].GetArrayLength()}; grupos {before["grupo_empresa"].GetArrayLength()} → {after["grupo_empresa"].GetArrayLength()}. Não se exige atomicidade dessas entidades.");
    });

    [Fact(DisplayName = "SLA-03 mudança de status preserva prazos persistidos"), Trait("Group", "SLA-RF06")]
    public Task Sla03() => Run("SLA-03", "RF07 e RF04; preservação implementada", async c =>
    {
        var code = await Open(c);
        var before = Ticket(await c.Snapshot("antes do status"));
        var response = await c.Status(code, "EmAndamento");
        Assert.Equal(200, response.Status);
        Assert.Equal("EmAndamento", response.Body.GetProperty("status").GetString());
        var after = Ticket(await c.Snapshot("após status"));
        Assert.Equal("EmAndamento", after.GetProperty("status").GetString());
        Preserved(before, after);
    });

    [Fact(DisplayName = "SLA-04 aguardando cliente registra e limpa início"), Trait("Group", "SLA-RF06")]
    public Task Sla04() => Run("SLA-04", "RF07 e RF04; marcador implementado", async c =>
    {
        var code = await Open(c);
        var before = Ticket(await c.Snapshot("antes de aguardar"));
        Assert.Equal(JsonValueKind.Null, before.GetProperty("aguardando_desde").ValueKind);
        var entering = await c.Status(code, "AguardandoCliente");
        Assert.Equal(200, entering.Status);
        var waiting = Ticket(await c.Snapshot("aguardando cliente"));
        Assert.Equal("AguardandoCliente", waiting.GetProperty("status").GetString());
        Assert.Equal(Date(waiting, "atualizado_em"), Date(waiting, "aguardando_desde"));
        AssertDate(entering.Body.GetProperty("aguardandoDesde").GetDateTime(), Date(waiting, "aguardando_desde"));
        var leaving = await c.Status(code, "EmAndamento");
        Assert.Equal(200, leaving.Status);
        var after = Ticket(await c.Snapshot("após sair de espera"));
        Assert.Equal("EmAndamento", after.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("aguardando_desde").ValueKind);
        Assert.Equal(JsonValueKind.Null, leaving.Body.GetProperty("aguardandoDesde").ValueKind);
        Preserved(before, waiting);
        Preserved(before, after);
    });

    [Fact(DisplayName = "RF06-01 criação envia identificação correta"), Trait("Group", "SLA-RF06")]
    public Task Rf0601() => Run("RF06-01", "RF06; TR v3 §§2.3/6.2", async c =>
    {
        var code = await Open(c);
        var snapshot = await c.Snapshot("criação e webhook");
        var ticket = Ticket(snapshot);
        Event(Assert.Single(c.Capture.Requests), "chamado.criado", code, ticket);
        Assert.Equal(61001, ticket.GetProperty("usuario_id").GetInt32());
        Assert.Equal(71001, ticket.GetProperty("grupo_empresa_id").GetInt32());
        Assert.Single(snapshot["interacao"].EnumerateArray());
    });

    [Fact(DisplayName = "RF06-02 interação envia atualizado"), Trait("Group", "SLA-RF06")]
    public Task Rf0602() => Run("RF06-02", "RF06; atualização pública", async c =>
    {
        var code = await Open(c);
        var response = await c.Interact(code);
        Assert.Equal(201, response.Status);
        var snapshot = await c.Snapshot("após interação");
        Assert.Equal(2, snapshot["interacao"].GetArrayLength());
        Assert.Contains(snapshot["interacao"].EnumerateArray(), i => i.GetProperty("mensagem").GetString() == "Resposta pública de QA");
        Assert.Equal(2, c.Capture.Requests.Length);
        Event(c.Capture.Requests[1], "chamado.atualizado", code, Ticket(snapshot));
        Assert.Equal("EmAndamento", Ticket(snapshot).GetProperty("status").GetString());
    });

    [Fact(DisplayName = "RF06-03 aguardando cliente envia evento"), Trait("Group", "SLA-RF06")]
    public Task Rf0603() => Run("RF06-03", "RF06; aguardando ação do cliente", async c =>
    {
        var code = await Open(c);
        Assert.Equal(200, (await c.Status(code, "AguardandoCliente")).Status);
        var ticket = Ticket(await c.Snapshot("aguardando cliente e webhook"));
        Assert.Equal("AguardandoCliente", ticket.GetProperty("status").GetString());
        Assert.Equal(2, c.Capture.Requests.Length);
        Event(c.Capture.Requests[1], "chamado.aguardando_cliente", code, ticket);
    });

    [Fact(DisplayName = "RF06-04 fechamento envia evento"), Trait("Group", "SLA-RF06")]
    public Task Rf0604() => Run("RF06-04", "RF06; fechamento", async c =>
    {
        var code = await Open(c);
        Assert.Equal(200, (await c.Status(code, "Fechado")).Status);
        var ticket = Ticket(await c.Snapshot("fechamento e webhook"));
        Assert.Equal("Fechado", ticket.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, ticket.GetProperty("fechado_em").ValueKind);
        Assert.Equal(2, c.Capture.Requests.Length);
        Event(c.Capture.Requests[1], "chamado.fechado", code, ticket);
    });

    [Fact(DisplayName = "RF06-05 POST autenticado respeita contrato JSON"), Trait("Group", "SLA-RF06")]
    public Task Rf0605() => Run("RF06-05", "RF06; TR v3 §6.2; JSON implementado", async c =>
    {
        var code = await Open(c);
        var request = Assert.Single(c.Capture.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(SlaRf06Application.Destination, request.Destination);
        Assert.Equal("application/json", request.ContentType);
        Assert.True(request.ApiKeyValid, "X-Api-Key deve ser igual à chave efêmera de saída.");
        Assert.Equal(1, request.ApiKeyHeaderCount);
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal(new[] { "categoria", "codigoPublico", "evento", "grupoEmpresaId", "ocorridoEm", "produto", "status", "titulo", "usuarioId" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        var ticket = Ticket(await c.Snapshot("contrato confrontado com SQL"));
        Event(request, "chamado.criado", code, ticket);
        var occurred = json.RootElement.GetProperty("ocorridoEm").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, occurred.Kind);
        Assert.True(occurred >= Date(ticket, "criado_em"));
        Assert.True(occurred <= DateTime.UtcNow);
    });

    [Fact(DisplayName = "RF06-06 500 500 202 repete somente notificação"), Trait("Group", "SLA-RF06")]
    public Task Rf0606() => Run("RF06-06", "RF06; retries adicionais implementados", async c =>
    {
        c.Capture.Respond(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.Accepted);
        var before = await c.Snapshot("antes dos retries");
        var code = await Open(c); // O dispatcher é aguardado: nenhuma espera arbitrária no teste.
        var after = await c.Snapshot("após 500,500,202");
        Assert.Equal(new[] { 500, 500, 202 }, c.Capture.Requests.Select(r => r.ResponseStatus).ToArray());
        Assert.Single(c.Capture.Requests.Select(r => r.Body).Distinct());
        Assert.Equal(before["chamado"].GetArrayLength() + 1, after["chamado"].GetArrayLength());
        Assert.Equal(before["interacao"].GetArrayLength() + 1, after["interacao"].GetArrayLength());
        var ticket = Ticket(after);
        foreach (var request in c.Capture.Requests)
        {
            Event(request, "chamado.criado", code, ticket);
            Assert.True(request.ApiKeyValid);
        }
        Assert.Equal(ticket.GetProperty("id").GetInt32(), Assert.Single(after["interacao"].EnumerateArray()).GetProperty("chamado_id").GetInt32());
    });

    private static async Task<string> Open(SlaRf06Case c)
    {
        var response = await c.Open();
        Assert.Equal(201, response.Status);
        var code = response.Body.GetProperty("codigoPublico").GetString();
        Assert.False(string.IsNullOrWhiteSpace(code));
        return code!;
    }

    private static JsonElement Ticket(Dictionary<string, JsonElement> snapshot) => Assert.Single(snapshot["chamado"].EnumerateArray());
    private static DateTime Date(JsonElement row, string name) => DateTime.SpecifyKind(row.GetProperty(name).GetDateTime(), DateTimeKind.Utc);
    private static void AssertDate(DateTime http, DateTime sql) => Assert.InRange(Math.Abs((http - sql).Ticks), 0L, 10L); // PostgreSQL: microssegundos.
    private static void Preserved(JsonElement before, JsonElement after)
    {
        Assert.Equal(before.GetProperty("sla_prazo_resposta").GetString(), after.GetProperty("sla_prazo_resposta").GetString());
        Assert.Equal(before.GetProperty("sla_prazo_resolucao").GetString(), after.GetProperty("sla_prazo_resolucao").GetString());
    }

    private static void Event(CapturedWebhook request, string expectedEvent, string code, JsonElement ticket)
    {
        using var json = JsonDocument.Parse(request.Body);
        var body = json.RootElement;
        Assert.Equal(expectedEvent, body.GetProperty("evento").GetString());
        Assert.Equal(code, body.GetProperty("codigoPublico").GetString());
        Assert.Equal(ticket.GetProperty("codigo_publico").GetString(), body.GetProperty("codigoPublico").GetString());
        foreach (var field in new[] { "titulo", "produto", "categoria", "status" })
            Assert.Equal(ticket.GetProperty(field).GetString(), body.GetProperty(field).GetString());
        Assert.Equal(ticket.GetProperty("usuario_id").GetInt32(), body.GetProperty("usuarioId").GetInt32());
        Assert.Equal(ticket.GetProperty("grupo_empresa_id").GetInt32(), body.GetProperty("grupoEmpresaId").GetInt32());
    }

    private async Task Run(string id, string requirement, Func<SlaRf06Case, Task> test)
    {
        var c = new SlaRf06Case(postgres);
        var result = "APROVADO";
        var category = "";
        var diagnostic = "Todas as asserções do cenário executadas.";
        var stage = "INFRAESTRUTURA";
        try
        {
            await c.Start();
            stage = "TESTE";
            await test(c);
        }
        catch (XunitException ex) { result = "REPROVADO"; category = "REQUISITO"; diagnostic = c.Sanitize(ex); }
        catch (Exception ex)
        {
            category = stage == "INFRAESTRUTURA" || ex is Npgsql.NpgsqlException ? "INFRAESTRUTURA" : "TESTE";
            result = category == "INFRAESTRUTURA" ? "BLOQUEADO" : "REPROVADO";
            diagnostic = c.Sanitize(ex);
        }
        finally
        {
            try { await c.Stop(); }
            catch (Exception ex)
            {
                diagnostic += " Falha de descarte: " + c.Sanitize(ex);
                if (result == "APROVADO") { result = "BLOQUEADO"; category = "INFRAESTRUTURA"; }
            }
        }
        var directory = Environment.GetEnvironmentVariable("QA_REPORT_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "sla-rf06-results", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Id = id, Requirement = requirement, Result = result, Category = category, Diagnostic = diagnostic,
            ExecutedAtUtc = DateTimeOffset.UtcNow, Database = c.Database,
            Http = c.HttpEvidence, Sql = c.SqlEvidence, Webhooks = c.Application?.Capture?.Requests ?? []
        }, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"{id}: {result} / {category}. {diagnostic}\nEvidência: {path}");
        if (result != "APROVADO") throw new XunitException($"{id}: {result} / {category}. {diagnostic}");
    }
}
