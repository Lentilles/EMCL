using EMCL.Core.Values;
using EMCL.Client.Sessions;
using EMCL.Profiles.BlueBook;
using EMCL.Profiles.Spodes;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Architecture;

public sealed class ArchitectureTests
{
    [Fact]
    public void KeepsProtocolAndProfilesIndependent()
    {
        var protocol = typeof(ApduCodec).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.True(!protocol.Any(n => n.StartsWith("EMCL.Framing", StringComparison.Ordinal) || n is "EMCL.Transport" or "EMCL.Objects"));
        Assert.True(!typeof(BlueBook16Profile).Assembly.GetReferencedAssemblies().Any(a => a.Name == "EMCL.Profiles.Spodes"));
        Assert.True(!typeof(Spodes4Profile).Assembly.GetReferencedAssemblies().Any(a => a.Name == "EMCL.Profiles.BlueBook"));
        Assert.DoesNotContain(typeof(DlmsClientSession).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.StartsWith("EMCL.Framing", StringComparison.Ordinal)
                || assembly.Name!.StartsWith("EMCL.Profiles", StringComparison.Ordinal) || assembly.Name == "EMCL");
        Assert.True(!typeof(DlmsValue).Assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("EMCL", StringComparison.Ordinal)));
    }
}
