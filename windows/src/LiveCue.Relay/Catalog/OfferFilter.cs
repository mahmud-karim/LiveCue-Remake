namespace LiveCue.Relay.Catalog;

/// <summary>
/// What is offered of the raw catalogue (protocol 7.1).
///
/// First version (foundation): the shape check of C-06 only. Dropping hidden entries, the effort rules
/// of C-05 and C-07 and the display name rule are work package WP-W03.
/// </summary>
internal static class OfferFilter
{
    private const string Prefix = "gpt-";

    /// <summary>C-06, checked by hand: "gpt-" followed by one or more of lower-case letters, digits, "." and "-".</summary>
    public static bool IsModelIdShape(ReadOnlySpan<char> id)
    {
        if (id.Length <= Prefix.Length || !id.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in id[Prefix.Length..])
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not ('.' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
