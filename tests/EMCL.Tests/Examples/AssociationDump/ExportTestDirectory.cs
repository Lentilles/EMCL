namespace EMCL.Tests.Examples.AssociationDump;

internal sealed class ExportTestDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"emcl-association-{Guid.NewGuid():N}");

    internal ExportTestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
