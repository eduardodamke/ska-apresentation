using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

// ─────────────────────────────────────────────
// Middleware de log global
// ─────────────────────────────────────────────
app.Use(async (ctx, next) =>
{
    ctx.Request.EnableBuffering();

    var sep = new string('-', 60);
    var req = ctx.Request;
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    var headers = string.Join("\n  ", req.Headers
        .Where(h => !h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
        .Select(h => h.Key + ": " + h.Value));

    string bodyText = "(vazio)";
    try
    {
        using var reader = new StreamReader(req.Body, leaveOpen: true);
        bodyText = await reader.ReadToEndAsync();
        req.Body.Position = 0;
        if (string.IsNullOrWhiteSpace(bodyText)) bodyText = "(vazio)";
    }
    catch { }

    Console.Error.WriteLine(sep);
    Console.Error.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + req.Method + " " + req.Path);
    Console.Error.WriteLine("Headers:");
    Console.Error.WriteLine("  " + headers);
    Console.Error.WriteLine("Body:");
    Console.Error.WriteLine(bodyText);
    Console.Error.WriteLine(sep);
    Console.Error.Flush();

    await next();
});

// ─────────────────────────────────────────────
// Configurações
// ─────────────────────────────────────────────
const string VALID_TOKEN = "ska-maestro-training";

string[] ExpectedFields = new[]
{
    "ProductionID", "OrderNum", "Operation", "Sequence",
    "PartCode", "PartName", "CycleQty", "CycleTime", "SetupTime", "PlanQty",
    "CycleCount", "PartCount", "ScrapCount"
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
        var missing = body[i].GetMissingFields();
        if (missing.Any())
            errors.Add("Item [" + i + "]: campos obrigatórios ausentes -> " + string.Join(", ", missing));
    }

    if (errors.Any())
        return Results.Json(new ApiResponse(string.Join(" | ", errors), false), statusCode: 422);

    return Results.Json(new ApiResponse(body.Count + " registro(s) recebido(s) com sucesso.", true));
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
        var missing = body[i].GetMissingFields();
        if (missing.Any())
            errors.Add("Item [" + i + "]: campos obrigatórios ausentes -> " + string.Join(", ", missing));
    }

    if (errors.Any())
        return Results.Json(new ApiResponse(string.Join(" | ", errors), false), statusCode: 422);

    return Results.Json(new ApiResponse(body.Count + " registro(s) recebido(s) com sucesso.", true));
})
.WithName("SendOrdersDetail");

// ─────────────────────────────────────────────
// POST /api/SendOrdersCustom
// ─────────────────────────────────────────────
app.MapPost("/api/SendOrdersCustom", async (HttpContext ctx, [FromBody] List<Dictionary<string, object?>>? body) =>
{
    if (!TryExtractToken(ctx.Request, out var token))
        return Results.Json(new DiscoverResponse(
            Message: "Acesso negado. Esta rota requer autenticação. Dica: adicione o header 'Authorization' com um Bearer token. Header esperado -> Authorization: Bearer ???",
            Validate: false), statusCode: 401);

    if (token != VALID_TOKEN)
        return Results.Json(new DiscoverResponse(
            Message: "Token inválido. Você está usando o token certo? O token tem o formato: ska-????-????.",
            Validate: false), statusCode: 401);

    if (!ctx.Request.HasJsonContentType())
        return Results.Json(new DiscoverResponse(
            Message: "Autenticado com sucesso! Mas o formato do body está incorreto. Header esperado -> Content-Type: application/json",
            Validate: false), statusCode: 415);

    if (body is null || body.Count == 0)
        return Results.Json(new DiscoverResponse(
            Message: "Headers corretos! Agora envie um body com os dados de producao. Envie uma lista de objetos JSON com os campos do registro de producao.",
            Validate: false), statusCode: 400);

    var allKeys = body.SelectMany(item => item.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var allKeysLower = allKeys.Select(k => k.ToLowerInvariant()).ToHashSet();

    int anchorMatches = AnchorFields.Count(f => allKeysLower.Contains(f.ToLowerInvariant()));
    if (anchorMatches < 3)
    {
        var msg = "Headers e formato OK, mas os dados enviados nao parecem ser de producao. Dica: procure por uma tabela que tenha colunas como OrderNum, Operation, PartCode, ResourceCode, CycleQty...";
        return Results.Json(body.Select((item, i) => new DiscoverResponse(Id: GetId(item), Message: msg, Validate: false)).ToList(), statusCode: 422);
    }

    var sentInternalFields = allKeys
        .Where(k => InternalFields.Any(f => f.Equals(k, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    if (sentInternalFields.Any())
    {
        var msg = "Voce esta enviando campo(s) interno(s) que nao devem fazer parte do payload: " + string.Join(", ", sentInternalFields) + ". Remova-os do JSON.";
        return Results.Json(body.Select((item, i) => new DiscoverResponse(Id: GetId(item), Message: msg, Validate: false)).ToList(), statusCode: 422);
    }

    var results = body.Select(item =>
    {
        var itemKeys = item.Keys.ToList();
        var missingFields = ExpectedFields
            .Where(f => !itemKeys.Any(k => k.Equals(f, StringComparison.OrdinalIgnoreCase))
                     || IsNullOrEmpty(item, itemKeys, f))
            .ToList();

        if (missingFields.Any())
            return new DiscoverResponse(Id: GetId(item), Message: "Campos obrigatorios ausentes ou nulos: " + string.Join(", ", missingFields) + ".", Validate: false);

        return new DiscoverResponse(Id: GetId(item), Message: "", Validate: true);
    }).ToList();

    return Results.Json(results);
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

static int? GetId(Dictionary<string, object?> item)
{
    var key = item.Keys.FirstOrDefault(k => k.Equals("ProductionID", StringComparison.OrdinalIgnoreCase));
    if (key is null) return null;
    if (item[key] is System.Text.Json.JsonElement el && el.TryGetInt32(out var v)) return v;
    return null;
}

app.Run();

// ─────────────────────────────────────────────
// Models
// ─────────────────────────────────────────────
record ApiResponse(string Message, bool Validate);

record DiscoverResponse(int? Id, string Message, bool Validate);

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