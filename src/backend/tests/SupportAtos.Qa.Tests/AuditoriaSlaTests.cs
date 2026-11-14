using System.Globalization;
using System.Text.Json;
using Npgsql;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SupportAtos.Qa.Tests;

[Collection("QA isolado")]
public sealed class AuditoriaSlaTests(QaPostgres postgres, ITestOutputHelper output)
{
    [Fact(DisplayName = "AUD-01 criação prazos e identidade recuperáveis"), Trait("Group", "AUD")]
    public Task Aud01() => Run("AUD-01", async a =>
    {
        var code = await a.Open();
        var db = await a.Snapshot("após abertura; leitura independente");
        var ticket = TraceAuditCase.Ticket(db);
        var created = TraceAuditCase.Date(ticket, "criado_em");
        var userId = ticket.GetProperty("usuario_id").GetInt32();
        var groupId = ticket.GetProperty("grupo_empresa_id").GetInt32();
        a.Check("Identidade da abertura", userId == 61001 && groupId == 71001
            && db["usuario"].EnumerateArray().Any(u => u.GetProperty("id").GetInt32() == userId)
            && db["grupo_empresa"].EnumerateArray().Any(g => g.GetProperty("id").GetInt32() == groupId),
            $"chamado={code}; autor={userId}; grupo={groupId}");
        a.Check("Criação e prazos recuperáveis sem exigir LogAuditoria", created != default
            && TraceAuditCase.Date(ticket, "sla_prazo_resposta") == created.AddMinutes(60)
            && TraceAuditCase.Date(ticket, "sla_prazo_resolucao") == created.AddMinutes(480),
            $"criação={created:O}; resposta={TraceAuditCase.Date(ticket, "sla_prazo_resposta"):O}; resolução={TraceAuditCase.Date(ticket, "sla_prazo_resolucao"):O}");
        a.Check("Interação inicial vinculada ao autor e chamado", db["interacao"].EnumerateArray().Any(i =>
            i.GetProperty("chamado_id").GetInt32() == ticket.GetProperty("id").GetInt32()
            && i.GetProperty("autor_id").GetInt32() == userId && i.GetProperty("criado_em").ValueKind == JsonValueKind.String),
            "Verificados vínculo, autor e timestamp da interação inicial.");
    });

    [Fact(DisplayName = "AUD-02 transição anterior sobrevive a nova alteração"), Trait("Group", "AUD")]
    public Task Aud02() => Run("AUD-02", async a =>
    {
        var code = await a.Open();
        await a.Snapshot("antes da primeira transição");
        var first = await a.Status(code, "EmAndamento");
        await a.Snapshot("após Aberto → EmAndamento; apenas oráculo de comparação");
        await a.Status(code, "AguardandoCliente");
        var final = await a.Snapshot("após segunda transição; única fonte usada para reconstrução");
        a.Check("Status atual persistido", TraceAuditCase.Ticket(final).GetProperty("status").GetString() == "AguardandoCliente", "Esperado AguardandoCliente.");
        a.Check("Histórico preserva Aberto → EmAndamento e momento", TraceAuditCase.HasTransition(final, code, "Aberto", "EmAndamento", first),
            "Busca em todas as relações públicas por evento com estados anterior/novo ou versões datadas vinculadas ao chamado. Não usa o snapshot anterior como histórico do produto.");
    });

    [Fact(DisplayName = "AUD-03 autor horário e prazo da resposta recuperáveis"), Trait("Group", "AUD")]
    public Task Aud03() => Run("AUD-03", async a =>
    {
        var code = await a.Open();
        var responseWindow = await a.Interact(code);
        var before = await a.Snapshot("resposta registrada");
        var firstResponse = FirstStaffResponse(before);
        await a.Status(code, "AguardandoCliente"); // Sobrescreve AtualizadoEm; a interação deve sobreviver.
        var final = await a.Snapshot("após alteração posterior à resposta");
        var recovered = FirstStaffResponse(final);
        var author = recovered.GetProperty("autor_id").GetInt32();
        var at = TraceAuditCase.Date(recovered, "criado_em");
        var deadline = TraceAuditCase.Date(TraceAuditCase.Ticket(final), "sla_prazo_resposta");
        a.Check("Primeira resposta pública da equipe preservada", recovered.GetProperty("id").GetInt32() == firstResponse.GetProperty("id").GetInt32()
            && at == TraceAuditCase.Date(firstResponse, "criado_em") && responseWindow.Contains(at),
            $"interação={recovered.GetProperty("id")}; horário={at:O}");
        a.Check("Autor recuperável", author == 62001 && final["usuario"].EnumerateArray().Any(u => u.GetProperty("id").GetInt32() == author),
            $"autor={author}; identidade presente no banco.");
        a.Check("Relação resposta/prazo apurável", deadline != default && at != default,
            $"resposta={at:O}; prazo={deadline:O}; diferença={(at - deadline).TotalSeconds:F6}s; cumprimento observado={at <= deadline}. Não se exige aprovação do SLA, somente capacidade de apuração.");
    });

