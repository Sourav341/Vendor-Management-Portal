
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using VendorPortal.Models;

namespace VendorPortal.ViewModels;

public class VendorApplicationSubmitViewModel
{
    [Required(ErrorMessage = "Please select a certificate type.")]
    [RegularExpression(
        "^(Admin|Material)$",
        ErrorMessage = "Certificate Type must be Admin or Material.")]
    [StringLength(10)]
    public string Cert_Type { get; set; } = "";

    [StringLength(5000)]
    public string? Remarks { get; set; }

    public List<VendorDocumentUploadInput> Uploads { get; set; } = new();
}

public class VendorDocumentUploadInput
{
    public DocumentType DocumentType { get; set; }

    public IFormFile? File { get; set; }
}
