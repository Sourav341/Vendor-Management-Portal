using System.ComponentModel.DataAnnotations;

namespace VendorPortal.ViewModels;

public class VendorProfileDetailsViewModel
{
    [Required(ErrorMessage = "Please select a work type.")]
    [StringLength(10)]
    public string Cert_Type { get; set; } = "";

    [Required(ErrorMessage = "Please select a sub-work type.")]
    [StringLength(100)]
    public string SubWorkType { get; set; } = "";

    [StringLength(4000)]
    public string? Remarks { get; set; }
}