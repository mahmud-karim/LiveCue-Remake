using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 6, <c>POST /v1/local-model/stop</c> (protocol 6.6): the checks M-1 to M-3 here, M-4 to M-7 in the
/// controller. 202 with the beginning status when the stop began.
/// </summary>
internal sealed class LocalModelStopRoute : IRouteHandler
{
    private readonly RelayContext _context;
    private readonly ILocalModelController _localModel;

    public LocalModelStopRoute(RelayContext context, ILocalModelController localModel)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(localModel);
        _context = context;
        _localModel = localModel;
    }

    public async Task HandleAsync(RouteContext context)
    {
        if (await LocalModelStartRoute.ReadModelAsync(context, _context).ConfigureAwait(false) is not { } model)
        {
            return;
        }

        var command = await _localModel.StopAsync(model, context.Work).ConfigureAwait(false);
        await LocalModelStartRoute.WriteAsync(context, command).ConfigureAwait(false);
    }
}
