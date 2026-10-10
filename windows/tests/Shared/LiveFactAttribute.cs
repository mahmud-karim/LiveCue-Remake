using Xunit;

namespace LiveCue.TestSupport;

/// <summary>
/// A test that talks to something real (test plan section 3). It is skipped unless the named
/// environment variable is <c>1</c>. Put <c>[Trait("Category", "Live")]</c> beside it as well: every
/// ordinary run filters that category out. Example: <c>[LiveFact("LIVECUE_LIVE_CODEX")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(string variable)
    {
        Variable = variable;
        if (!string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal))
        {
            Skip = $"Opt-in live test: set {variable}=1 to run it.";
        }
    }

    /// <summary>The environment variable that opts in.</summary>
    public string Variable { get; }
}
