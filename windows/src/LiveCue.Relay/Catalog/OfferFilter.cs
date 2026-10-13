using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Catalog;

/// <summary>
/// What is offered of the raw catalogue (protocol 7.1). An entry is offered when it is not hidden, its
/// id has the shape C-06, and at least one reasoning effort is left after filtering.
/// </summary>
internal static class OfferFilter
{
    private const string Prefix = "gpt-";
    private const string None = "none";

    /// <summary>
    /// C-06, checked by hand over the whole text: "gpt-" followed by one or more of lower-case letters,
    /// digits, "." and "-", and at most <see cref="ProtocolConstants.ModelIdMaxLength"/> characters in
    /// all. The id ends up on a command line.
    /// </summary>
    public static bool IsModelIdShape(ReadOnlySpan<char> id)
    {
        if (id.Length <= Prefix.Length
            || id.Length > ProtocolConstants.ModelIdMaxLength
            || !id.StartsWith(Prefix, StringComparison.Ordinal))
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

    /// <summary>The offered form of every entry that may be offered, in the order received.</summary>
    public static List<ModelOffer> Apply(IReadOnlyList<RawCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var offers = new List<ModelOffer>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry is null || entry.Hidden || entry.Model is null || !IsModelIdShape(entry.Model))
            {
                continue;
            }

            var efforts = Efforts(entry);
            if (efforts.Count == 0)
            {
                continue;
            }

            offers.Add(new ModelOffer(
                entry.Model,
                string.IsNullOrEmpty(entry.DisplayName) ? entry.Model : entry.DisplayName,
                efforts));
        }

        return offers;
    }

    /// <summary>
    /// The efforts of C-05 that the entry advertises, in its order and without duplicates. For the ids of
    /// C-07, "none" comes first whether or not it was advertised.
    /// </summary>
    private static List<string> Efforts(RawCatalogEntry entry)
    {
        var alwaysNone = Contains(ProtocolConstants.AlwaysNoneModels, entry.Model);
        var efforts = new List<string>();
        if (alwaysNone)
        {
            efforts.Add(None);
        }

        foreach (var advertised in entry.ReasoningEfforts ?? [])
        {
            // The protocol's own text of the effort is kept, never the catalogue's.
            var known = Known(advertised);
            if (known is null || Contains(efforts, known))
            {
                continue;
            }

            efforts.Add(known);
        }

        return efforts;
    }

    private static string? Known(string? effort)
    {
        foreach (var known in ProtocolConstants.ReasoningEfforts)
        {
            if (string.Equals(known, effort, StringComparison.Ordinal))
            {
                return known;
            }
        }

        return null;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (var item in list)
        {
            if (string.Equals(item, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
