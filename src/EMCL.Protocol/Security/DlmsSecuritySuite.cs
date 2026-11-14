namespace EMCL.Protocol.Security;

/// <summary>Identifies the built-in symmetric security suites.</summary>
public enum DlmsSecuritySuite : byte
{
    /// <summary>DLMS/COSEM suite 0 using AES-GCM-128, HLS GMAC and AES Key Wrap.</summary>
    AesGcm128 = 0,
    /// <summary>SPODES suite 3 using Kuznyechik CTR and CMAC.</summary>
    Kuznyechik = 3
}
