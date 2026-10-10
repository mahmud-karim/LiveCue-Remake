using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Edge;

/// <summary>
/// Writes every response relay code produces (protocol 5.3), so that the fixed headers cannot be
/// forgotten: <c>Content-Type: application/json; charset=utf-8</c>, <c>Cache-Control: no-store</c> and
/// <c>X-Content-Type-Options: nosniff</c>, plus <c>Content-Length</c>. Nothing else is ever set: no CORS
/// header, no cookie, no redirect. Kestrel itself adds <c>Date</c> and has no switch for it (W-design K-1).
/// </summary>
internal static class ResponseWriter
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string NoStore = "no-store";
    private const string NoSniff = "nosniff";
    private const string Close = "close";

    /// <summary>
    /// One JSON document, written in one call. A second write on the same request throws
    /// <see cref="InvalidOperationException"/>; a status from 300 to 399 is never written.
    /// </summary>
    /// <param name="closeConnection">True adds <c>Connection: close</c>: the response is written before
    /// the request body was completely read, and the connection must not be used for another request
    /// (W-design K-4).</param>
    public static Task WriteJsonAsync(HttpContext http, int status, ReadOnlyMemory<byte> utf8Json, bool closeConnection = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(status, 200);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599);
        if (status is >= 300 and <= 399)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "The relay never sends a redirect.");
        }

        var response = http.Response;
        if (response.HasStarted)
        {
            throw new InvalidOperationException("A response was already written for this request.");
        }

        response.StatusCode = status;
        response.ContentLength = utf8Json.Length;

        var headers = response.Headers;
        headers.ContentType = JsonContentType;
        headers.CacheControl = NoStore;
        headers.XContentTypeOptions = NoSniff;
        if (closeConnection)
        {
            headers.Connection = Close;
        }

        // HEAD never reaches a route (it is 404 at E-4); its answer keeps the headers and has no body.
        return HttpMethods.IsHead(http.Request.Method)
            ? response.StartAsync(http.RequestAborted)
            : response.Body.WriteAsync(utf8Json, http.RequestAborted).AsTask();
    }
}
