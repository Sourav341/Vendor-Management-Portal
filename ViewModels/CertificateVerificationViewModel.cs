/// <summary>
/// Safe public fields displayed by the certificate QR verification page.
/// </summary>

using System.ComponentModel.DataAnnotations;

namespace VendorPortal.ViewModels;

public sealed class CertificateVerificationViewModel
{
    public string CertificateNumber { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string? CompanyFirmName { get; set; }
    public string District { get; set; } = string.Empty;
    public string UcanNumber { get; set; } = string.Empty;
    public string? RegistrationType { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ValidUntilUtc { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsValid { get; set; }
    public string Status { get; set; } = string.Empty;
}
