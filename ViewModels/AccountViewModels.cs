using System.ComponentModel.DataAnnotations;

namespace VendorPortal.ViewModels;

public class RegisterViewModel
{
    [Required, StringLength(150), Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    private string _pan = "";
    private string _gst = "";
    private string _mobile = "";

    [Required(ErrorMessage = "PAN number is required."), Display(Name = "PAN number")]
    [RegularExpression(@"^[A-Z]{5}[0-9]{4}[A-Z]$",
        ErrorMessage = "PAN must be 10 characters: 5 letters, 4 digits, 1 letter (e.g. ABCDE1234F).")]
    public string PanNumber
    {
        get => _pan;
        set => _pan = (value ?? "").Trim().ToUpperInvariant();
    }

    [Required(ErrorMessage = "Mobile number is required."), Display(Name = "Mobile number")]
    [RegularExpression(@"^[6-9][0-9]{9}$",
        ErrorMessage = "Enter a 10-digit mobile number starting with 6, 7, 8 or 9.")]
    public string MobileNumber
    {
        get => _mobile;
        set => _mobile = (value ?? "").Replace(" ", "").Replace("-", "").Trim();
    }

    [Required(ErrorMessage = "GST number is required."), Display(Name = "GST number")]
    [RegularExpression(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        ErrorMessage = "GSTIN must be 15 characters (e.g. 22ABCDE1234F1Z5).")]
    public string GstNumber
    {
        get => _gst;
        set => _gst = (value ?? "").Trim().ToUpperInvariant();
    }

    [Required, EmailAddress, Display(Name = "Email address")]
    public string Email { get; set; } = "";
       

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

public class LoginViewModel
{
    [Required, EmailAddress, Display(Name = "Email address")]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required, EmailAddress, Display(Name = "Email address")]
    public string Email { get; set; } = "";
}

public class ResetPasswordViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Code { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "New password")]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}