    [Fact(DisplayName = "AUD-04 resolução e fechamento distinguíveis após fechar"), Trait("Group", "AUD")]
    public Task Aud04() => Run("AUD-04", async a =>
    {
        var code = await a.Open();
        var resolved = await a.Status(code, "Resolvido");
        await a.Snapshot("resolvido; AtualizadoEm observado antes do fechamento");
        var closed = await a.Status(code, "Fechado");
        var final = await a.Snapshot("fechado; recuperação apenas do estado final persistido");
        var ticket = TraceAuditCase.Ticket(final);
        a.Check("Fechamento recuperável", ticket.GetProperty("fechado_em").ValueKind == JsonValueKind.String
            && closed.Contains(TraceAuditCase.Date(ticket, "fechado_em")), "FechadoEm deve identificar o fechamento efetivo.");
        a.Check("Momento da resolução preservado após fechamento", TraceAuditCase.HasMoment(final, code, "Resolvido", resolved),
            "Avalia timestamps de eventos/versões em qualquer relação vinculada; não exige coluna ResolvidoEm.");
    });

    [Fact(DisplayName = "AUD-05 reabertura preserva história contratual anterior"), Trait("Group", "AUD")]
    public Task Aud05() => Run("AUD-05", async a =>
    {
        var code = await a.Open();
        await a.Interact(code);
        var resolved = await a.Status(code, "Resolvido");
        var closed = await a.Status(code, "Fechado");
        var before = await a.Snapshot("antes da reabertura");
        await a.Status(code, "Aberto");
        await a.Status(code, "EmAndamento"); // Não confundir última atualização com eventos anteriores.
        var final = await a.Snapshot("após reabertura e alteração subsequente");
        var original = TraceAuditCase.Ticket(before);
        var current = TraceAuditCase.Ticket(final);
        a.Check("Criação prazos e interações anteriores preservados",
            new[] { "criado_em", "sla_prazo_resposta", "sla_prazo_resolucao", "usuario_id", "grupo_empresa_id" }
                .All(field => original.GetProperty(field).GetRawText() == current.GetProperty(field).GetRawText())
            && before["interacao"].GetRawText() == final["interacao"].GetRawText(),
            "Compara dados persistidos antes/depois; não usa a comparação como substituto de histórico de status.");
        a.Check("Resolução anterior continua reconstruível", TraceAuditCase.HasMoment(final, code, "Resolvido", resolved),
            "Procura evento/versão datada no banco após reabertura.");
        a.Check("Fechamento anterior continua reconstruível", TraceAuditCase.HasMoment(final, code, "Fechado", closed),
            $"FechadoEm atual={current.GetProperty("fechado_em").GetRawText()}; histórico deve preservar fechamento anterior por alguma fonte persistida.");
    });

    private static JsonElement FirstStaffResponse(Dictionary<string, JsonElement> db)
    {
        var staff = db["usuario"].EnumerateArray().Where(u => u.GetProperty("papel").GetString() != "Cliente")
            .Select(u => u.GetProperty("id").GetInt32()).ToHashSet();
        return db["interacao"].EnumerateArray().Where(i => i.GetProperty("tipo").GetString() == "Publica"
            && staff.Contains(i.GetProperty("autor_id").GetInt32())).OrderBy(i => TraceAuditCase.Date(i, "criado_em")).First();
    }

