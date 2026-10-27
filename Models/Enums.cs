namespace VendorPortal.Models;

public enum ApplicationStatus { Draft, PendingReview, UnderReview, CorrectionsRequired, Approved, Rejected }
public enum DocumentStatus { NotSubmitted, Pending, Approved, Rejected }
public enum ReviewDecision { Approved, Rejected }

public enum DocumentType
{
    ProprietorshipDocument, PanCard, GstCertificate, TradeLicense, BankPassbook,
    Affidavit, ProfilePhoto, MiningDocument, Itr3Years, StockRegister
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
        DocumentType.Affidavit => "Affidavit",
        DocumentType.ProfilePhoto => "Profile photo",
        DocumentType.MiningDocument => "Mining document",
        DocumentType.Itr3Years => "ITR for previous 3 years",
        DocumentType.StockRegister => "Stock register",
        _ => t.ToString()
    };

    public static string Badge(this ApplicationStatus s) => s switch
    {
        ApplicationStatus.Approved => "success",
        ApplicationStatus.Rejected => "danger",
        ApplicationStatus.CorrectionsRequired => "warning text-dark",
        ApplicationStatus.Draft => "secondary",
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
