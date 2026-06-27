using CherryBox.Plugins.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CherryBox.Email.Plugin;

internal sealed class SmtpEmailSender
{
    public async Task SendAsync(
        EmailSettings settings,
        string toAddress,
        string subject,
        string? plainTextBody,
        string? htmlBody,
        IReadOnlyList<EmailEmbeddedImage>? embeddedImages,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");
        if (string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("From address is not configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromDisplayName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;

        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            var builder = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = plainTextBody ?? StripHtml(htmlBody)
            };

            if (embeddedImages is { Count: > 0 })
            {
                foreach (var image in embeddedImages)
                {
                    if (string.IsNullOrWhiteSpace(image.ContentId) || image.Data.Length == 0)
                        continue;

                    var fileName = string.IsNullOrWhiteSpace(image.FileName)
                        ? $"{image.ContentId}.jpg"
                        : image.FileName;
                    var linked = builder.LinkedResources.Add(fileName, image.Data, ContentType.Parse(image.MimeType));
                    linked.ContentId = image.ContentId;
                }
            }

            message.Body = builder.ToMessageBody();
        }
        else
        {
            message.Body = new TextPart("plain") { Text = plainTextBody ?? string.Empty };
        }

        using var client = new SmtpClient();
        await client.ConnectAsync(
            settings.SmtpHost,
            settings.SmtpPort,
            settings.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static string StripHtml(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty).Trim();
}
