namespace VendorPortal.Services;

public interface IAppEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody);
}

// Development implementation: writes the email to the log.
// Replace with an SMTP / SendGrid implementation before going live.
public class LoggingEmailSender : IAppEmailSender
{
    private readonly ILogger<LoggingEmailSender> _log;
    public LoggingEmailSender(ILogger<LoggingEmailSender> log) => _log = log;

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        _log.LogInformation("EMAIL to {To} | {Subject} | {Body}", to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
