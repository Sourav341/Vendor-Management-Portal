using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    public AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        ApplicationDbContext db, IAppEmailSender email, IHostEnvironment env)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
        _email = email;
        _env = env;
    }

    // ---------------- Register (no email verification) ----------------

    [HttpGet, AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel m)
    {
        if (!ModelState.IsValid) return View(m);

        var pan = m.PanNumber.Trim().ToUpperInvariant();
        var gst = m.GstNumber.Trim().ToUpperInvariant();

        if (await _db.VendorProfiles.AnyAsync(p => p.PanNumber == pan))
            ModelState.AddModelError(nameof(m.PanNumber), "This PAN is already registered.");
        if (await _db.VendorProfiles.AnyAsync(p => p.GstNumber == gst))
            ModelState.AddModelError(nameof(m.GstNumber), "This GST number is already registered.");
        if (!ModelState.IsValid) return View(m);

        var email = m.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            PhoneNumber = m.MobileNumber,
            EmailConfirmed = true          // no verification step
        };

        var result = await _users.CreateAsync(user, m.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(m);
        }

        await _users.AddToRoleAsync(user, "Vendor");

        _db.VendorProfiles.Add(new VendorProfile
        {
            UserId = user.Id,
            FullName = m.FullName.Trim(),
            PanNumber = pan,
            GstNumber = gst,
            MobileNumber = m.MobileNumber
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            await _users.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "Registration failed. PAN or GST may already be registered.");
            return View(m);
        }

        TempData["Success"] = "Registration successful. Please log in with your email and password.";
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
        if (!ModelState.IsValid) return View(m);

        var user = await _users.FindByEmailAsync(m.Email.Trim());

        var result = await _signIn.PasswordSignInAsync(
            user?.UserName ?? m.Email, m.Password, m.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            if (Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl!);

            return await _users.IsInRoleAsync(user!, "Admin")
                ? RedirectToAction("Index", "Admin")
                : RedirectToAction("Dashboard", "Vendor");
        }

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "Account locked after too many failed attempts. Try again in 15 minutes.");
        else
            ModelState.AddModelError(string.Empty, "Invalid email or password.");

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
        if (!ModelState.IsValid) return View(m);

        string? devLink = null;
        var user = await _users.FindByEmailAsync(m.Email.Trim());
        if (user != null)
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = Url.Action(nameof(ResetPassword), "Account",
                new { code, email = user.Email }, Request.Scheme)!;

            await _email.SendAsync(user.Email!, "Reset your password",
                $"Reset your password by <a href='{link}'>clicking here</a>.");

            if (_env.IsDevelopment()) devLink = link;
        }

        return View("ForgotPasswordConfirmation", devLink);
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPassword(string? code, string? email)
    {
        if (code == null || email == null) return RedirectToAction(nameof(Login));
        return View(new ResetPasswordViewModel { Code = code, Email = email });
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel m)
    {
        if (!ModelState.IsValid) return View(m);

        var user = await _users.FindByEmailAsync(m.Email);
        if (user == null) return RedirectToAction(nameof(ResetPasswordDone));

        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(m.Code));
            var result = await _users.ResetPasswordAsync(user, token, m.Password);
            if (result.Succeeded) return RedirectToAction(nameof(ResetPasswordDone));

            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
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
