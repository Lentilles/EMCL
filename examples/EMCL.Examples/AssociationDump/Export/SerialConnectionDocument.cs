namespace EMCL.Examples.AssociationDump.Export;

internal sealed record SerialConnectionDocument(string PortName, int BaudRate, int DataBits,
    string Parity, string StopBits, string Handshake, bool DtrEnable, bool RtsEnable);
