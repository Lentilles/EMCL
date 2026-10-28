using EMCL.Examples.AssociationDump.Configuration;

namespace EMCL.Tests.Examples.AssociationDump;

public sealed class DefaultConfigurationFileTests
{
    [Fact]
    public async Task PrefersLocalConfigurationAndFallsBackToBundledExample()
    {
        using var directory = new ExportTestDirectory();
        string example = Path.Combine(directory.Path, DefaultConfigurationFile.ExampleFileName);
        string local = Path.Combine(directory.Path, DefaultConfigurationFile.LocalFileName);
        await File.WriteAllTextAsync(example, "{}", TestContext.Current.CancellationToken);

        Assert.Equal(example, DefaultConfigurationFile.Resolve(directory.Path));

        await File.WriteAllTextAsync(local, "{}", TestContext.Current.CancellationToken);
        Assert.Equal(local, DefaultConfigurationFile.Resolve(directory.Path));
    }
}
