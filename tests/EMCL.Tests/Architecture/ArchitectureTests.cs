using EMCL.Core.Values;
using EMCL.Client.Sessions;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Architecture;

public sealed class ArchitectureTests
{
    [Fact]
    public void KeepsProtocolAndClientIndependent()
    {
        var protocol = typeof(ApduCodec).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.True(!protocol.Any(n => n.StartsWith("EMCL.Framing", StringComparison.Ordinal) || n is "EMCL.Transport" or "EMCL.Objects"));
        Assert.DoesNotContain(typeof(DlmsClientSession).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.StartsWith("EMCL.Framing", StringComparison.Ordinal)
                || assembly.Name == "EMCL");
        Assert.True(!typeof(DlmsValue).Assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("EMCL", StringComparison.Ordinal)));
    }
}
