/// <summary>
/// Safe public fields displayed by the certificate QR verification page.
/// </summary>



namespace VendorPortal.ViewModels;

public sealed class CertificateVerificationViewModel
{

    public string VendorName { get; set; } = string.Empty;
    public string? CompanyFirmName { get; set; }
    public string District { get; set; } = string.Empty;
    public string? WorkType { get; set; }
    public string? SubWorkType { get; set; }
    public string UcanNumber { get; set; } = string.Empty;

    public DateTime IssuedAtUtc { get; set; }
    public DateTime ValidUntilUtc { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsValid { get; set; }
    public string Status { get; set; } = string.Empty;
}
