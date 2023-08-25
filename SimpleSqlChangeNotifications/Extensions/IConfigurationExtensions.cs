using FluentValidation;
using Microsoft.Extensions.Configuration;
using SimpleSqlChangeNotifications.Options;
using SimpleSqlChangeNotifications.Options.Helpers;

namespace SimpleSqlChangeNotifications.Extensions;

public static class IConfigurationExtensions
{
    public static TSettings BindAndValidate<TSettings, TValidator>(this IConfiguration configuration) where TSettings : class where TValidator : AbstractValidator<TSettings>
    {
        return OptionsBinder.BindAndValidate<TSettings, TValidator>(configuration);
    } 
}