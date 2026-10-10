using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 2, <c>POST /v1/pair/verify</c> (protocol 6.3): any JSON object is answered <c>{"ok":true}</c>.
/// The members are ignored and nothing is recorded.
/// </summary>
internal sealed class PairVerifyRoute : IRouteHandler
{
    private static readonly byte[] Ok = "{\"ok\":true}"u8.ToArray();

    public PairVerifyRoute(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public async Task HandleAsync(RouteContext context)
    {
        var body = await context.ReadJsonBodyAsync().ConfigureAwait(false);
        if (body.Error is { } error)
        {
            await context.WriteErrorAsync(error).ConfigureAwait(false);
            return;
        }

        await context.WriteJsonAsync(StatusCodes.Status200OK, Ok).ConfigureAwait(false);
    }
}
