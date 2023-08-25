using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Debugging;
using Serilog.Sinks.InMemory;
using SimpleSqlChangeNotifications;
using SimpleSqlChangeNotifications.Data;
using SimpleSqlChangeNotifications.Extensions;
using SimpleSqlChangeNotifications.Library;
using SimpleSqlChangeNotifications.Options;


try
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            // Logging setup
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(context.Configuration)
                .WriteTo.InMemory(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}")
                .CreateLogger();
            services.AddLogging(loggingBuilder =>
            {
                SelfLog.Enable(Console.Error);
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSerilog(Log.Logger, true);
            });
        
            // Bind and validate the SimpleSqlChangeNotificationOptions key from appsettings.json
            var options = context.Configuration.BindAndValidate<SimpleSqlChangeNotificationOptions, SimpleSqlChangeNotificationOptionsValidator>();
            services.AddSingleton(options);
            
            // Dapper abstraction layer
            services.AddTransient<IDbConnectionFactory, SqlDbConnectionFactory>();
            
            // Add core app entrypoint
            services.AddSingleton<App>();
        })
        .Build();

    var app = host.Services.GetRequiredService<App>();
    
    await app.RunAsync(); // Run app
}
catch (Exception ex)
{
    Log.Fatal(ex, "An unhandled exception occurred during application startup or execution.");

}
finally
{
    Log.CloseAndFlush();
}
