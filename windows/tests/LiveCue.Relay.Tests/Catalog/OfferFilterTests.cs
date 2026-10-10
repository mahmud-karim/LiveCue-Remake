using LiveCue.Relay.Catalog;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Catalog;

/// <summary>
/// What is offered of Codex's model list (protocol 7.1; statements P2-038 to P2-040; security test
/// ST-13 for the shape C-06).
/// </summary>
public sealed class OfferFilterTests
{
    /// <summary>U+2028, which several regular-expression engines take for the end of a line.</summary>
    private static readonly string LineSeparator = ((char)0x2028).ToString();

    [Theory]
    [InlineData("gpt-5.6-sol")]
    [InlineData("gpt-5.6-terra")]
    [InlineData("gpt-5.6-luna")]
    [InlineData("gpt-5.5")]
    public void Always_none_models_get_none_first(string id)
    {
        var offer = Assert.Single(OfferFilter.Apply([Entry(id, "low", "high", "ultra")]));
        var advertised = Assert.Single(OfferFilter.Apply([Entry(id, "low", "none", "high", "none")]));

        Assert.Equal(["none", "low", "high"], offer.ReasoningEfforts);

        // "none" comes first also when Codex advertises it somewhere else, and only once.
        Assert.Equal(["none", "low", "high"], advertised.ReasoningEfforts);
        Assert.Contains(id, ProtocolConstants.AlwaysNoneModels);
        Assert.Equal(4, ProtocolConstants.AlwaysNoneModels.Count);
    }

    [Theory]
    [InlineData("gpt-6-astra")]
    [InlineData("gpt-5.3-codex-spark")]
    public void Other_models_keep_only_known_efforts(string id)
    {
        var offer = Assert.Single(OfferFilter.Apply([Entry(id, "low", "medium", "ultra")]));

        Assert.Equal(["low", "medium"], offer.ReasoningEfforts);
    }

    [Fact]
    public void Hidden_bad_id_and_effortless_entries_are_not_offered_and_a_nameless_one_uses_its_id()
    {
        var offers = OfferFilter.Apply(
        [
            new RawCatalogEntry("gpt-hidden", "Hidden", Hidden: true, ["low"]),
            new RawCatalogEntry("o4-mini", "Wrong prefix", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-", "Nothing after the prefix", Hidden: false, ["low"]),
            new RawCatalogEntry("GPT-5", "Upper case", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-5 turbo", "A space", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-5;shell=true", "Argument text", Hidden: false, ["low"]),
            new RawCatalogEntry("--config", "An option", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-no-efforts", "No efforts", Hidden: false, []),
            new RawCatalogEntry("gpt-unknown-efforts", "Unknown efforts", Hidden: false, ["ultra", "LOW", "low "]),
            new RawCatalogEntry("gpt-nameless", null, Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-empty-name", string.Empty, Hidden: false, ["medium"]),
            new RawCatalogEntry("gpt-named", "Named", Hidden: false, ["minimal", "xhigh", "max"]),
        ]);

        Assert.Equal(["gpt-nameless", "gpt-empty-name", "gpt-named"], offers.Select(offer => offer.Id));
        Assert.Equal(["gpt-nameless", "gpt-empty-name", "Named"], offers.Select(offer => offer.Name));
        Assert.Equal(["minimal", "xhigh", "max"], offers[2].ReasoningEfforts);
        Assert.All(offers, offer => Assert.NotEmpty(offer.ReasoningEfforts));
    }

    [Fact]
    public void Efforts_keep_their_order_and_lose_their_duplicates()
    {
        var offer = Assert.Single(OfferFilter.Apply([Entry("gpt-6-astra", "high", "none", "low", "high", "ultra", "none", "max")]));

        // For a model outside C-07, "none" is an effort like any other and stays where it was.
        Assert.Equal(["high", "none", "low", "max"], offer.ReasoningEfforts);
        Assert.Equal(
            ["none", "minimal", "low", "medium", "high", "xhigh", "max"],
            ProtocolConstants.ReasoningEfforts);
    }

    [Fact]
    public void A_model_id_with_a_trailing_line_break_or_longer_than_64_is_not_offered()
    {
        var sixtyFour = "gpt-" + new string('a', 60);
        string[] refused =
        [
            "gpt-5\n",
            "gpt-5\r\n",
            "gpt-5\r",
            "gpt-5" + LineSeparator,
            "\ngpt-5",
            "gpt-5\0",
            sixtyFour + "a",
            "gpt-" + new string('a', 10_000),
        ];

        Assert.Equal(64, ProtocolConstants.ModelIdMaxLength);
        Assert.Equal(64, sixtyFour.Length);
        Assert.True(OfferFilter.IsModelIdShape(sixtyFour));
        Assert.True(OfferFilter.IsModelIdShape("gpt-5"));
        Assert.Equal(sixtyFour, Assert.Single(OfferFilter.Apply([Entry(sixtyFour, "low")])).Id);
        Assert.All(refused, id =>
        {
            Assert.False(OfferFilter.IsModelIdShape(id));
            Assert.Empty(OfferFilter.Apply([Entry(id, "low")]));
        });
    }

    [Theory]
    [InlineData("gpt-5", true)]
    [InlineData("gpt-5.6-sol", true)]
    [InlineData("gpt-a.b-c.9", true)]
    [InlineData("gpt--", true)]
    [InlineData("gpt-", false)]
    [InlineData("gpt", false)]
    [InlineData("", false)]
    [InlineData("gpt-5_1", false)]
    [InlineData("gpt-5/1", false)]
    [InlineData(@"gpt-5\1", false)]
    [InlineData("gpt-5 ", false)]
    [InlineData(" gpt-5", false)]
    [InlineData("gpt-é", false)]
    [InlineData("gpt-５", false)]
    [InlineData("gpt-5\"", false)]
    [InlineData("xgpt-5", false)]
    public void The_shape_of_a_model_id(string id, bool expected)
    {
        Assert.Equal(expected, OfferFilter.IsModelIdShape(id));
    }

    private static RawCatalogEntry Entry(string id, params string[] efforts) => new(id, "Name", Hidden: false, efforts);
}
