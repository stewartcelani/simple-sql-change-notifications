using FluentValidation;
using System.Data.Common;
using System.Net.Mail;

namespace SimpleSqlChangeNotifications.Options;

public class SimpleSqlChangeNotificationOptionsValidator : AbstractValidator<SimpleSqlChangeNotificationOptions>
{
    public SimpleSqlChangeNotificationOptionsValidator()
    {
        RuleFor(x => x.SmtpServer)
            .NotEmpty().WithMessage("SMTP Server must not be empty or null.");

        RuleFor(x => x.SmtpPort)
            .NotEqual(0).WithMessage("SMTP Port must not be 0.");

        RuleFor(x => x.SmtpFromAddress)
            .Must(ValidateUsingMailAddress).WithMessage("SMTP From Address must be a valid email address.");

        RuleFor(x => x.SmtpToAddress)
            .Must(ContainValidEmail).WithMessage("SMTP Notification To Addresses must contain at least one valid email address.");

        RuleFor(x => x.Query)
            .Must(x => x != null && x.StartsWith("select ", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Query must start with 'select '.");

        RuleFor(x => x.PrimaryKey)
            .NotEmpty()
            .WithMessage("Primary Key must contain at least one column name.");

        RuleFor(x => x.ConnectionString)
           .NotNull()
           .MinimumLength(8)
           .WithMessage("Connection String must be a valid connection string.");
    }
    
    private bool ContainValidEmail(string[] emails) => emails.All(ValidateUsingMailAddress);

    private static bool ValidateUsingMailAddress(string emailAddress)
    {
        try
        {
            var mailAddress = new MailAddress(emailAddress);
            return true;
        }
        catch
        {
            return false;
        }
    }
}