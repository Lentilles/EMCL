using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Protocol.Apdu;

namespace EMCL.Objects.Spodes;

/// <summary>Delivers a SPODES Push using the port, peer and association selected by the application.</summary>
public interface ISpodesPushTransport
{
    /// <summary>Sends one notification and waits for the confirmation expected by the selected mode.</summary>
    /// <param name="source">The Push Setup object's logical name.</param>
    /// <param name="destination">The configured send destination and method structure.</param>
    /// <param name="notification">The notification to deliver.</param>
    /// <param name="cancellationToken">A token that cancels this attempt.</param>
    /// <returns>True after the applicable application or link confirmation.</returns>
    ValueTask<bool> SendAsync(LogicalName source, DlmsValue destination, DataNotification notification,
        CancellationToken cancellationToken);
}
