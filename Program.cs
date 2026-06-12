using Microsoft.AspNetCore.Mvc;
using System;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

// ─────────────────────────────────────────────
// Configurações
// ─────────────────────────────────────────────
const string VALID_TOKEN = "ska-training-2025";

string[] ExpectedFields = new[]
{
    "ProductionID", "ProdStatus", "ProdRun", "OrderNum", "Operation", "Sequence",
    "PartCode", "PartName", "ResourceCode", "CycleQty", "CycleTime", "SetupTime",
    "ProdTimeTolAbs", "ProdTimeTolRel", "ScrapTolAbs", "ScrapTolRel", "PlanQty",
    "BeginTimestamp", "EndTimestamp", "CycleCount", "PartCount", "ScrapCount",
    "RegistryTimestamp", "ProdData", "IsEnabled", "CostCenter",
    "PlannedBeginTimestamp", "PlannedEndTimestamp", "UnitID"
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
    if (string.IsNullOrWhiteSpace(auth)) return false;
    if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
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

// ─────────────────────────────────────────────
// POST /api/SendOrders
// ─────────────────────────────────────────────
app.MapPost("/api/SendOrders", async (HttpContext ctx, [FromBody] List<SendOrdersRequest>? body) =>
{
    var check = CheckHeaders(ctx.Request, VALID_TOKEN, out var statusCode);
    if (check is not null)
        return Results.Json(check, statusCode: statusCode);

    if (body is null || body.Count == 0)
        return Results.Json(new ApiResponse("O body não pode ser vazio. Envie uma lista de registros.", false), statusCode: 400);

    var errors = new List<string>();
    for (int i = 0; i < body.Count; i++)
    {
        var item = body[i];
        var missing = item.GetMissingFields();
        if (missing.Any())
            errors.Add($"Item [{i}]: campos obrigatórios ausentes → {string.Join(", ", missing)}");
    }

    if (errors.Any())
        return Results.Json(new ApiResponse(string.Join(" | ", errors), false), statusCode: 422);

    return Results.Json(new ApiResponse($"{body.Count} registro(s) recebido(s) com sucesso.", true));
})
.WithName("SendOrders");

// ─────────────────────────────────────────────
// POST /api/SendOrdersDetail
// ─────────────────────────────────────────────
app.MapPost("/api/SendOrdersDetail", async (HttpContext ctx, [FromBody] List<SendOrdersDetailRequest>? body) =>
{
    var check = CheckHeaders(ctx.Request, VALID_TOKEN, out var statusCode);
    if (check is not null)
        return Results.Json(check, statusCode: statusCode);

    if (body is null || body.Count == 0)
        return Results.Json(new ApiResponse("O body não pode ser vazio. Envie uma lista de registros.", false), statusCode: 400);

    var errors = new List<string>();
    for (int i = 0; i < body.Count; i++)
    {
        var item = body[i];
        var missing = item.GetMissingFields();
        if (missing.Any())
            errors.Add($"Item [{i}]: campos obrigatórios ausentes → {string.Join(", ", missing)}");
    }

    if (errors.Any())
        return Results.Json(new ApiResponse(string.Join(" | ", errors), false), statusCode: 422);

    return Results.Json(new ApiResponse($"{body.Count} registro(s) recebido(s) com sucesso.", true));
})
.WithName("SendOrdersDetail");

// ─────────────────────────────────────────────
// POST /api/SendOrdersCustom
// ─────────────────────────────────────────────
app.MapPost("/api/SendOrdersCustom", async (HttpContext ctx, [FromBody] Dictionary<string, object?>? body) =>
{
    if (!TryExtractToken(ctx.Request, out var token))
        return Results.Json(new DiscoverResponse(
            Step: 1,
            Message: "🔒 Acesso negado. Esta rota requer autenticação. Dica: adicione o header 'Authorization' com um Bearer token.",
            Hint: "Header esperado → Authorization: Bearer ???",
            Validate: false), statusCode: 401);

    if (token != VALID_TOKEN)
        return Results.Json(new DiscoverResponse(
            Step: 2,
            Message: "❌ Token inválido. Você está usando o token certo?",
            Hint: "O token tem o formato: ska-????-????. Pergunte ao instrutor 😉",
            Validate: false), statusCode: 401);

    if (!ctx.Request.HasJsonContentType())
        return Results.Json(new DiscoverResponse(
            Step: 3,
            Message: "📄 Autenticado com sucesso! Mas o formato do body está incorreto.",
            Hint: "Header esperado → Content-Type: application/json",
            Validate: false), statusCode: 415);

    if (body is null || body.Count == 0)
        return Results.Json(new DiscoverResponse(
            Step: 4,
            Message: "✅ Headers corretos! Agora envie um body com os dados de produção.",
            Hint: "Envie um objeto JSON com os campos do registro de produção.",
            Validate: false), statusCode: 400);

    var receivedKeys = body.Keys.ToList();
    var receivedKeysLower = receivedKeys.Select(k => k.ToLowerInvariant()).ToHashSet();

    int anchorMatches = AnchorFields.Count(f => receivedKeysLower.Contains(f.ToLowerInvariant()));
    if (anchorMatches < 3)
        return Results.Json(new DiscoverResponse(
            Step: 5,
            Message: "🗄️ Headers e formato OK, mas os dados enviados não parecem ser de produção.",
            Hint: "Dica: procure por uma tabela que tenha colunas como OrderNum, Operation, PartCode, ResourceCode, CycleQty e ProdStatus.",
            Validate: false), statusCode: 422);

    var sentInternalFields = receivedKeys
        .Where(k => InternalFields.Any(f => f.Equals(k, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    if (sentInternalFields.Any())
        return Results.Json(new DiscoverResponse(
            Step: 6,
            Message: $"⚠️ A tabela está correta, mas você está enviando campo(s) interno(s) que não devem fazer parte do payload: {string.Join(", ", sentInternalFields)}.",
            Hint: "Esses campos são apenas para uso interno (rastreamento de eventos). Remova-os do JSON.",
            Validate: false), statusCode: 422);

    var missingFields = ExpectedFields
        .Where(f => !receivedKeysLower.Contains(f.ToLowerInvariant()) ||
                     IsNullOrEmpty(body, receivedKeys, f))
        .ToList();

    if (missingFields.Any())
        return Results.Json(new DiscoverResponse(
            Step: 7,
            Message: "📋 Tabela e campos corretos! Mas alguns campos obrigatórios estão ausentes ou nulos.",
            Hint: $"Campos faltando: {string.Join(", ", missingFields)}.",
            Validate: false), statusCode: 422);

    var unknownFields = receivedKeys
        .Where(k => !ExpectedFields.Any(f => f.Equals(k, StringComparison.OrdinalIgnoreCase))
                 && !InternalFields.Any(f => f.Equals(k, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    if (unknownFields.Any())
        return Results.Json(new DiscoverResponse(
            Step: 8,
            Message: $"🤔 Quase lá! Você enviou campo(s) que não pertencem a esta tabela: {string.Join(", ", unknownFields)}.",
            Hint: "Revise a origem desses dados — eles provavelmente vêm de outra tabela.",
            Validate: false), statusCode: 422);

    return Results.Json(new DiscoverResponse(
        Step: 9,
        Message: "🎉 Parabéns! Você completou todos os passos corretamente.",
        Hint: "Token ✅ | Content-Type ✅ | Tabela (Production) ✅ | Sem campos internos ✅ | Campos obrigatórios ✅",
        Validate: true));
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

record DiscoverResponse(int Step, string Message, string Hint, bool Validate);

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

