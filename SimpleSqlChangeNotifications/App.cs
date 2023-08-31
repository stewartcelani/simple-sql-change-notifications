using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog.Sinks.InMemory;
using SimpleSqlChangeNotifications.Data;
using SimpleSqlChangeNotifications.Helpers;
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
        var sinceText = string.Empty;
        if (File.Exists("queryCache.json"))
        {
            var cachedData = await File.ReadAllTextAsync("queryCache.json");
            var serializerOptions = new JsonSerializerOptions
            {
                Converters = { new ObjectDeserializer() }
            };
            var cache = JsonSerializer.Deserialize<QueryCache>(cachedData, serializerOptions);

            if (cache?.SimpleSqlChangeNotificationOptionsHash == optionsHash && cache.ExecutableHash == exeHash)
            {
                cachedResults = cache.Data;
                var lastWriteTime = File.GetLastWriteTime("queryCache.json");
                var currentTime = DateTime.Now;
                var hoursAgo = (currentTime - lastWriteTime).TotalHours;
                var roundedHoursAgo = Math.Round(hoursAgo, 1); // Round to one decimal place
                sinceText = $"since {lastWriteTime} ({roundedHoursAgo} hours ago)";
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

        var changes = new List<DataItemChange>();

        // Compare the cached results with the new results
        if (cachedResults.Any())
        {
            if (cachedResults.Count != queryResult.Count)
            {
                _logger.LogWarning("Row count has changed from {cachedResultsCount} to {queryResultCount}.",
                    cachedResults.Count, queryResult.Count);
            }

            // Check for new rows and changes
            foreach (var newRow in queryResult)
            {
                var oldRow = cachedResults.FirstOrDefault(item =>
                    _options.PrimaryKey.All(key =>
                        item.Value.ContainsKey(key) && item.Value[key]?.ToString() == newRow.Value[key]?.ToString()));

                if (oldRow == null)
                {
                    var keyValues = string.Join(", ",
                        _options.PrimaryKey.Select(key => $"{key}: {newRow.Value[key]}"));
                    var addedText = new List<string>
                    {
                        $"New row: {keyValues}",
                        $"New value: {JsonSerializer.Serialize(newRow.Value)}"
                    };
                    added.AddRange(addedText);
                    foreach (var s in addedText)
                    {
                        _logger.LogDebug(s);
                    }

                    changes.Add(new DataItemChange
                    {
                        OldRow = null,
                        NewRow = newRow
                    });
                }
                else if (oldRow.Hash != newRow.Hash)
                {
                    var keyValues = string.Join(", ",
                        _options.PrimaryKey.Select(key => $"{key}: {newRow.Value[key]}"));

                    changes.Add(new DataItemChange
                    {
                        OldRow = oldRow,
                        NewRow = newRow
                    });

                    // Convert the old and new values to JSON
                    var oldJson = JsonSerializer.Serialize(oldRow.Value);
                    var newJson = JsonSerializer.Serialize(newRow.Value);

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
            if (!HandleNotification(connection.Database, sinceText, changes))
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

    private bool HandleNotification(string databaseName, string sinceText, List<DataItemChange> changes)
    {
        _logger.LogInformation("Changes detected. Sending notification.");

        var table = new List<string>();
        table.Add(@"
            <style>
                table {
                  font-family: arial, sans-serif;
                  border-collapse: collapse;
                  width: 100%;
                }

                table * {
                  font-size: 11px;                  
                }

                td, th {
                  border: 1px solid #dddddd;
                  text-align: left;
                  padding: 8px;
                }

                tr:nth-child(even) {
                  background-color: #dddddd;
                }
            </style>
        ");
        table.Add("<table style ='width:100%;'>");

        // Table Header
        table.Add("<tr>");
        var header = "<th>&nbsp;</th>";
        changes[0].NewRow.Value.Keys.ToList().ForEach(key => header += $"<th>{key}</th>");
        table.Add(header);
        table.Add("</tr>");

        // Table Rows
        foreach (var dataItemChange in changes)
        {
            table.Add("<tr>");

            var row = $"<th>{dataItemChange.Status}</th>";

            // loop through each column
            foreach (var key in dataItemChange.NewRow.Value.Keys)
            {
                var oldValue = dataItemChange.OldRow?.Value[key] ?? null;
                var newValue = dataItemChange.NewRow.Value[key];

                row += "<td>";

                if (oldValue is not null && !oldValue.Equals(newValue))
                {
                    row += $"<del style='color: red;'>{oldValue}</del> ";
                }

                row += $"{newValue}";

                row += "</td>";
            }

            table.Add(row);

            table.Add("</tr>");
        }

        table.Add("</table>");
        


        var client = new SimpleEmailClient(_options.SmtpServer, _options.SmtpPort, _options.SmtpFromAddress,
            _options.SmtpSSL, _options.SmtpUsername, _options.SmtpPassword, _logger);
        try
        {
            var subjectSummary = changes.Count == 1 ? "1 change" : $"{changes.Count} changes";
            var subject = $"[{databaseName}] {subjectSummary}";
            var htmlContent = @$"
                    <p>You are receiving this email because you are subscribed to receive notifications for changes to the following SQL query on the <b>{databaseName}</b> database: </p>
                    <pre style='color: orange;'>{_options.Query}</pre>                    
                    <h3 style='margin-bottom: 10px;'>{subjectSummary} {sinceText}</h3>              
                ";
                
            htmlContent += string.Join("\n", table);
            client.SendEmail(_options.SmtpToAddress, subject, htmlContent, true);
            return true;
        }
        catch
        {
            return false;
        }
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