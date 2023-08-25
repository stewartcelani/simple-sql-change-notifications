using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog.Sinks.InMemory;
using SimpleSqlChangeNotifications.Data;
using SimpleSqlChangeNotifications.Library;
using SimpleSqlChangeNotifications.Options;

namespace SimpleSqlChangeNotifications;

public class App
{
    private readonly ILogger<App> _logger;
    private readonly SimpleSqlChangeNotificationOptions _options;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public App(ILogger<App> logger, IDbConnectionFactory dbConnectionFactory,
        SimpleSqlChangeNotificationOptions options)
    {
        _logger = logger;
        _dbConnectionFactory = dbConnectionFactory;
        _options = options;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Program start.");
        _logger.LogInformation("Looking for changes for query: {query}", _options.Query);

        var optionsHash = HashOptions(_options);
        var exeHash =
            GetExecutableHash(); // Need to ignore/invalidate the queryCache.json file if the executable changes versions

        var cachedResults = new List<DataItem>();
        if (File.Exists("queryCache.json"))
        {
            var cachedData = await File.ReadAllTextAsync("queryCache.json");
            var cache = JsonSerializer.Deserialize<QueryCache>(cachedData);

            if (cache?.SimpleSqlChangeNotificationOptionsHash == optionsHash && cache.ExecutableHash == exeHash)
            {
                cachedResults = cache.Data;
                var lastWriteTime = File.GetLastWriteTime("queryCache.json");
                var currentTime = DateTime.Now;
                var hoursAgo = (currentTime - lastWriteTime).TotalHours;
                var roundedHoursAgo = Math.Round(hoursAgo, 1); // Round to one decimal place
                _logger.LogInformation(
                    "Cached results ({cachedResultsCount} rows) found from previous run at {time} ({hoursAgo} hours ago).",
                    cachedResults.Count, lastWriteTime, roundedHoursAgo);
            }
            else
            {
                _logger.LogWarning(
                    "Cached results found but options hash or executable hash has changed. Ignoring cached results.");
                cachedResults = new List<DataItem>();
            }
        }

        using var connection = _dbConnectionFactory.CreateConnection(_options.ConnectionString);
        var dapperResult = connection.Query(_options.Query);
        var queryResult = dapperResult.Select(row =>
        {
            var dapperRow = (IDictionary<string, object>)row;
            var value = dapperRow.ToDictionary(pair => pair.Key, pair => pair.Value);
            return new DataItem
            {
                Hash = ComputeHash(JsonSerializer.Serialize(value)),
                Value = value
            };
        }).ToList();

        _logger.LogInformation("Query found {rowCount} rows.", queryResult.Count);

        var added = new List<string>();
        var changed = new List<string>();


        // Compare the cached results with the new results
        if (cachedResults.Any())
        {
            if (cachedResults.Count != queryResult.Count)
            {
                _logger.LogWarning("Row count has changed from {cachedResultsCount} to {queryResultCount}.",
                    cachedResults.Count, queryResult.Count);
            }

            // Check for new rows and changes
            foreach (var newItem in queryResult)
            {
                var oldItem = cachedResults.FirstOrDefault(item =>
                    _options.PrimaryKey.All(key =>
                        item.Value.ContainsKey(key) && item.Value[key]?.ToString() == newItem.Value[key]?.ToString()));

                if (oldItem == null)
                {
                    var keyValues = string.Join(", ",
                        _options.PrimaryKey.Select(key => $"{key}: {newItem.Value[key]}"));
                    var addedText = new List<string>
                    {
                        $"New row: {keyValues}",
                        $"New value: {JsonSerializer.Serialize(newItem.Value)}"
                    };
                    added.AddRange(addedText);
                    foreach (var s in addedText)
                    {
                        _logger.LogDebug(s);
                    }
                }
                else if (oldItem.Hash != newItem.Hash)
                {
                    var keyValues = string.Join(", ",
                        _options.PrimaryKey.Select(key => $"{key}: {newItem.Value[key]}"));

                    // Convert the old and new values to JSON
                    var oldJson = JsonSerializer.Serialize(oldItem.Value);
                    var newJson = JsonSerializer.Serialize(newItem.Value);

                    var changedText = new List<string>
                    {
                        $"Row changed: {keyValues}",
                        $"Old value: {oldJson}",
                        $"New value: {newJson}"
                    };

                    changed.AddRange(changedText);
                    foreach (var s in changedText)
                    {
                        _logger.LogWarning(s);
                    }
                }
            }
        }

        if (added.Any() || changed.Any())
        {
            if (!HandleNotification())
            {
                _logger.LogError("Changes detected but failed to send notification.");
            }
        }
        else if (cachedResults.Any())
        {
            _logger.LogInformation("No changes detected.");
        }

        // Serialize the results and hash to JSON
        var cacheToWrite = new QueryCache
        {
            SimpleSqlChangeNotificationOptionsHash = optionsHash,
            ExecutableHash = exeHash,
            Data = queryResult
        };
        var json = JsonSerializer.Serialize(cacheToWrite);

        // Write the JSON to a file
        _logger.LogInformation("Storing {queryResultCount} rows in queryCache.json.", queryResult.Count);
        await File.WriteAllTextAsync("queryCache.json", json);

        _logger.LogInformation("Program end.");
    }

