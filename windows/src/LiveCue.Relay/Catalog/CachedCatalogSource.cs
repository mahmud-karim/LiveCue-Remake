using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Catalog;

/// <summary>
/// The source cache of protocol 7.2: a successful read is reused for the cache life of C-44, callers
/// that arrive during a read share it, and a failure is never cached.
///
/// A read belongs to the relay, not to the caller that happened to start it: it runs under the relay's
/// stopping token, and a caller's own token only ends that caller's wait.
/// </summary>
internal sealed class CachedCatalogSource : ICatalogSource
{
    private readonly ICatalogSource _source;
    private readonly RelayContext _context;
    private readonly Lock _gate = new();

    private IReadOnlyList<RawCatalogEntry>? _cached;
    private long _cachedAt;
    private Task<IReadOnlyList<RawCatalogEntry>>? _reading;

    public CachedCatalogSource(ICatalogSource source, RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        _source = source;
        _context = context;
    }

    public Task<IReadOnlyList<RawCatalogEntry>> ReadAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<RawCatalogEntry>>(ct);
        }

        Task<IReadOnlyList<RawCatalogEntry>> reading;
        lock (_gate)
        {
            if (_cached is { } cached && _context.Clock.GetElapsedTime(_cachedAt) < ProtocolConstants.CatalogCacheLife)
            {
                return Task.FromResult(cached);
            }

            if (_reading is null)
            {
                // Started on the pool, so that the source never runs under this lock or on a caller's stack.
                _reading = Task.Run(LoadAsync);

                // Every caller may have stopped waiting by the time a read fails.
                _ = _reading.ContinueWith(
                    static failed => _ = failed.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            reading = _reading;
        }

        return reading.WaitAsync(ct);
    }

    private async Task<IReadOnlyList<RawCatalogEntry>> LoadAsync()
    {
        try
        {
            var entries = await _source.ReadAsync(_context.Access.Stopping).ConfigureAwait(false)
                ?? throw new CatalogReadException();
            lock (_gate)
            {
                _cached = entries;
                _cachedAt = _context.Clock.GetTimestamp();
                _reading = null;
            }

            return entries;
        }
        catch
        {
            lock (_gate)
            {
                _reading = null;
            }

            throw;
        }
    }
}
