namespace SupportAtos.Api.Middleware;

/// <summary>Cookie endpoints require an explicit trusted Origin or Referer.
/// Missing headers and Origin: null never prove same-origin.</summary>
public class CsrfProtectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _origens;
    private readonly string[] _caminhos;

    public CsrfProtectionMiddleware(RequestDelegate next, string[] origensPermitidas, string[] caminhosProtegidos)
    {
        _next = next;
        _origens = origensPermitidas.Select(ValidarOrigem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _caminhos = caminhosProtegidos;
    }

    public static string ValidarOrigem(string origem)
    {
        if (!Uri.TryCreate(origem, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http")
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || origem.EndsWith('/') || origem.Contains('\\'))
            throw new InvalidOperationException("Cors:AllowedOrigins deve conter somente origens HTTP(S) explicitas, sem caminho ou barra final.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!_caminhos.Any(c => request.Path.StartsWithSegments(c, StringComparison.OrdinalIgnoreCase))
            || HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
        {
            await _next(context);
            return;
        }

        string? origem = null;
        try
        {
            if (request.Headers.ContainsKey("Origin"))
                origem = ValidarOrigem(request.Headers.Origin.ToString());
            else if (request.Headers.Referer.Count == 1
                && Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
                && string.IsNullOrEmpty(referer.UserInfo)
                && !request.Headers.Referer.ToString().Contains('\\'))
                origem = ValidarOrigem(referer.GetLeftPart(UriPartial.Authority));
        }
        catch (InvalidOperationException) { }

        if (origem is null || !_origens.Contains(origem))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { Status = 403, Title = "CSRF invalido", Detail = "Origem ausente ou nao permitida." });
            return;
        }
        await _next(context);
    }
}
