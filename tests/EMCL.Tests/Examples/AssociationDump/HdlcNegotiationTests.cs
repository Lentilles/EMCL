using EMCL.Examples.AssociationDump.Transport;
using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Tests.Examples.AssociationDump;

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
}
