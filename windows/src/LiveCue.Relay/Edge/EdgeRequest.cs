namespace LiveCue.Relay.Edge;

/// <summary>
/// What the edge remembers about one authenticated request between its steps. It is a feature of the
/// request's <c>HttpContext</c>, so that the body reader and the response writer of the same request
/// agree on whether the connection still holds unread bytes.
/// </summary>
internal sealed class EdgeRequest(bool declaresBody)
{
    private volatile bool _bodyRead;
    private volatile bool _connectionLost;

    /// <summary>Step P-1: <c>Content-Length</c> above 0, or a <c>Transfer-Encoding</c> header.</summary>
    public bool DeclaresBody { get; } = declaresBody;

    /// <summary>
    /// W-design K-4: true while the request declared a body that has not been read to its end. A response
    /// written now carries <c>Connection: close</c>, so that what is left of the body can never be taken
    /// for the next request on a connection the proxy reuses.
    /// </summary>
    public bool MustCloseConnection => DeclaresBody && !_bodyRead;

    /// <summary>The body reader saw the caller's connection end under it.</summary>
    public bool ConnectionLost => _connectionLost;

    public void MarkBodyRead() => _bodyRead = true;

    public void MarkConnectionLost() => _connectionLost = true;
}