    private async Task Run(string id, Func<TraceAuditCase, Task> scenario)
    {
        var a = new TraceAuditCase(postgres);
        var result = "INCONCLUSIVO";
        var category = "INFRAESTRUTURA";
        var diagnostic = "";
        var started = false;
        try
        {
            await a.Start();
            started = true;
            await scenario(a);
            result = a.Checks.All(c => c.Passed) ? "APROVADO" : "FALHOU";
            category = result == "FALHOU" ? "RASTREABILIDADE" : "";
        }
        catch (XunitException ex) { result = "FALHOU"; category = "BACKEND/HTTP"; diagnostic = a.Real.Sanitize(ex); }
        catch (Exception ex) { category = !started || ex is NpgsqlException ? "INFRAESTRUTURA" : "TESTE"; diagnostic = a.Real.Sanitize(ex); }
        finally
        {
            try { await a.Real.Stop(); }
            catch (Exception ex) { result = "INCONCLUSIVO"; category = "INFRAESTRUTURA"; diagnostic += " Descarte: " + a.Real.Sanitize(ex); }
        }
        var path = Path.Combine(postgres.Report.DirectoryPath, id + ".json");
        Directory.CreateDirectory(postgres.Report.DirectoryPath);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Id = id, Requirement = "TR v3 §5, p.5: Registro de logs de auditoria para rastreabilidade de SLA contratual.",
            Result = result, Category = category, Diagnostic = diagnostic, ExecutedAtUtc = DateTimeOffset.UtcNow,
            Database = a.Real.Database, Checks = a.Checks, Http = a.Real.HttpEvidence, Sql = a.Sql,
            Schema = a.Schema, NonInternalTriggers = a.Triggers,
            Webhooks = a.Real.Application?.Capture?.Requests ?? [],
            Scope = "Banco isolado após as operações. Capturas HTTP/webhook e snapshots prévios do teste não são considerados histórico mantido pelo produto. Logs/consumidores externos não foram validados."
        }, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"{id}: {result} / {category}. {diagnostic}\n{string.Join("\n", a.Checks)}\nEvidência: {path}");
        if (result != "APROVADO") throw new XunitException($"{id}: {result} / {category}. {diagnostic}\n" + string.Join("\n", a.Checks.Where(c => !c.Passed)));
    }
}

public record TraceCheck(string Condition, bool Passed, string Observation);
public record TraceWindow(DateTime Start, DateTime End)
{
    public bool Contains(DateTime at) => at >= Start.AddTicks(-10) && at <= End.AddTicks(10);
}

// Reutiliza migrations, host, autenticação e transporte seguro existentes. Só acrescenta consultas de leitura.
public sealed class TraceAuditCase(QaPostgres postgres)
{
    public SlaRf06Case Real { get; } = new(postgres);
    public List<TraceCheck> Checks { get; } = [];
    public List<object> Sql { get; } = [];
    public JsonElement Schema { get; private set; } = JsonSerializer.SerializeToElement(Array.Empty<object>());
    public JsonElement Triggers { get; private set; } = JsonSerializer.SerializeToElement(Array.Empty<object>());
    public Task Start() => Real.Start();
    public void Check(string condition, bool passed, string observation) => Checks.Add(new(condition, passed, observation));

    public async Task<string> Open()
    {
        var response = await Real.Open();
        Assert.Equal(201, response.Status);
        return response.Body.GetProperty("codigoPublico").GetString()!;
    }

    public async Task<TraceWindow> Status(string code, string state)
    {
        var start = DateTime.UtcNow;
        var response = await Real.Status(code, state);
        var end = DateTime.UtcNow;
        Assert.Equal(200, response.Status);
        Assert.Equal(state, response.Body.GetProperty("status").GetString());
        return new(start, end);
    }

    public async Task<TraceWindow> Interact(string code)
    {
        var start = DateTime.UtcNow;
        var response = await Real.Interact(code);
        var end = DateTime.UtcNow;
        Assert.Equal(201, response.Status);
        return new(start, end);
    }

