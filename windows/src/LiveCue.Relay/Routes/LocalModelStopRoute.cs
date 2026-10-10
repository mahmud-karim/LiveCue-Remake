using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 6, <c>POST /v1/local-model/stop</c> (protocol 6.6): checks M-1 to M-3 here, M-4 to M-7 in the
/// controller.
/// </summary>
internal sealed class LocalModelStopRoute : IRouteHandler
{
    private readonly RelayContext _context;
    private readonly ILocalModelController _localModel;

    public LocalModelStopRoute(RelayContext context, ILocalModelController localModel)
    {
        _context = context;
        _localModel = localModel;
    }

    public async Task HandleAsync(RouteContext context)
    {
        var model = await LocalModelStartRoute.ReadModelAsync(context, _context).ConfigureAwait(false);
        var command = await _localModel.StopAsync(model, context.Work).ConfigureAwait(false);
        await LocalModelStartRoute.WriteAsync(context, command).ConfigureAwait(false);
    }
}
