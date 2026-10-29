using System.ComponentModel.DataAnnotations;

namespace VendorPortal.ViewModels;

public class VendorProfileDetailsViewModel
{
    [Required(ErrorMessage = "Please select a certificate type.")]
    [RegularExpression(
        "^(Admin|Material)$",
        ErrorMessage = "Please select Admin or Material.")]
    [StringLength(10)]
    public string Cert_Type { get; set; } = "";

    [StringLength(5000)]
    public string? Remarks { get; set; }
}