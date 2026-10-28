namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record OutputSettings
{
    public required string Path
    {
        get; init;
    }
    public bool Overwrite
    {
        get; init;
    }
}
