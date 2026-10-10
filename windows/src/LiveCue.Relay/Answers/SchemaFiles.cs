namespace LiveCue.Relay.Answers;

/// <summary>
/// The two output schemas of protocol 12.5, embedded in the assembly and rewritten into
/// <c>&lt;data folder&gt;\schemas\</c> at each start (architecture 3.5).
/// </summary>
internal static class SchemaFiles
{
    public const string FolderName = "schemas";
    public const string AssistFileName = "assist.schema.json";
    public const string SummaryFileName = "summary.schema.json";

    private const string ResourcePrefix = "LiveCue.Relay.Answers.Schemas.";

    /// <summary>The absolute path of the schema file for <paramref name="kind"/>.</summary>
    public static string PathFor(string dataDirectory, AnswerKind kind) => Path.Combine(
        dataDirectory, FolderName, kind == AnswerKind.Summary ? SummaryFileName : AssistFileName);

    /// <summary>Writes both files from the embedded copies, replacing whatever is there.</summary>
    public static void Rewrite(string dataDirectory)
    {
        var folder = Path.Combine(dataDirectory, FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, AssistFileName), Read(AssistFileName));
        File.WriteAllBytes(Path.Combine(folder, SummaryFileName), Read(SummaryFileName));
    }

    /// <summary>The embedded bytes of one schema file.</summary>
    public static byte[] Read(string fileName)
    {
        using var stream = typeof(SchemaFiles).Assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException("An embedded schema is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
