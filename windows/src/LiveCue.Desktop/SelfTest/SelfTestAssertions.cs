using System.Text.RegularExpressions;
using System.Windows;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// The named assertions of the self-test (architecture 3.13; W-design 5.13). Each one that holds prints
/// "ok &lt;name&gt;"; the first that does not ends the run with its name. It also keeps what the three
/// end-of-run assertions need: every text the window showed or said in any step, and every break of
/// the crisp-text rule.
/// </summary>
internal sealed partial class SelfTestAssertions(SelfTestLog log, string? forcedFailure)
{
    private readonly List<string> _passed = [];
    private readonly HashSet<string> _texts = new(StringComparer.Ordinal);
    private readonly List<string> _crispTextProblems = [];
    private int _observations;

    /// <summary>The names that passed, in order.</summary>
    public IReadOnlyList<string> Passed => _passed;

    /// <summary>Where the run is, for a failure that no assertion names (a wait that ran out).</summary>
    public string Step { get; set; } = "start";

    /// <summary>
    /// Runs the checks of one assertion. They report through <see cref="VisualChecks.Require"/>; a
    /// failure leaves here as a <see cref="SelfTestFailedException"/> that carries the name.
    /// </summary>
    public void Check(string name, Action checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        try
        {
            checks();

            // The hidden test switch: the named assertion fails although what it checks holds, so that a
            // test can see how a failure is reported without breaking the window.
            VisualChecks.Require(
                !string.Equals(name, forcedFailure, StringComparison.Ordinal),
                $"the assertion not to be named by {SelfTestRunner.FailSwitch}");
        }
        catch (SelfTestFailedException failure) when (failure.Assertion is null)
        {
            throw new SelfTestFailedException(failure.Message, name);
        }

        _passed.Add(name);
        log.Write("ok " + name);
    }

    /// <summary>
    /// Looks at the window as it is after a step: remembers every text in it, and every text that an
    /// effect, an opacity mask, a bitmap cache or a resting opacity would blur. Returns how many texts
    /// were looked at.
    /// </summary>
    public int Observe(FrameworkElement root)
    {
        _observations++;
        foreach (var text in VisualChecks.SpokenAndShownTexts(root))
        {
            if (text.Length > 0)
            {
                _texts.Add(text);
            }
        }

        var problems = VisualChecks.CrispTextProblems(root, out var texts);
        foreach (var problem in problems)
        {
            _crispTextProblems.Add($"{Step}: {problem}");
        }

        return texts;
    }

    /// <summary>
    /// <c>no-token</c>: nothing the window showed or said in any step contains the synthetic token, or
    /// anything of a token's shape (43 characters of the token alphabet in a row).
    /// </summary>
    public void CheckNoToken() => Check("no-token", () =>
    {
        VisualChecks.Require(_observations > 0 && _texts.Count > 0, "the window to have been looked at");
        var leak = _texts.FirstOrDefault(text => text.Contains(SyntheticData.Token, StringComparison.Ordinal));
        VisualChecks.Require(leak is null, "no text, tooltip or automation name to contain the synthetic token");
        var shaped = _texts.FirstOrDefault(text => TokenShape().IsMatch(text));
        VisualChecks.Require(shaped is null, "no text, tooltip or automation name to look like a pairing token");
    });

    /// <summary><c>crisp-text</c>: in no step did an element that blurs its subtree have a text below it.</summary>
    public void CheckCrispText() => Check("crisp-text", () =>
        VisualChecks.Require(_crispTextProblems.Count == 0, _crispTextProblems.FirstOrDefault() ?? string.Empty));

    /// <summary>How many different texts were collected, for the log.</summary>
    public int TextCount => _texts.Count;

    [GeneratedRegex("[A-Za-z0-9_-]{43}")]
    private static partial Regex TokenShape();
}
