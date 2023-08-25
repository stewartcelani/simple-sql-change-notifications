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
}