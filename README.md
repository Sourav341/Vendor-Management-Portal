# Vendor Portal (ASP.NET Core MVC, .NET 8)

## Run
1. Open `VendorPortal.csproj` in Visual Studio 2022 (File > Open > Project/Solution), let NuGet restore.
2. Package Manager Console:
       Add-Migration InitialCreate
   (The app applies migrations automatically on startup.)
3. Press F5.

## First Admin
Seeded from appsettings.json (SeedAdmin). Defaults: admin@vendorportal.local / Admin#12345
Change them via right-click project > Manage User Secrets:
    { "SeedAdmin": { "Email": "you@domain.com", "Password": "Strong#Pass123" } }

## Development email
No SMTP is configured. In Development, the confirmation and reset links are shown on screen and logged.
Replace LoggingEmailSender (Services/EmailSender.cs) with a real sender before production.

## Uploaded files
Stored in PrivateStorage/ (outside wwwroot), served only through DocumentsController after an owner/admin check.
Set FileStorage:RootPath to change the location.


## Shared header, navigation and footer
The shared Razor layout is `Views/Shared/_Layout.cshtml`. It uses `wwwroot/css/site.css` for responsive styling and keeps the existing VendorPortal route/role rules for Home, Applications, Dashboard, Login, Register and Logout. The home hero background and state-emblem artwork are stored locally in `wwwroot/images/`.
