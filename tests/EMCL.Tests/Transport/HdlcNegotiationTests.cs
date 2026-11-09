using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Tests.Transport;

public sealed class HdlcNegotiationTests
{
    [Fact]
    public void UsesDefaultWhenUaOmitsParameters()
    {
        Assert.Equal(HdlcFrameFormat.DefaultInformationLength, HdlcNegotiation.MaximumOutgoingInformation([]));
    }

    [Fact]
    public void ParsesObservedUaAndCapsOutgoingInformationToSafeDefault()
    {
        var parameters = Convert.FromHexString("8180140502023B0602023B070400000001080400000001");

        Assert.Equal(HdlcFrameFormat.DefaultInformationLength,
            HdlcNegotiation.MaximumOutgoingInformation([.. parameters]));
    }

    [Theory]
    [InlineData("818003060100")]
    [InlineData("8180040601020601")]
    [InlineData("8180070605FFFFFFFFFF")]
    [InlineData("81800409020001")]
    public void RejectsMalformedOrUnsupportedUaParameters(string hex)
    {
        Assert.Throws<InvalidDataException>(() =>
            HdlcNegotiation.MaximumOutgoingInformation([.. Convert.FromHexString(hex)]));
    }
    [Theory]
    [InlineData(128, 128, "818012050180060180070400000001080400000001")]
    [InlineData(571, 1024, "8180140502023B06020400070400000001080400000001")]
    [InlineData(64, 571, "818014050200400602023B070400000001080400000001")]
    public void SnrmCarriesExactProposalAndOneFrameWindows(int transmit, int receive, string expected)
    {
        Assert.Equal(Convert.FromHexString(expected),
            HdlcNegotiation.CreateSnrmInformation(new(transmit, receive)).ToArray());
        Assert.Empty(HdlcNegotiation.CreateSnrmInformation(null));
    }

    [Theory]
    [InlineData(1024, 512, "8180140502023B06020100070400000001080400000001", 256, 512)]
    [InlineData(256, 1024, "8180140502023B06020400070400000001080400000001", 256, 571)]
    [InlineData(1024, 1024, "", 128, 128)]
    [InlineData(64, 32, "", 64, 32)]
    [InlineData(1024, 1024, "818003060140", 64, 128)]
    public void NegotiatesDirectionsAndDefaultsIndependently(int transmit, int receive, string ua,
        int effectiveTransmit, int effectiveReceive)
    {
        var proposed = new HdlcLinkParameters(transmit, receive);
        var information = Convert.FromHexString(ua).ToImmutableArray();
        var result = HdlcNegotiation.Negotiate(proposed, information);
        Assert.Equal(proposed, result.Proposed);
        Assert.Equal(information, result.UaInformation);
        Assert.Equal(new(effectiveTransmit, effectiveReceive), result.Effective);
        Assert.Equal(ua.Length == 0, result.Received is null);
    }

    [Fact]
    public void UnsolicitedLargeUaDoesNotEnlargeDefaultLocalLimits()
    {
        var result = HdlcNegotiation.Negotiate(null,
            [.. Convert.FromHexString("8180140502023B0602023B070400000001080400000001")]);
        Assert.Null(result.Proposed);
        Assert.Equal(new(571, 571, 1, 1), result.Received);
        Assert.Equal(new(), result.Effective);
    }

    [Fact]
    public void DiagnosticsPreserveOmittedParametersInsteadOfReportingThemAsReceivedDefaults()
    {
        var result = HdlcNegotiation.Negotiate(new(1024, 1024),
            [.. Convert.FromHexString("818003060140")]);
        Assert.Equal(new(null, 64, null, null), result.Received);
        Assert.Equal(new(64, 128, 1, 1), result.Effective);
    }

    [Theory]
    [InlineData(0, 128, 1, 1)]
    [InlineData(2, 128, 1, 1)]
    [InlineData(2031, 128, 1, 1)]
    [InlineData(128, -1, 1, 1)]
    [InlineData(128, 2031, 1, 1)]
    [InlineData(128, 128, 0, 1)]
    [InlineData(128, 128, 1, 2)]
    public void RejectsUnsupportedLocalParameters(int transmit, int receive, int windowTx, int windowRx)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HdlcNegotiation.CreateSnrmInformation(new(transmit, receive, windowTx, windowRx)));
    }

    [Theory]
    [InlineData("818006070400000002")]
    [InlineData("818006080400000002")]
    [InlineData("818006070400000008")]
    [InlineData("818003050102")]
    [InlineData("818003060101")]
    [InlineData("818004050207EF")]
    [InlineData("818006060180060180")]
    [InlineData("818003060200")]
    public void RejectsUaThatCannotSupportChannel(string hex)
    {
        Assert.Throws<InvalidDataException>(() =>
            HdlcNegotiation.Negotiate(new(1024, 1024), [.. Convert.FromHexString(hex)]));
    }
}
