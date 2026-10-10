namespace LiveCue.Desktop.Infrastructure;

/// <summary>
/// Exception isolation per event (WIN-10): every subscriber of an event is called, also after an earlier
/// one threw, and what was thrown is handed back instead of travelling up into the code that raised the
/// event. So one page that cannot show an event never keeps the other pages, or the events after it,
/// from being shown.
/// </summary>
internal static class SafeInvoke
{
    /// <summary>
    /// Calls every subscriber of <paramref name="handlers"/> in order. Returns the first exception a
    /// subscriber threw, or null when none did.
    /// </summary>
    public static Exception? Each<T>(EventHandler<T>? handlers, object sender, T args)
    {
        if (handlers is null)
        {
            return null;
        }

        Exception? first = null;
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)handler)(sender, args);
            }
#pragma warning disable CA1031 // WIN-10: a subscriber that throws never stops the others.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                first ??= exception;
            }
        }

        return first;
    }

    /// <summary>Runs <paramref name="action"/>. Returns what it threw, or null.</summary>
    public static Exception? Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
            return null;
        }
#pragma warning disable CA1031 // The caller decides what a failure becomes; nothing escapes from here.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return exception;
        }
    }
}
