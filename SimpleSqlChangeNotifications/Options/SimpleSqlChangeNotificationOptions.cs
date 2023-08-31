namespace SimpleSqlChangeNotifications.Options;

public sealed class SimpleSqlChangeNotificationOptions
{
    public string ConnectionString { get; set; }
    public string Query { get; set; }
    public string[] PrimaryKey { get; set; }
    public string SmtpServer { get; set; }
    public int SmtpPort { get; set; } = 25;
    public string SmtpFromAddress { get; set; }
    public string[] SmtpToAddress { get; set; } = Array.Empty<string>();
    public bool SmtpSSL { get; set; } = false;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
}