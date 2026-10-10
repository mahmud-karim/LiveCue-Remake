using LiveCue.Relay.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace LiveCue.Relay.Http;

/// <summary>
/// The relay's request pipeline. Order matters and is the contract:
/// <list type="number">
/// <item>every response gets the fixed security headers;</item>
/// <item>every request is authenticated before routing, so a caller without a valid token learns
/// nothing about which routes exist (always 401);</item>
/// <item>a route is a method plus an exact request target; anything else is 404.</item>
/// </list>
/// Routing is a dictionary lookup on purpose. ASP.NET Core endpoint routing ignores case and trailing
/// slashes and matches percent-encoded spellings, which is friendlier than this contract allows.
/// </summary>
internal static class RelayPipeline
{
    public const string HealthPath = "/v1/health";

    private const string InvalidToken = "Invalid pairing token.";
    private const string NotFound = "Endpoint not found.";

    public static void Configure(
        WebApplication app,
        IRelayAuthenticator authenticator,
        Action onAuthenticatedRequest)
    {
        var routes = new Dictionary<(string Method, string Target), RequestDelegate>
        {
            [(HttpMethods.Get, HealthPath)] = static context =>
                context.Response.WriteAsJsonAsync(
                    HealthResponse.Current,
                    RelayJsonContext.Default.HealthResponse,
                    cancellationToken: context.RequestAborted),
        };

        app.Run(context =>
        {
            var headers = context.Response.Headers;
            headers.CacheControl = "no-store";
            headers.XContentTypeOptions = "nosniff";

            if (!BearerToken.TryRead(context.Request.Headers.Authorization, out var token)
                || !authenticator.IsAccepted(token))
            {
                headers.WWWAuthenticate = "Bearer";
                return WriteErrorAsync(context, StatusCodes.Status401Unauthorized, InvalidToken);
            }

            onAuthenticatedRequest();

            // The raw target is what the client sent, before any decoding: "/v1/health" and nothing else.
            var target = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
            return routes.TryGetValue((context.Request.Method, target), out var handler)
                ? handler(context)
                : WriteErrorAsync(context, StatusCodes.Status404NotFound, NotFound);
        });
    }

    private static Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(
            new ErrorResponse(message),
            RelayJsonContext.Default.ErrorResponse,
            cancellationToken: context.RequestAborted);
    }
}
