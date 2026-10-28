using EMCL.Client.Sessions;
using EMCL.Core.Objects;

namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record LoadedConfiguration(MeterConfiguration Settings, ClientSessionOptions Session,
    LogicalName AssociationName, TimeSpan ConnectTimeout, string OutputPath);
