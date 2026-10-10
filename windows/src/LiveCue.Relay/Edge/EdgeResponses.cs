using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Edge;

/// <summary>
/// Writes every response relay code produces, so the fixed headers of protocol 5.3 cannot be forgotten:
/// <c>Content-Type: application/json; charset=utf-8</c>, <c>Cache-Control: no-store</c> and
/// <c>X-Content-Type-Options: nosniff</c>, and nothing else besides the length and connection headers.
/// </summary>
internal static class EdgeResponses
{
    private const string JsonContentType = "application/json; charset=utf-8";

    public static Task WriteErrorAsync(HttpContext context, WireError error) =>
        WriteJsonAsync(context, error.Status, error.Body, WireJsonContext.Default.ErrorResponse);

    public static Task WriteJsonAsync<T>(HttpContext context, int status, T body, JsonTypeInfo<T> typeInfo)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, typeInfo);
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = JsonContentType;
        response.ContentLength = bytes.Length;
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";

        // A HEAD request never reaches a route (it is 404 at E-4); its answer keeps the headers and has no body.
        return HttpMethods.IsHead(context.Request.Method)
            ? Task.CompletedTask
            : response.Body.WriteAsync(bytes, context.RequestAborted).AsTask();
    }
}
