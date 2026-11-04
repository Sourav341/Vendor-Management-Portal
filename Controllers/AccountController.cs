using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using VendorPortal.Data;
using VendorPortal.Models;
using VendorPortal.Services;
using VendorPortal.ViewModels;

namespace VendorPortal.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ApplicationDbContext _db;
    private readonly IAppEmailSender _email;
    private readonly IHostEnvironment _env;

    public AccountController(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        ApplicationDbContext db,
        IAppEmailSender email,
        IHostEnvironment env)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
        _email = email;
        _env = env;
    }

    // ---------------- Register ----------------

    private static readonly string[] GenderOptions =
    {
        "Male",
        "Female",
        "Other"
    };

    /// <summary>
    /// Loads distinct district names and their codes from the existing
    /// government master table. The vendor sees district names only;
    /// DistrictCode is used as the dropdown value for server-side lookup.
    /// </summary>
    private async Task LoadRegistrationDropdownsAsync(RegisterViewModel? model = null)
    {
        var districts = await _db.MasterDistricts
            .AsNoTracking()
            .Where(d => d.DistrictCode > 0 && d.DistrictName != "")
            .Select(d => new
            {
                d.DistrictCode,
                d.DistrictName
            })
            .Distinct()
            .OrderBy(d => d.DistrictName)
            .ToListAsync();

        ViewBag.Districts = new SelectList(
            districts,
            nameof(MasterDistrict.DistrictCode),
            nameof(MasterDistrict.DistrictName),
            model?.DistrictCode
        );

        ViewBag.Genders = new SelectList(
            GenderOptions,
            model?.Gender
        );
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Register()
    {
        var model = new RegisterViewModel();
        await LoadRegistrationDropdownsAsync(model);
        return View(model);
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel m)
    {
        // Resolve the selected DistrictCode against the government master table.
        // Only the district name is saved in VendorProfile. The code remains
        // available in the master table for the future UCAN generation step.
        string? selectedDistrictName = null;

        if (m.DistrictCode <= 0)
        {
            ModelState.AddModelError(
                nameof(m.DistrictCode),
                "Please select a valid district."
            );
        }
        else
        {
            var districtNames = await _db.MasterDistricts
                .AsNoTracking()
                .Where(d => d.DistrictCode == m.DistrictCode)
                .Select(d => d.DistrictName)
                .Distinct()
                .ToListAsync();

            if (districtNames.Count == 0)
            {
                ModelState.AddModelError(
                    nameof(m.DistrictCode),
                    "The selected district is not available in the master list."
                );
            }
            else if (districtNames.Count > 1)
            {
                // A district code should map to one district name, even though
                // the code is repeated for its blocks, panchayats and villages.
                ModelState.AddModelError(
                    nameof(m.DistrictCode),
                    "The district master data contains conflicting names for this code. Please contact the administrator."
                );
            }
            else
            {
                selectedDistrictName = districtNames[0];
            }
        }

        // Validate gender when supplied.
        if (!string.IsNullOrWhiteSpace(m.Gender) &&
            !GenderOptions.Contains(m.Gender, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(m.Gender),
                "Please select a valid gender."
            );
        }

        if (!ModelState.IsValid)
        {
            await LoadRegistrationDropdownsAsync(m);
            return View(m);
        }

        var pan = m.PanNumber.Trim().ToUpperInvariant();
        var gst = m.GstNumber.Trim().ToUpperInvariant();
        var mobile = m.MobileNumber.Trim();

        if (await _db.VendorProfiles.AnyAsync(p => p.PanNumber == pan))
        {
            ModelState.AddModelError(
                nameof(m.PanNumber),
                "This PAN is already registered."
            );
        }

        if (await _db.VendorProfiles.AnyAsync(p => p.GstNumber == gst))
        {
            ModelState.AddModelError(
                nameof(m.GstNumber),
                "This GST number is already registered."
            );
        }

        if (!ModelState.IsValid)
        {
            await LoadRegistrationDropdownsAsync(m);
            return View(m);
        }

        var email = m.Email.Trim();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            PhoneNumber = mobile,
            EmailConfirmed = true
        };

        var result = await _users.CreateAsync(user, m.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            await LoadRegistrationDropdownsAsync(m);
            return View(m);
        }

        var roleResult = await _users.AddToRoleAsync(user, "Vendor");

        if (!roleResult.Succeeded)
        {
            foreach (var error in roleResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            await _users.DeleteAsync(user);
            await LoadRegistrationDropdownsAsync(m);
            return View(m);
        }

        // Store only the district name in VendorProfiles.
        // The selected DistrictCode is deliberately NOT stored in VendorProfile.
        // UCAN generation later can look up the code from tbl_Master_district
        // using this verified district name.
        var vendorProfile = new VendorProfile
        {
            UserId = user.Id,
            FullName = m.FullName.Trim(),
            PanNumber = pan,
            GstNumber = gst,
            MobileNumber = mobile,
            District = selectedDistrictName!,
            TradeName = m.TradeName?.Trim(),
            Gender = m.Gender,
            DOB = m.DOB,
            Father_Husband_Name = m.Father_Husband_Name?.Trim(),
            Trade_Address = m.Trade_Address?.Trim()
        };

        _db.VendorProfiles.Add(vendorProfile);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Remove the newly created Identity user if its profile could not
            // be saved, preventing an orphaned login account.
            await _users.DeleteAsync(user);

            ModelState.AddModelError(
                string.Empty,
                "Registration failed. Please verify your registration details and try again."
            );

            await LoadRegistrationDropdownsAsync(m);
            return View(m);
        }

        TempData["Success"] =
            "Registration successful. Please log in with your email and password.";

        return RedirectToAction(nameof(Login));
    }

    // ---------------- Login / Logout ----------------

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel m, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
            return View(m);

        var user = await _users.FindByEmailAsync(m.Email.Trim());

        var result = await _signIn.PasswordSignInAsync(
            user?.UserName ?? m.Email,
            m.Password,
            m.RememberMe,
            lockoutOnFailure: true
        );

        if (result.Succeeded)
        {
            if (Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl!);

            return user != null && await _users.IsInRoleAsync(user, "Admin")
                ? RedirectToAction("Index", "Admin")
                : RedirectToAction("Dashboard", "Vendor");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(
                string.Empty,
                "Account locked after too many failed attempts. Try again in 15 minutes."
            );
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
        }

        return View(m);
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    // ---------------- Forgot / Reset password ----------------
    // Note: the reset link is only shown on screen in Development.
    // Plug in a real IAppEmailSender before production.

    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel m)
    {
        if (!ModelState.IsValid)
            return View(m);

        string? devLink = null;
        var user = await _users.FindByEmailAsync(m.Email.Trim());

        if (user != null)
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = Url.Action(
                nameof(ResetPassword),
                "Account",
                new { code, email = user.Email },
                Request.Scheme
            )!;

            await _email.SendAsync(
                user.Email!,
                "Reset your password",
                $"Reset your password by <a href='{link}'>clicking here</a>."
            );

            if (_env.IsDevelopment())
                devLink = link;
        }

        return View("ForgotPasswordConfirmation", devLink);
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPassword(string? code, string? email)
    {
        if (code == null || email == null)
            return RedirectToAction(nameof(Login));

        return View(new ResetPasswordViewModel { Code = code, Email = email });
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel m)
    {
        if (!ModelState.IsValid)
            return View(m);

        var user = await _users.FindByEmailAsync(m.Email);
        if (user == null)
            return RedirectToAction(nameof(ResetPasswordDone));

        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(m.Code));
            var result = await _users.ResetPasswordAsync(user, token, m.Password);

            if (result.Succeeded)
                return RedirectToAction(nameof(ResetPasswordDone));

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "Invalid reset link.");
        }

        return View(m);
    }

    [AllowAnonymous]
    public IActionResult ResetPasswordDone() => View();
}
