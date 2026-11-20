
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using VendorPortal.Models;

namespace VendorPortal.ViewModels;

public class VendorApplicationSubmitViewModel
{
    public List<VendorDocumentUploadInput> Uploads { get; set; } = new();
}

public class VendorDocumentUploadInput
{
    public DocumentType DocumentType { get; set; }

    public IFormFile? File { get; set; }
}
