using System.ComponentModel.DataAnnotations;
using VendorPortal.Models;

namespace VendorPortal.ViewModels;

public class AdditionalDetailsPageViewModel
{
    public Guid? ReplacesRequestBatchId { get; set; }
    public List<AdditionalDetailEntryInput> Entries { get; set; } = new() { new() };
    public List<VendorAdditionalDetailRequest> Requests { get; set; } = new();
}

public class AdditionalDetailEntryInput
{
    [Required]
    [StringLength(100)]
    public string District { get; set; } = "";

    [Required]
    [StringLength(100)]
    public string WorkType { get; set; } = "";

    [Required]
    [StringLength(150)]
    public string SubWorkType { get; set; } = "";

    [StringLength(4000)]
    public string? Remarks { get; set; }
}
