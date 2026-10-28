namespace EMCL.Examples.AssociationDump.Configuration;

internal static class DefaultConfigurationFile
{
    internal const string LocalFileName = "meter.local.json";
    internal const string ExampleFileName = "meter.example.json";

    internal static string Resolve(string applicationDirectory)
    {
        string localPath = Path.Combine(applicationDirectory, LocalFileName);
        return File.Exists(localPath) ? localPath : Path.Combine(applicationDirectory, ExampleFileName);
    }
}
