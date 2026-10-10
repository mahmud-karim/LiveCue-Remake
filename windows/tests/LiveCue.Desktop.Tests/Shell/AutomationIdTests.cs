using System.Reflection;

namespace LiveCue.Desktop.Tests.Shell;

/// <summary>The automation ids of screens section 9, as constants.</summary>
public sealed class AutomationIdTests
{
    [Fact]
    public void Ids_are_unique()
    {
        var constants = typeof(AutomationIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

        // Each constant is named exactly like its value, so two cannot share a value.
        Assert.All(constants, constant => Assert.Equal(constant.Key, constant.Value));
        Assert.Equal(constants.Count, constants.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(58, constants.Count);
    }

    [Theory]
    [InlineData("StartRelayButton")]
    [InlineData("PauseRelayButton")]
    [InlineData("StopRelayButton")]
    [InlineData("RelayStatusText")]
    [InlineData("PhoneStatusText")]
    [InlineData("PcAddressText")]
    [InlineData("ReplyHint")]
    [InlineData("ReplyTiming")]
    [InlineData("PrerequisiteList")]
    [InlineData("LiveSetUpConnectionButton")]
    [InlineData("NavSettings")]
    [InlineData("SidebarPhoneStatus")]
    [InlineData("PairingQr")]
    [InlineData("PairingVerifyNotice")]
    [InlineData("ConfirmResetPairingButton")]
    [InlineData("ActivityLog")]
    [InlineData("ClearActivityButton")]
    [InlineData("PublicPathPrefixField")]
    [InlineData("SettingsFileNotice")]
    [InlineData("MappingCommandText")]
    [InlineData("RemoveMappingDialog")]
    [InlineData("CheckAssistantButton")]
    [InlineData("LoadedModelStatus")]
    [InlineData("ThirdPartyNoticesButton")]
    [InlineData("SourceCodeButton")]
    public void An_id_of_the_screen_design_is_a_constant(string id)
    {
        Assert.Equal(id, typeof(AutomationIds).GetField(id)?.GetRawConstantValue());
    }

    [Fact]
    public void There_is_no_id_for_what_is_not_built()
    {
        // R-01: the desktop never holds the pairing text, so there is nothing to copy it with.
        Assert.DoesNotContain(
            typeof(AutomationIds).GetFields().Select(field => field.Name),
            name => name.Contains("PairingText", StringComparison.OrdinalIgnoreCase));
    }
}
