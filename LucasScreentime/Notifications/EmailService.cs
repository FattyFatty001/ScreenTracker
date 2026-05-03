using LucasScreentime.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LucasScreentime.Notifications;

public class EmailService
{
    private readonly AppSettings _settings;

    public EmailService(AppSettings settings) => _settings = settings;

    public async Task SendAsync(string subject, string textBody, string htmlBody,
        List<string>? attachmentPaths = null)
    {
        if (_settings.ToAddresses.Count == 0)
            throw new InvalidOperationException("No recipient email addresses configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Lucas Screentime", _settings.SmtpUsername));
        foreach (var addr in _settings.ToAddresses)
            message.To.Add(MailboxAddress.Parse(addr));
        message.Subject = subject;

        // Build the rich body (text + html alternatives)
        var alternative = new Multipart("alternative")
        {
            new TextPart("plain") { Text = textBody },
            new TextPart("html")  { Text = htmlBody },
        };

        // Attach log files if any
        if (attachmentPaths != null && attachmentPaths.Count > 0)
        {
            var mixed = new Multipart("mixed") { alternative };
            foreach (var path in attachmentPaths)
            {
                if (File.Exists(path))
                {
                    var attachment = new MimePart("text", "plain")
                    {
                        Content = new MimeContent(File.OpenRead(path), ContentEncoding.Default),
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                        ContentTransferEncoding = ContentEncoding.Base64,
                        FileName = Path.GetFileName(path)
                    };
                    mixed.Add(attachment);
                }
            }
            message.Body = mixed;
        }
        else
        {
            message.Body = alternative;
        }

        using var client = new SmtpClient();
        await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(_settings.SmtpUsername, _settings.SmtpPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
