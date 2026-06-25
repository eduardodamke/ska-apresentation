using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

// ─────────────────────────────────────────────
// Configurações
// ─────────────────────────────────────────────
const string VALID_TOKEN = "ska-maestro-training";

string[] ExpectedFields = new[]
{
    "ProductionID", "ProdStatus", "ProdRun", "OrderNum", "Operation", "Sequence",
    "PartCode", "PartName", "ResourceCode", "CycleQty", "CycleTime", "SetupTime",
    "ProdTimeTolAbs", "ProdTimeTolRel", "ScrapTolAbs", "ScrapTolRel", "PlanQty",
    "CycleCount", "PartCount", "ScrapCount", "CostCenter", "UnitID"
};

string[] InternalFields = new[]
{
    "FirstBeginEventID", "LastBeginEventID", "ActualEventID", "ParentProductionID"
};

string[] AnchorFields = new[]
{
    "OrderNum", "Operation", "PartCode", "ResourceCode", "CycleQty", "PartCount", "ScrapCount"
};

// ─────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────
static bool TryExtractToken(HttpRequest request, out string token)
{
    token = string.Empty;
    var auth = request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(auth))
        return false;

    if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return false;

    token = auth["Bearer ".Length..].Trim();

    return !string.IsNullOrWhiteSpace(token);
}

static ApiResponse? CheckHeaders(HttpRequest request, string validToken, out int statusCode)
{
    statusCode = 200;
    var auth = request.Headers.Authorization.ToString();

    if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        statusCode = 401;
        return new ApiResponse("Autenticação necessária. Inclua o header: Authorization: Bearer <token>", false);
    }

    var token = auth["Bearer ".Length..].Trim();
    if (token != validToken)
    {
        statusCode = 401;
        return new ApiResponse("Token inválido. Verifique o valor enviado no header Authorization.", false);
    }

    if (!request.HasJsonContentType())
    {
        statusCode = 415;
        return new ApiResponse("Content-Type incorreto. Use: Content-Type: application/json", false);
    }

    return null;
}

static void LogRequest(string endpoint, HttpContext ctx, object? body, int responseStatus, string responseMessage)
{
    var sep = new string('-', 60);
    var req = ctx.Request;
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    var headers = string.Join("\n    ", req.Headers
        .Where(h => !h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
        .Select(h => $"{h.Key}: {h.Value}"));

    var bodyJson = body is null
        ? "(vazio)"
        : JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });

    var statusIcon = responseStatus is >= 200 and < 300 ? "OK" : "ERRO";

    Console.WriteLine(sep);
    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] POST {endpoint}");
    Console.WriteLine($"IP         : {ip}");
    Console.WriteLine($"User-Agent : {req.Headers.UserAgent}");
    Console.WriteLine("HEADERS:");
    Console.WriteLine($"    {headers}");
    Console.WriteLine("BODY:");
    Console.WriteLine(bodyJson);
    Console.WriteLine($"{statusIcon} [{responseStatus}]: {responseMessage}");
    Console.WriteLine(sep);
    Console.Out.Flush();
}

// ─────────────────────────────────────────────
// POST /api/SendOrders
// ─────────────────────────────────────────────
app.MapPost("/api/SendOrders", async (HttpContext ctx, [FromBody] List<SendOrdersRequest>? body) =>
{
    var check = CheckHeaders(ctx.Request, VALID_TOKEN, out var statusCode);
    if (check is not null)
    {
        LogRequest("/api/SendOrders", ctx, body, statusCode, check.Message);
        return Results.Json(check, statusCode: statusCode);
    }

    if (body is null || body.Count == 0)
    {
        var resp = new ApiResponse("O body não pode ser vazio. Envie uma lista de registros.", false);
        LogRequest("/api/SendOrders", ctx, body, 400, resp.Message);
        return Results.Json(resp, statusCode: 400);
    }

    var errors = new List<string>();
    for (int i = 0; i < body.Count; i++)
    {
        var item = body[i];
        var missing = item.GetMissingFields();
        if (missing.Any())
            errors.Add($"Item [{i}]: campos obrigatórios ausentes → {string.Join(", ", missing)}");
    }

    if (errors.Any())
    {
        var resp = new ApiResponse(string.Join(" | ", errors), false);
        LogRequest("/api/SendOrders", ctx, body, 422, resp.Message);
        return Results.Json(resp, statusCode: 422);
    }

    var success = new ApiResponse($"{body.Count} registro(s) recebido(s) com sucesso.", true);
    LogRequest("/api/SendOrders", ctx, body, 200, success.Message);
    return Results.Json(success);
})
.WithName("SendOrders");

// ─────────────────────────────────────────────
// POST /api/SendOrdersDetail
// ─────────────────────────────────────────────
app.MapPost("/api/SendOrdersDetail", async (HttpContext ctx, [FromBody] List<SendOrdersDetailRequest>? body) =>
{
    var check = CheckHeaders(ctx.Request, VALID_TOKEN, out var statusCode);
    if (check is not null)
    {
        LogRequest("/api/SendOrdersDetail", ctx, body, statusCode, check.Message);
        return Results.Json(check, statusCode: statusCode);
    }

    if (body is null || body.Count == 0)
    {
        var resp = new ApiResponse("O body não pode ser vazio. Envie uma lista de registros.", false);
        LogRequest("/api/SendOrdersDetail", ctx, body, 400, resp.Message);
        return Results.Json(resp, statusCode: 400);
    }

    var errors = new List<string>();
    for (int i = 0; i < body.Count; i++)
    {
        var item = body[i];
        var missing = item.GetMissingFields();
        if (missing.Any())
            errors.Add($"Item [{i}]: campos obrigatórios ausentes → {string.Join(", ", missing)}");
    }

    if (errors.Any())
    {
        var resp = new ApiResponse(string.Join(" | ", errors), false);
        LogRequest("/api/SendOrdersDetail", ctx, body, 422, resp.Message);
        return Results.Json(resp, statusCode: 422);
    }

    var success = new ApiResponse($"{body.Count} registro(s) recebido(s) com sucesso.", true);
    LogRequest("/api/SendOrdersDetail", ctx, body, 200, success.Message);
    return Results.Json(success);
})
.WithName("SendOrdersDetail");