    public async Task<Dictionary<string, JsonElement>> Snapshot(string label)
    {
        await using var connection = new NpgsqlConnection(postgres.Connection(Real.Database));
        await connection.OpenAsync();
        var queries = new List<string>();
        async Task<JsonElement> JsonQuery(string sql)
        {
            queries.Add(sql);
            await using var command = new NpgsqlCommand(sql, connection);
            using var json = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            return json.RootElement.Clone();
        }
        Schema = await JsonQuery("SELECT COALESCE(jsonb_agg(jsonb_build_object('table',table_name,'column',column_name,'type',data_type) ORDER BY table_name,ordinal_position),'[]'::jsonb)::text FROM information_schema.columns WHERE table_schema='public'");
        Triggers = await JsonQuery("SELECT COALESCE(jsonb_agg(jsonb_build_object('name',t.tgname,'definition',pg_get_triggerdef(t.oid))),'[]'::jsonb)::text FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal");
        var tables = Schema.EnumerateArray().Select(c => c.GetProperty("table").GetString()!).Distinct().OrderBy(t => t).ToArray();
        var snapshot = new Dictionary<string, JsonElement>();
        foreach (var table in tables)
        {
            // Nomes vêm do catálogo do banco exclusivo; QuoteIdentifier também cobre nomes incomuns.
            var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(table);
            snapshot[table] = await JsonQuery($"SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),'[]'::jsonb)::text FROM public.{quoted} t");
        }
        Sql.Add(new { Label = label, Queries = queries, Tables = snapshot });
        return snapshot;
    }

    public static JsonElement Ticket(Dictionary<string, JsonElement> db) => Assert.Single(db["chamado"].EnumerateArray());
    public static DateTime Date(JsonElement row, string field) => DateTime.SpecifyKind(row.GetProperty(field).GetDateTime(), DateTimeKind.Utc);
    private static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    // Suporta eventos com anterior/novo e versões separadas. Não exige tabela LogAuditoria ou coluna ResolvidoEm.
    // Procura somente dados persistidos no snapshot final, vinculados por FK/id/código ao chamado em teste.
    private static IEnumerable<JsonElement> RelatedRows(Dictionary<string, JsonElement> db, string code)
    {
        var id = Ticket(db).GetProperty("id").GetInt32();
        foreach (var (table, rows) in db)
            foreach (var row in rows.EnumerateArray())
                if ((table == "chamado" && row.GetProperty("id").GetInt32() == id)
                    || row.EnumerateObject().Any(p => Normalize(p.Name) is "chamadoid" && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var fk) && fk == id)
                    || Strings(row).Any(v => v == code)) yield return row;
    }

    private static IEnumerable<string> Strings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var p in value.EnumerateObject()) { foreach (var s in Strings(p.Value)) yield return s; }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) { foreach (var s in Strings(item)) yield return s; }
        else if (value.ValueKind == JsonValueKind.String) yield return value.GetString()!;
    }

    private static bool State(JsonElement row, string state) => Strings(row).Any(s => Normalize(s) == Normalize(state));
    private static IEnumerable<DateTime> Times(JsonElement row)
    {
        foreach (var s in Strings(row))
            if (s.Contains('T') && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                yield return DateTime.SpecifyKind(at, DateTimeKind.Utc);
    }

    public static bool HasMoment(Dictionary<string, JsonElement> db, string code, string state, TraceWindow window) =>
        RelatedRows(db, code).Any(row => (State(row, state) && Times(row).Any(window.Contains))
            || row.EnumerateObject().Any(p => MomentField(p.Name, state) && Times(p.Value).Any(window.Contains)));

    // Um marco no próprio chamado também é suficiente; não se exige um evento separado.
    // Prazos são vencimentos, portanto não podem ser usados como instantes de execução.
    private static bool MomentField(string name, string state)
    {
        var field = Normalize(name);
        if (field.Contains("prazo") || field.Contains("deadline")) return false;
        return Normalize(state) switch
        {
            "resolvido" => field.Contains("resolvido") || field.Contains("resolved") || field is "resolucaoem" or "dataresolucao",
            "fechado" => field.Contains("fechado") || field.Contains("closed") || field is "fechamentoem" or "datafechamento",
            _ => false
        };
    }

    public static bool HasTransition(Dictionary<string, JsonElement> db, string code, string from, string to, TraceWindow window)
    {
        var rows = RelatedRows(db, code).ToArray();
        return rows.Any(row => State(row, from) && State(row, to) && Times(row).Any(window.Contains))
            || (HasMoment(db, code, to, window) && rows.Any(row => State(row, from) && Times(row).Any(t => t < window.Start)));
    }
}