    private bool HandleNotification()
    {
        _logger.LogInformation("Changes detected. Sending notification.");

        var client = new SimpleEmailClient(_options.SmtpServer, _options.SmtpPort, _options.SmtpFromAddress, _logger);
        try
        {
            var subject = $"[{Environment.MachineName}] SQL Change Notification";
            var htmlContent = new StringBuilder("<div style='font-family:Courier New'><pre>");
            foreach (var instanceLogEvent in InMemorySink.Instance.LogEvents.ToList())
            {
                var logLevelShortString = GetLogLevelShortString(instanceLogEvent.Level);
                var logLevelColor = GetLogLevelColor(instanceLogEvent.Level);
                var encodedMessage = System.Net.WebUtility.HtmlEncode(instanceLogEvent.RenderMessage());

                // Highlight the query string within the log message
                var queryHighlightColor = "#FF0000"; // Red
                encodedMessage = encodedMessage.Replace(System.Net.WebUtility.HtmlEncode(_options.Query),
                    $"<span style='color:{queryHighlightColor}'>{System.Net.WebUtility.HtmlEncode(_options.Query)}</span>");

                htmlContent.AppendLine(
                    $"<span style='color:{logLevelColor}'>[{instanceLogEvent.Timestamp:HH:mm:ss} {logLevelShortString}] {encodedMessage}</span>");
            }

            htmlContent.Append("</pre></div>");

            client.SendEmail(_options.SmtpToAddress, subject, htmlContent.ToString(), true);
            return true;
        }
        catch
        {
            return false;
        }
    }


    private string GetLogLevelColor(LogEventLevel logEventLevel)
    {
        return logEventLevel switch
        {
            LogEventLevel.Verbose => "#B3B3B3", // Light Grey
            LogEventLevel.Debug => "#808080", // Grey
            LogEventLevel.Information => "#000000", // Black
            LogEventLevel.Warning => "#FF8C00", // Orange
            LogEventLevel.Error => "#FF0000", // Red
            LogEventLevel.Fatal => "#8B0000", // Dark Red
            _ => throw new ArgumentOutOfRangeException(nameof(logEventLevel))
        };
    }


    private string GetLogLevelShortString(LogEventLevel logEventLevel)
    {
        return logEventLevel switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => throw new ArgumentOutOfRangeException(nameof(logEventLevel))
        };
    }


    private string ComputeHash(string input)
    {
        using (SHA256 sha256Hash = SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }

    private string GetExecutableHash()
    {
        using var sha256 = SHA256.Create();
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var bytes = File.ReadAllBytes(exePath);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }


    private string HashOptions(SimpleSqlChangeNotificationOptions options)
    {
        var combinedOptions = options.ConnectionString + options.Query + string.Join(",", options.PrimaryKey);
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(combinedOptions);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }
}