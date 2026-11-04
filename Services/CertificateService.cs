using Microsoft.EntityFrameworkCore;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using QRCoder;
using System.Globalization;
using VendorPortal.Data;
using VendorPortal.Models;

namespace VendorPortal.Services;

/// <summary>
/// Generates the official vendor certificate PDF and stores it outside wwwroot.
/// The QR code opens the public certificate-verification page.
///
/// Layout (one A4 page, same structure as the Jharkhand UCAN sample slip):
///   header (state seal, title, UCAN emblem) -> Basic Details box (with photo, PAN, UCAN)
///   -> Bidder Signature / Date and the verification QR code.
/// There is intentionally no Registration Details section.
/// </summary>
public sealed class CertificateService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _db;
    private readonly FileStorageService _fileStorage;

    private static readonly object FontLock = new();
    private static bool _pdfFontsConfigured;

    // Arial gives the certificate a clean, readable sans-serif hierarchy.
    private const string PdfFontName = "Arial";
    private const double BodyFontSize = 9.5;
    private const double BoxBorderWidth = 0.55;

    // The state emblem is loaded from wwwroot and embedded into each generated PDF.
    // The UCAN logo remains embedded so the finished certificate has no external image links.
    private const string JharkhandEmblemFileName = "jharkhand-state-emblem.png";
    private static readonly string UcanEmblemImageSource = "base64:" + CertificateAssets.UcanEmblemBase64;

    public CertificateService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ApplicationDbContext db,
        FileStorageService fileStorage)
    {
        _environment = environment;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Generates a one-page PDF and returns its private storage key,
    /// for example: Certificates/JH34-2026-JH34020427.pdf.
    /// </summary>
    public Task<string> GeneratePdfAsync(
        VendorCertificate certificate,
        CancellationToken cancellationToken = default)
    {
        return GeneratePdfInternalAsync(certificate, overwriteExisting: false, cancellationToken);
    }

    /// <summary>
    /// Rebuilds the PDF for an already-issued certificate while preserving its
    /// database record, verification token, issue/expiry dates, and download history.
    /// This method is intended for Admin-only repair of a previously generated PDF.
    /// </summary>
    public Task<string> RegeneratePdfAsync(
        VendorCertificate certificate,
        CancellationToken cancellationToken = default)
    {
        return GeneratePdfInternalAsync(certificate, overwriteExisting: true, cancellationToken);
    }

    private async Task<string> GeneratePdfInternalAsync(
        VendorCertificate certificate,
        bool overwriteExisting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (string.IsNullOrWhiteSpace(certificate.UcanNumber))
            throw new InvalidOperationException("UCAN is required to generate the certificate PDF.");

        if (string.IsNullOrWhiteSpace(certificate.VerificationToken))
            throw new InvalidOperationException("Certificate verification token is required.");

        if (certificate.ValidUntilUtc <= certificate.IssuedAtUtc)
            throw new InvalidOperationException("Certificate expiry must be after its issue date.");

        ConfigurePdfFonts();

        var certificateDirectory = Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "Certificates");

        Directory.CreateDirectory(certificateDirectory);

        // Agreed filename: Certificates/JH34-{issue year}-{UCAN}.pdf
        var issuedYear = certificate.IssuedAtUtc.ToString("yyyy", CultureInfo.InvariantCulture);
        var safeUcanNumber = new string(
            certificate.UcanNumber.Where(char.IsLetterOrDigit).ToArray());

        if (string.IsNullOrWhiteSpace(safeUcanNumber))
            throw new InvalidOperationException("UCAN contains no valid filename characters.");

        var fileName = $"JH34-{issuedYear}-{safeUcanNumber}.pdf";
        var storageKey = Path.Combine("Certificates", fileName)
            .Replace(Path.DirectorySeparatorChar, '/');
        var pdfPath = Path.Combine(certificateDirectory, fileName);

        // Normal issuance must never overwrite a previous file. The separate Admin-only
        // regeneration method is the only path allowed to replace an existing PDF.
        if (!overwriteExisting && System.IO.File.Exists(pdfPath))
            throw new InvalidOperationException(
                $"A certificate PDF already exists for UCAN {certificate.UcanNumber} in {issuedYear}.");

        // Render to a unique temporary PDF first. This keeps the old file intact if
        // rendering fails, then atomically replaces it after successful rendering.
        var temporaryPdfPath = Path.Combine(
            certificateDirectory,
            $".{Path.GetFileNameWithoutExtension(fileName)}.{Guid.NewGuid():N}.tmp.pdf");

        var verificationUrl = BuildVerificationUrl(certificate.VerificationToken);
        var qrBytes = GenerateQrCode(verificationUrl);
        var jharkhandEmblemBytes = await LoadJharkhandEmblemAsync(cancellationToken);
        var profilePhotoBytes = await LoadLatestProfilePhotoAsync(
            certificate.ApplicationId,
            cancellationToken);

        var document = CreateCertificateDocument(
            certificate,
            qrBytes,
            profilePhotoBytes,
            jharkhandEmblemBytes);

        try
        {
            var renderer = new PdfDocumentRenderer
            {
                Document = document
            };

            renderer.RenderDocument();
            renderer.Save(temporaryPdfPath);

            // For new issuance, do not overwrite a file created by a concurrent request.
            // For explicit Admin regeneration, replace the prior PDF but keep the same record.
            System.IO.File.Move(temporaryPdfPath, pdfPath, overwriteExisting);
            return storageKey;
        }
        catch
        {
            if (System.IO.File.Exists(temporaryPdfPath))
                System.IO.File.Delete(temporaryPdfPath);

            throw;
        }
    }

    private static byte[] GenerateQrCode(string verificationUrl)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(
            verificationUrl,
            QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);

        // IMPORTANT: use the colour overload. GetGraphic(int) writes a 1-bit *grayscale* PNG,
        // which PDFsharp's PNG importer rejects - MigraDoc then prints "Image has no valid type."
        // in place of the QR. This overload writes a 1-bit *indexed* PNG, which PDFsharp accepts.
        return qrCode.GetGraphic(
            12,
            [0, 0, 0],
            new byte[] { 255, 255, 255 });
    }

    private async Task<byte[]> LoadJharkhandEmblemAsync(CancellationToken cancellationToken)
    {
        var webRoot = string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? Path.Combine(_environment.ContentRootPath, "wwwroot")
            : _environment.WebRootPath;
        var logoPath = Path.Combine(webRoot, "image", JharkhandEmblemFileName);

        if (!System.IO.File.Exists(logoPath))
            throw new FileNotFoundException(
                "The certificate logo was not found in wwwroot/image.",
                logoPath);

        var imageBytes = await System.IO.File.ReadAllBytesAsync(logoPath, cancellationToken);
        if ((!IsPng(imageBytes) && !IsJpeg(imageBytes)) || !CanPdfSharpImport(imageBytes))
            throw new InvalidDataException("PDFsharp could not read the certificate logo image.");

        return imageBytes;
    }

    /// <summary>
    /// Loads the latest uploaded ProfilePhoto for this application from the existing
    /// private file storage. The photo itself is embedded into the PDF, not stored in SQL.
    /// </summary>
    private async Task<byte[]?> LoadLatestProfilePhotoAsync(
        int applicationId,
        CancellationToken cancellationToken)
    {
        var storageKey = await _db.DocumentVersions
            .AsNoTracking()
            .Where(v => v.VendorDocument.ApplicationId == applicationId &&
                        v.VendorDocument.DocumentType == DocumentType.ProfilePhoto)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => v.StorageKey)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(storageKey))
            return null;

        await using var stream = _fileStorage.Open(storageKey);
        if (stream is null)
            return null;

        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var imageBytes = memory.ToArray();

        // FileStorageService validates JPG/PNG at upload. Check the image signature again
        // before embedding so a non-image or damaged file cannot break PDF generation.
        if (!IsPng(imageBytes) && !IsJpeg(imageBytes))
            return null;

        // PDFsharp does not import every PNG/JPEG variant (e.g. 16-bit or 1-bit grayscale PNG).
        // If it cannot read the photo, fall back to "Photo not available" instead of drawing
        // MigraDoc's grey "Image has no valid type." box on the certificate.
        return CanPdfSharpImport(imageBytes) ? imageBytes : null;
    }

    private static bool CanPdfSharpImport(byte[] imageBytes)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes, 0, imageBytes.Length, writable: true, publiclyVisible: true);
            using var image = PdfSharp.Drawing.XImage.FromStream(stream);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string BuildVerificationUrl(string verificationToken)
    {
        var configuredBaseUrl = _configuration["Certificate:PublicBaseUrl"];
        string baseUrl;

        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            baseUrl = configuredBaseUrl.TrimEnd('/');
        }
        else
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request is null || !request.Host.HasValue)
            {
                throw new InvalidOperationException(
                    "Set Certificate:PublicBaseUrl or generate the certificate during an HTTP request.");
            }

            baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Certificate:PublicBaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        return $"{baseUrl}/Certificates/Verify/{Uri.EscapeDataString(verificationToken)}";
    }

    private static Document CreateCertificateDocument(
        VendorCertificate certificate,
        byte[] qrImageBytes,
        byte[]? profilePhotoBytes,
        byte[] jharkhandEmblemBytes)
    {
        var document = new Document();
        document.Info.Title = "Unique Contractor Account Number - Electronic Certificate Slip";
        // Keep the internal identifier in PDF metadata only. It is not printed on the certificate.
        document.Info.Subject = $"Vendor certificate for UCAN {certificate.UcanNumber}";
        document.Info.Author = "Government of Jharkhand - Department of Rural Development";

        var normal = document.Styles["Normal"];
        normal.Font.Name = PdfFontName;
        normal.Font.Size = BodyFontSize;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(1.5);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(0.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(0.8);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.0);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.0);

        // Outer certificate border, matching the supplied government certificate sample.
        var frame = section.AddTable();
        frame.Borders.Width = 0.8;
        frame.Borders.Color = Colors.Black;
        frame.AddColumn(Unit.FromCentimeter(19.0));
        frame.LeftPadding = Unit.FromCentimeter(0.18);
        frame.RightPadding = Unit.FromCentimeter(0.18);
        frame.TopPadding = Unit.FromCentimeter(0.16);
        frame.BottomPadding = Unit.FromCentimeter(0.16);
        var frameCell = frame.AddRow().Cells[0];

        AddCertificateHeader(frameCell, jharkhandEmblemBytes);
        AddSpacer(frameCell, 8);

        AddSectionHeading(frameCell, "Basic Details");
        frameCell.Elements.Add(CreateBasicDetailsTable(certificate, profilePhotoBytes));

        AddSpacer(frameCell, 14);
        frameCell.Elements.Add(CreateSignatureAndQrTable(qrImageBytes));

        // A closing paragraph keeps the last nested table from sitting flush against the frame.
        AddSpacer(frameCell, 2);

        return document;
    }

    private static void AddCertificateHeader(Cell frameCell, byte[] jharkhandEmblemBytes)
    {
        // Equal-width logo columns keep the title exactly centred on the page.
        var header = new Table();
        header.Borders.Width = 0;
        header.AddColumn(Unit.FromCentimeter(2.3));
        header.AddColumn(Unit.FromCentimeter(14.0));
        header.AddColumn(Unit.FromCentimeter(2.3));
        var row = header.AddRow();
        row.Height = Unit.FromCentimeter(2.25);
        row.HeightRule = RowHeightRule.AtLeast;
        row.VerticalAlignment = VerticalAlignment.Center;

        var leftLogoParagraph = row.Cells[0].AddParagraph();
        leftLogoParagraph.Format.Alignment = ParagraphAlignment.Left;
        var leftLogo = leftLogoParagraph.AddImage(ToMigraDocImageSource(jharkhandEmblemBytes));
        leftLogo.LockAspectRatio = true;
        leftLogo.Width = Unit.FromCentimeter(1.8);

        var titleCell = row.Cells[1];
        titleCell.VerticalAlignment = VerticalAlignment.Center;
        AddHeaderLine(titleCell, "GOVERNMENT OF JHARKHAND", 12);
        AddHeaderLine(titleCell, "Viksit Bharat - Guarantee for Rozgar and Ajeevika Mission (Gramin)", 8.2, bold: false);
        AddHeaderLine(titleCell, "Department of Rural Development", 8.8, bold: false);
        AddHeaderLine(titleCell, "UNIQUE CONTRACTOR ACCOUNT NUMBER", 10.8);
        AddHeaderLine(titleCell, "Electronic Certificate Slip - Vendor", 9.5);

        var rightLogoParagraph = row.Cells[2].AddParagraph();
        rightLogoParagraph.Format.Alignment = ParagraphAlignment.Right;
        var rightLogo = rightLogoParagraph.AddImage(UcanEmblemImageSource);
        rightLogo.LockAspectRatio = true;
        rightLogo.Width = Unit.FromCentimeter(1.75);

        frameCell.Elements.Add(header);
    }

    private static void AddHeaderLine(Cell cell, string text, double fontSize, bool bold = true)
    {
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        paragraph.Format.SpaceAfter = Unit.FromPoint(0.6);
        var run = paragraph.AddFormattedText(text);
        run.Font.Name = PdfFontName;
        run.Font.Size = fontSize;
        run.Bold = bold;
    }

    /// <summary>
    /// Basic Details box, laid out like the sample slip: label / value columns on the left,
    /// photo top-right, PAN and UCAN on the right beside the last two rows.
    /// </summary>
    private static Table CreateBasicDetailsTable(
        VendorCertificate certificate,
        byte[]? profilePhotoBytes)
    {
        const int photoRowCount = 6; // photo spans rows 0-5; PAN / UCAN sit in rows 6 and 7

        var table = new Table();
        table.Format.Font.Name = PdfFontName;
        table.Format.Font.Size = BodyFontSize;
        table.LeftPadding = Unit.FromPoint(4);
        table.RightPadding = Unit.FromPoint(4);
        table.TopPadding = Unit.FromPoint(2);
        table.BottomPadding = Unit.FromPoint(2);

        table.AddColumn(Unit.FromCentimeter(3.6));
        table.AddColumn(Unit.FromCentimeter(9.6));
        table.AddColumn(Unit.FromCentimeter(5.4));

        var details = new (string Label, string Value)[]
        {
            ("Name of Bidder:", ValueOrFallback(certificate.VendorName)),
            ("Company/Firm/Prop. Name", ValueOrFallback(certificate.CompanyFirmName, "No Company")),
            ("Father / Husband Name", ValueOrFallback(certificate.FatherOrHusbandName)),
            ("DOB", FormatDateOfBirth(certificate.DateOfBirth)),
            ("Gender", ValueOrFallback(certificate.Gender)),
            ("Address", FormatRegisteredAddress(certificate.RegisteredAddress, certificate.District)),
            ("Certificate Validity", FormatIndiaDate(certificate.ValidUntilUtc)),
            ("GST Number", ValueOrFallback(certificate.GstNumber))
        };

        for (var index = 0; index < details.Length; index++)
        {
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Height = Unit.FromCentimeter(index < photoRowCount ? 0.67 : 0.58);
            row.HeightRule = RowHeightRule.AtLeast;

            var labelCell = row.Cells[0];
            if (index == 0)
            {
                // "Name of Bidder:" with the small "(Prop / Director / Individual)" hint underneath.
                var labelParagraph = labelCell.AddParagraph();
                labelParagraph.AddFormattedText(details[index].Label, TextFormat.Bold);
                labelParagraph.AddLineBreak();
                var hint = labelParagraph.AddFormattedText("(Prop / Director / Individual)", TextFormat.Bold);
                hint.Font.Size = 5.5;
            }
            else
            {
                var label = labelCell.AddParagraph(details[index].Label);
                label.Format.Font.Bold = true;
            }

            row.Cells[1].AddParagraph(details[index].Value);

            if (index == 0)
            {
                var photoCell = row.Cells[2];
                photoCell.MergeDown = photoRowCount - 1;
                photoCell.VerticalAlignment = VerticalAlignment.Center;

                if (profilePhotoBytes is { Length: > 0 })
                {
                    var photoParagraph = photoCell.AddParagraph();
                    photoParagraph.Format.Alignment = ParagraphAlignment.Center;
                    var photo = photoParagraph.AddImage(ToMigraDocImageSource(profilePhotoBytes));
                    ApplyPassportPhotoFrame(photo, profilePhotoBytes);
                }
                else
                {
                    var photoMissing = photoCell.AddParagraph("Photo not available");
                    photoMissing.Format.Alignment = ParagraphAlignment.Left;
                    photoMissing.Format.Font.Italic = true;
                    photoMissing.Format.Font.Size = 8;
                }
            }
            else if (index == photoRowCount)
            {
                AddRightColumnValue(row.Cells[2], "PAN: ", certificate.PanNumber);
            }
            else if (index == photoRowCount + 1)
            {
                AddRightColumnValue(row.Cells[2], "UCAN: ", certificate.UcanNumber);
            }
        }

        ApplyOuterBorder(table);
        return table;
    }

    private static void ApplyPassportPhotoFrame(
        MigraDoc.DocumentObjectModel.Shapes.Image photo,
        byte[] photoBytes)
    {
        const double passportWidthCentimeters = 2.45;
        const double passportHeightCentimeters = 3.15;
        const double passportAspectRatio = passportWidthCentimeters / passportHeightCentimeters;

        using var stream = new MemoryStream(
            photoBytes,
            0,
            photoBytes.Length,
            writable: true,
            publiclyVisible: true);
        using var source = PdfSharp.Drawing.XImage.FromStream(stream);

        var sourceAspectRatio = source.PointWidth / source.PointHeight;
        photo.LockAspectRatio = true;

        if (sourceAspectRatio > passportAspectRatio)
        {
            // Scale the uncropped image to the target height, then crop its sides.
            // MigraDoc applies crop distances at the image's display scale.
            photo.Height = Unit.FromCentimeter(passportHeightCentimeters);
            var cropPerSide = (source.PointWidth - source.PointHeight * passportAspectRatio) / 2;
            photo.PictureFormat.CropLeft = Unit.FromPoint(cropPerSide);
            photo.PictureFormat.CropRight = Unit.FromPoint(cropPerSide);
        }
        else if (sourceAspectRatio < passportAspectRatio)
        {
            // Scale the uncropped image to the target width, then crop its top and bottom.
            photo.Width = Unit.FromCentimeter(passportWidthCentimeters);
            var cropPerSide = (source.PointHeight - source.PointWidth / passportAspectRatio) / 2;
            photo.PictureFormat.CropTop = Unit.FromPoint(cropPerSide);
            photo.PictureFormat.CropBottom = Unit.FromPoint(cropPerSide);
        }
        else
        {
            photo.Width = Unit.FromCentimeter(passportWidthCentimeters);
        }
    }

    private static void AddRightColumnValue(Cell cell, string label, string? value)
    {
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Left;
        paragraph.AddFormattedText(label, TextFormat.Bold);
        paragraph.AddFormattedText(ValueOrFallback(value), TextFormat.Bold);
    }

    /// <summary>
    /// Draws only the outside edge of the table (no inner grid lines), like the sample slip.
    /// </summary>
    private static void ApplyOuterBorder(Table table)
    {
        var lastRow = table.Rows.Count - 1;
        var lastColumn = table.Columns.Count - 1;

        for (var r = 0; r <= lastRow; r++)
        {
            for (var c = 0; c <= lastColumn; c++)
            {
                var borders = table.Rows[r].Cells[c].Borders;

                if (c == 0)
                {
                    borders.Left.Width = BoxBorderWidth;
                    borders.Left.Color = Colors.DarkGray;
                }

                if (c == lastColumn)
                {
                    borders.Right.Width = BoxBorderWidth;
                    borders.Right.Color = Colors.DarkGray;
                }

                if (r == 0)
                {
                    borders.Top.Width = BoxBorderWidth;
                    borders.Top.Color = Colors.DarkGray;
                }

                if (r == lastRow)
                {
                    borders.Bottom.Width = BoxBorderWidth;
                    borders.Bottom.Color = Colors.DarkGray;
                }
            }
        }
    }

    private static Table CreateSignatureAndQrTable(byte[] qrImageBytes)
    {
        var table = new Table();
        table.Borders.Width = 0;
        table.Format.Font.Name = PdfFontName;
        table.Format.Font.Size = BodyFontSize;
        table.AddColumn(Unit.FromCentimeter(13.6));
        table.AddColumn(Unit.FromCentimeter(5.0));

        var row = table.AddRow();
        row.Height = Unit.FromCentimeter(2.8); // leaves room above the label for the bidder to sign
        row.HeightRule = RowHeightRule.AtLeast;
        row.VerticalAlignment = VerticalAlignment.Bottom;

        var signature = row.Cells[0].AddParagraph();
        signature.AddFormattedText("Bidder Signature", TextFormat.Bold);
        signature.AddLineBreak();
        signature.AddFormattedText("Date", TextFormat.Bold);

        var qrParagraph = row.Cells[1].AddParagraph();
        qrParagraph.Format.Alignment = ParagraphAlignment.Right;
        var qr = qrParagraph.AddImage(ToMigraDocImageSource(qrImageBytes));
        qr.LockAspectRatio = true;
        qr.Width = Unit.FromCentimeter(2.0);
        qr.Height = Unit.FromCentimeter(2.0);

        var verifyLabel = row.Cells[1].AddParagraph("Scan to verify");
        verifyLabel.Format.Alignment = ParagraphAlignment.Right;
        verifyLabel.Format.Font.Size = 7;

        return table;
    }

    private static void AddSectionHeading(Cell cell, string text)
    {
        var paragraph = cell.AddParagraph();
        paragraph.Format.SpaceBefore = Unit.FromPoint(2);
        paragraph.Format.SpaceAfter = Unit.FromPoint(3);
        paragraph.AddFormattedText(text, TextFormat.Bold);
    }

    private static void AddSpacer(Cell cell, double points)
    {
        var paragraph = cell.AddParagraph(" ");
        paragraph.Format.Font.Size = 2;
        paragraph.Format.SpaceAfter = Unit.FromPoint(points);
    }

    private static string ToMigraDocImageSource(byte[] imageBytes) =>
        "base64:" + Convert.ToBase64String(imageBytes);

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 8 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    private static bool IsJpeg(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    private static string FormatDateOfBirth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Not provided";

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        return value.Trim();
    }

    private static string FormatRegisteredAddress(string? address, string? district)
    {
        var formattedAddress = address?.Trim() ?? string.Empty;
        var formattedDistrict = district?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(formattedDistrict) ||
            string.IsNullOrWhiteSpace(formattedAddress) ||
            formattedAddress.Contains(formattedDistrict, StringComparison.OrdinalIgnoreCase))
        {
            return ValueOrFallback(formattedAddress);
        }

        return $"{formattedAddress}, {formattedDistrict}";
    }

    private static string ValueOrFallback(string? value, string fallback = "Not provided") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FormatIndiaDate(DateTime utcDate)
    {
        var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
        var utc = DateTime.SpecifyKind(utcDate, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, indiaTimeZone)
            .ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private static void ConfigurePdfFonts()
    {
        lock (FontLock)
        {
            if (_pdfFontsConfigured)
                return;

            if (OperatingSystem.IsWindows())
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;

            _pdfFontsConfigured = true;
        }
    }
}