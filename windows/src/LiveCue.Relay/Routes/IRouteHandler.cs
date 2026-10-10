using LiveCue.Relay.Edge;

namespace LiveCue.Relay.Routes;

/// <summary>One route. Stateless; one instance serves every request of its route.</summary>
internal interface IRouteHandler
{
    Task HandleAsync(RouteContext context);
}