// ─────────────────────────────────────────────
// POST /api/SendOrdersCustom
// ─────────────────────────────────────────────
app.MapPost("/api/SendOrdersCustom", async (HttpContext ctx, [FromBody] Dictionary<string, object?>? body) =>
{
    IResult Respond(DiscoverResponse r, int status)
    {
        LogRequest("/api/SendOrdersCustom", ctx, body, status, r.Message);
        return Results.Json(r, statusCode: status);
    }

    if (!TryExtractToken(ctx.Request, out var token))
        return Respond(new DiscoverResponse(
            Message: "Acesso negado. Esta rota requer autenticação. Dica: adicione o header 'Authorization' com um Bearer token. Header esperado → Authorization: Bearer ???",
            Validate: false), 401);

    if (token != VALID_TOKEN)
        return Respond(new DiscoverResponse(
            Message: "Token inválido. Você está usando o token certo? O token tem o formato: ska-????-????.",
            Validate: false), 401);

    if (!ctx.Request.HasJsonContentType())
        return Respond(new DiscoverResponse(
            Message: "Autenticado com sucesso! Mas o formato do body está incorreto. Header esperado → Content-Type: application/json",
            Validate: false), 415);

    if (body is null || body.Count == 0)
        return Respond(new DiscoverResponse(
            Message: "Headers corretos! Agora envie um body com os dados de produção. Envie um objeto JSON com os campos do registro de produção.",
            Validate: false), 400);

    var receivedKeys = body.Keys.ToList();
    var receivedKeysLower = receivedKeys.Select(k => k.ToLowerInvariant()).ToHashSet();

    int anchorMatches = AnchorFields.Count(f => receivedKeysLower.Contains(f.ToLowerInvariant()));
    if (anchorMatches < 3)
        return Respond(new DiscoverResponse(
            Message: "Headers e formato OK, mas os dados enviados não parecem ser de produção. Dica: procure por uma tabela que tenha colunas como OrderNum, Operation, PartCode, ResourceCode, CycleQty...",
            Validate: false), 422);

    var sentInternalFields = receivedKeys
        .Where(k => InternalFields.Any(f => f.Equals(k, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    if (sentInternalFields.Any())
        return Respond(new DiscoverResponse(
            Message: $"A tabela está correta, mas você está enviando campo(s) interno(s) que não devem fazer parte do payload: {string.Join(", ", sentInternalFields)}. Esses campos são apenas para uso interno (rastreamento de eventos). Remova-os do JSON.",
            Validate: false), 422);

    var missingFields = ExpectedFields
        .Where(f => !receivedKeysLower.Contains(f.ToLowerInvariant()) ||
                     IsNullOrEmpty(body, receivedKeys, f))
        .ToList();

    if (missingFields.Any())
        return Respond(new DiscoverResponse(
            Message: $"Tabela e campos corretos! Mas alguns campos obrigatórios estão ausentes ou nulos. Campos faltando: {string.Join(", ", missingFields)}.",
            Validate: false), 422);

    return Respond(new DiscoverResponse(
        Message: "",
        Validate: true), 200);
})
.WithName("Discover");

static bool IsNullOrEmpty(Dictionary<string, object?> body, List<string> receivedKeys, string field)
{
    var key = receivedKeys.FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
    if (key is null) return true;
    var value = body[key];
    if (value is null) return true;
    if (value is string s && string.IsNullOrWhiteSpace(s)) return true;
    return false;
}

app.Run();

// ─────────────────────────────────────────────
// Models
// ─────────────────────────────────────────────
record ApiResponse(string Message, bool Validate);

record DiscoverResponse(string Message, bool Validate);

record SendOrdersRequest(string? OP, string? OPER, string? CODPECA, string? MAQ)
{
    public List<string> GetMissingFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(OP)) missing.Add("OP");
        if (string.IsNullOrWhiteSpace(OPER)) missing.Add("OPER");
        if (string.IsNullOrWhiteSpace(CODPECA)) missing.Add("CODPECA");
        if (string.IsNullOrWhiteSpace(MAQ)) missing.Add("MAQ");
        return missing;
    }
}

record SendOrdersDetailRequest(
    string? OP, string? OPER, string? CODPECA, string? SEQOPER, string? MAQ)
{
    public List<string> GetMissingFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(OP)) missing.Add("OP");
        if (string.IsNullOrWhiteSpace(OPER)) missing.Add("OPER");
        if (string.IsNullOrWhiteSpace(CODPECA)) missing.Add("CODPECA");
        if (string.IsNullOrWhiteSpace(SEQOPER)) missing.Add("SEQOPER");
        if (string.IsNullOrWhiteSpace(MAQ)) missing.Add("MAQ");
        return missing;
    }
}