using VendorPortal.Models;

namespace VendorPortal.Services;

public class FileStorageService
{
    public const long MaxBytes = 3 * 1024 * 1024;
    private readonly string _root;

    public static readonly Dictionary<string, string> Mime = new()
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    public FileStorageService(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["FileStorage:RootPath"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "PrivateStorage")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public static string? Validate(IFormFile? file, DocumentType type)
    {
        if (file is null || file.Length == 0) return "Please choose a file.";
        if (file.Length > MaxBytes) return "File is larger than 3 MB.";

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Mime.ContainsKey(ext)) return "Only PDF, JPG and PNG files are allowed.";
        if (type == DocumentType.ProfilePhoto && ext == ".pdf") return "Profile photo must be a JPG or PNG image.";

        var header = new byte[8];
        using (var s = file.OpenReadStream()) { _ = s.Read(header, 0, header.Length); }

        bool ok = ext switch
        {
            ".pdf" => header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46,
            ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
            _ => false
        };
        return ok ? null : "File content does not match its extension.";
    }

    public async Task<(string Key, string ContentType)> SaveAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var key = $"{Guid.NewGuid():N}{ext}";
        await using var fs = File.Create(Path.Combine(_root, key));
        await file.CopyToAsync(fs);
        return (key, Mime[ext]);
    }

    public Stream? Open(string key)
    {
        var path = Path.Combine(_root, Path.GetFileName(key));
        return File.Exists(path) ? File.OpenRead(path) : null;
    }
}
