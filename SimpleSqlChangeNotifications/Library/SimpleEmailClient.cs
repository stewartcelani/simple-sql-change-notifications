using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SimpleSqlChangeNotifications.Library;

public class SimpleEmailClient
{
    private readonly string _fromAddress;
    private readonly ILogger? _logger;
    private readonly int _smtpPort;
    private readonly string _smtpServer;

    public SimpleEmailClient(string smtpServer, int smtpPort, string fromAddress,
        ILogger? logger = null)
    {
        _smtpServer = smtpServer;
        _smtpPort = smtpPort;
        _fromAddress = fromAddress;
        _logger ??= logger;
    }

    // Send email using the smtp client here
    public void SendEmail(string[] toAddress, string subject, string body, bool isBodyHtml = false)
    {
        _logger?.LogInformation($"Sending email with subject '{subject}' to {string.Join(", ", toAddress)}");
        try
        {
            using var smtp = new SmtpClient();
            smtp.Host = _smtpServer;
            smtp.Port = _smtpPort;

            var message = new MailMessage();
            message.From = new MailAddress(_fromAddress);
            foreach (var address in toAddress)
            {
                message.To.Add(new MailAddress(address));
            }
            message.Subject = subject;
            message.SubjectEncoding = Encoding.UTF8;
            message.Body = body;
            message.BodyEncoding = Encoding.UTF8;
            message.IsBodyHtml = isBodyHtml;
            smtp.Send(message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error sending email to {toAddress}.");
        }
    }
}