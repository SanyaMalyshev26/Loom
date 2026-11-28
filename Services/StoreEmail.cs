using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace Loom.Services;

public sealed class StoreEmail(IConfiguration configuration, ILogger<StoreEmail> logger)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["PasswordRecovery:SmtpHost"]) &&
        MailAddress.TryCreate(configuration["PasswordRecovery:From"], out _);

    public Task<bool> SendLoginCodeAsync(string recipient, string code, CancellationToken cancellationToken)
    {
        var minutes = (int)LoginCodes.Lifetime.TotalMinutes;
        var text = $"Код входа: {code}\nСрок действия: {minutes} мин. Если вы не запрашивали код, проигнорируйте письмо.";
        return SendAsync(recipient, "Код входа Loom", text, cancellationToken, LoginCodeEmail.Render(code, minutes));
    }

    public async Task<bool> SendAsync(string recipient, string subject, string body,
        CancellationToken cancellationToken, string? html = null)
    {
        if (!IsConfigured || !MailAddress.TryCreate(recipient, out var address))
            return false;

        using var message = new MailMessage(new MailAddress(configuration["PasswordRecovery:From"]!), address)
        {
            Subject = subject,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        if (html != null)
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));

        using var smtp = new SmtpClient(configuration["PasswordRecovery:SmtpHost"]!,
            configuration.GetValue("PasswordRecovery:SmtpPort", 587))
        {
            EnableSsl = configuration.GetValue("PasswordRecovery:EnableSsl", true)
        };
        if (configuration["PasswordRecovery:SmtpUsername"] is { Length: > 0 } username)
            smtp.Credentials = new NetworkCredential(username, configuration["PasswordRecovery:SmtpPassword"]);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await smtp.SendMailAsync(message, timeout.Token);
            return true;
        }
        catch (SmtpException exception)
        {
            logger.LogWarning("SMTP delivery failed ({StatusCode}).", exception.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("SMTP delivery timed out.");
            return false;
        }
    }
}
