namespace VendorPortal.Models;

public enum ApplicationStatus { Draft, PendingReview, UnderReview, CorrectionsRequired, Approved, Rejected }
public enum DocumentStatus { NotSubmitted, Pending, Approved, Rejected }
public enum ReviewDecision { Approved, Rejected }
public enum CertificateRevalidationStatus { Open, Approved }
public enum AdditionalDetailStatus { PendingReview, Approved, Rejected, CorrectionsRequired, Superseded, RemovedByAdmin }

public enum DocumentType
{
    ProprietorshipDocument, PanCard, GstCertificate, TradeLicense, BankPassbook,
    Affidavit, ProfilePhoto, MiningDocument, Itr3Years, StockRegister, ShopPicture,
    AffidavitNoBloodRelation, ItemLicense
}

public static class EnumExtensions
{
    public static string DisplayName(this DocumentType t) => t switch
    {
        DocumentType.ProprietorshipDocument => "Proprietorship document",
        DocumentType.PanCard => "PAN card",
        DocumentType.GstCertificate => "GST certificate",
        DocumentType.TradeLicense => "Trade license",
        DocumentType.BankPassbook => "Bank passbook",
        DocumentType.Affidavit => "Affidavit (BlackList Certificate)",
        DocumentType.AffidavitNoBloodRelation => "Affidavit (No blood relation member)",
        DocumentType.ItemLicense => "Item License",
        DocumentType.ProfilePhoto => "Profile photo",
        DocumentType.MiningDocument => "Mining document",
        DocumentType.Itr3Years => "ITR for previous 3 years",
        DocumentType.StockRegister => "Stock register",
        DocumentType.ShopPicture => "Shop Picture",
        _ => t.ToString()
    };

    public static int DisplayOrder(this DocumentType t) => t switch
    {
        DocumentType.ProprietorshipDocument => 0,
        DocumentType.PanCard => 1,
        DocumentType.GstCertificate => 2,
        DocumentType.TradeLicense => 3,
        DocumentType.ItemLicense => 4,
        DocumentType.BankPassbook => 5,
        DocumentType.Affidavit => 6,
        DocumentType.AffidavitNoBloodRelation => 7,
        DocumentType.ProfilePhoto => 8,
        DocumentType.MiningDocument => 9,
        DocumentType.Itr3Years => 10,
        DocumentType.StockRegister => 11,
        DocumentType.ShopPicture => 12,
        _ => int.MaxValue
    };

    public static string Badge(this ApplicationStatus s) => s switch
    {
        ApplicationStatus.Approved => "success",
        ApplicationStatus.Rejected => "danger",
        ApplicationStatus.CorrectionsRequired => "warning text-dark",
        ApplicationStatus.Draft => "secondary",
        _ => "primary"
    };

    public static string Badge(this AdditionalDetailStatus s) => s switch
    {
        AdditionalDetailStatus.Approved => "success",
        AdditionalDetailStatus.Rejected => "danger",
        AdditionalDetailStatus.CorrectionsRequired => "warning text-dark",
        AdditionalDetailStatus.Superseded => "secondary",
        AdditionalDetailStatus.RemovedByAdmin => "dark",
        _ => "primary"
    };

    public static string Badge(this DocumentStatus s) => s switch
    {
        DocumentStatus.Approved => "success",
        DocumentStatus.Rejected => "danger",
        DocumentStatus.Pending => "primary",
        _ => "secondary"
    };
}
