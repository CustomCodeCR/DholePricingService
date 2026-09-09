using System.Net;
using System.Net.Mail;

namespace Dhole.Pricing.Api.Services;

public sealed class PricingEmailService(
    IConfiguration configuration,
    ILogger<PricingEmailService> logger)
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        PricingEmailAttachment? attachment = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            throw new InvalidOperationException("El destinatario del correo de Pricing es requerido.");

        var enabled = ReadBool("NOTIFICATIONS_EMAIL_ENABLED", true);
        if (!enabled)
            throw new InvalidOperationException("El envío de correo está deshabilitado en este ambiente.");

        var host = Read("SMTP_HOST") ?? throw new InvalidOperationException("Falta SMTP_HOST.");
        var port = ReadInt("SMTP_PORT", 587);
        var ssl = ReadBool("SMTP_ENABLE_SSL", true);
        var username = Read("SMTP_USERNAME");
        var password = Read("SMTP_PASSWORD");
        var from = Read("SMTP_FROM_ADDRESS") ?? username
            ?? throw new InvalidOperationException("Falta SMTP_FROM_ADDRESS/SMTP_USERNAME.");
        var fromName = Read("SMTP_FROM_NAME") ?? "Dhole Pricing";

        using var message = new MailMessage
        {
            From = new MailAddress(from, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(recipient.Trim()));

        MemoryStream? attachmentStream = null;
        if (attachment is not null)
        {
            attachmentStream = new MemoryStream(attachment.Content, writable: false);
            message.Attachments.Add(new Attachment(
                attachmentStream,
                attachment.FileName,
                string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? "application/octet-stream"
                    : attachment.ContentType));
        }

        try
        {
            using var smtp = new SmtpClient(host, port)
            {
                EnableSsl = ssl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = string.IsNullOrWhiteSpace(username)
                    ? CredentialCache.DefaultNetworkCredentials
                    : new NetworkCredential(username, password),
            };

            cancellationToken.ThrowIfCancellationRequested();
            await smtp.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo enviar correo de Pricing a {Recipient}.", recipient);
            throw;
        }
        finally
        {
            attachmentStream?.Dispose();
        }
    }

    private string? Read(string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private int ReadInt(string key, int fallback) =>
        int.TryParse(Read(key), out var value) && value > 0 ? value : fallback;

    private bool ReadBool(string key, bool fallback) =>
        bool.TryParse(Read(key), out var value) ? value : fallback;
}

public sealed record PricingEmailAttachment(string FileName, string ContentType, byte[] Content);
