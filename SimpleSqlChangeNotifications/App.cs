using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Common.Extensions.Object;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SimpleSqlChangeNotifications.Data;
using SimpleSqlChangeNotifications.Library;
using SimpleSqlChangeNotifications.Options;

namespace SimpleSqlChangeNotifications;

public class App
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<App> _logger;
    private readonly SimpleSqlChangeNotificationOptions _options;

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
        
        var optionsHash = ComputeHash(Encoding.UTF8.GetBytes(_options.ConnectionString + _options.Query + string.Join(",", _options.PrimaryKey)));

        var exeHash = ComputeHash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location));


        var oldResult = new List<DataItem>();
        var sinceText = string.Empty;
        if (File.Exists("queryCache.json"))
        {
            var cachedData = await File.ReadAllTextAsync("queryCache.json");
            var cache = JsonConvert.DeserializeObject<QueryCache>(cachedData);

            if (cache?.SimpleSqlChangeNotificationOptionsHash == optionsHash && cache.ExecutableHash == exeHash)
            {
                oldResult = cache.Data;
                var lastWriteTime = File.GetLastWriteTime("queryCache.json");
                var currentTime = DateTime.Now;
                var hoursAgo = (currentTime - lastWriteTime).TotalHours;
                var roundedHoursAgo = Math.Round(hoursAgo, 1); // Round to one decimal place
                sinceText = $"since {lastWriteTime} ({roundedHoursAgo} hours ago)";
                _logger.LogInformation(
                    "Cached results ({cachedResultsCount} rows) found from previous run at {time} ({hoursAgo} hours ago).",
                    oldResult.Count, lastWriteTime, roundedHoursAgo);
            }
            else
            {
                _logger.LogWarning(
                    "Cached results found but options hash or executable hash has changed. Ignoring cached results.");
                oldResult = new List<DataItem>();
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
                Hash = ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value))),
                Value = value
            };
        }).ToList();

        // Serialize the results and hash to JSON
        var cacheToWrite = new QueryCache
        {
            SimpleSqlChangeNotificationOptionsHash = optionsHash,
            ExecutableHash = exeHash,
            Data = queryResult
        };
        var json = JsonConvert.SerializeObject(cacheToWrite);

        _logger.LogInformation("Query found {rowCount} rows.", queryResult.Count);

        // Write the JSON to a file
        _logger.LogInformation("Storing {queryResultCount} rows in queryCache.json.", queryResult.Count);
        await File.WriteAllTextAsync("queryCache.json", json);
        var newCache =
            JsonConvert.DeserializeObject<QueryCache>(await File.ReadAllTextAsync("queryCache.json"));
        var newResult = newCache!.Data;

        // Compare the cached results with the new results
        var changes = new List<DataItemChange>();

        if (oldResult.Any())
        {
            if (oldResult.Count != newResult.Count)
                _logger.LogWarning("Row count has changed from {cachedResultsCount} to {queryResultCount}.",
                    oldResult.Count, queryResult.Count);

            // Check for new rows and changes
            foreach (var newRow in newResult)
            {
                var oldRow = oldResult.FirstOrDefault(item =>
                    _options.PrimaryKey.All(key =>
                        item.Value.ContainsKey(key) && item.Value[key]?.ToString() == newRow.Value[key]?.ToString()));

                if (oldRow == null)
                {
                    var keyValues = string.Join(", ",
                        _options.PrimaryKey.Select(key => $"{key}: {newRow.Value[key]}"));

                    _logger.LogDebug($"New row: {keyValues}");
                    _logger.LogDebug($"New value: {JsonConvert.SerializeObject(newRow.Value)}");

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
                    var oldJson = JsonConvert.SerializeObject(oldRow.Value);
                    var newJson = JsonConvert.SerializeObject(newRow.Value);

                    _logger.LogDebug($"Row changed: {keyValues}");
                    _logger.LogDebug($"Old value: {oldJson}");
                    _logger.LogDebug($"New value: {newJson}");
                }
            }
        }

        if (changes.Any())
        {
            if (!HandleNotification(connection.Database,  _options.SmtpSubject, sinceText, changes))
                _logger.LogError("Changes detected but failed to send notification.");
        }
        else if (oldResult.Any())
        {
            _logger.LogInformation("No changes detected.");
        }

        _logger.LogInformation("Program end.");
    }


    private bool HandleNotification(string databaseName, string smtpSubject, string sinceText, List<DataItemChange> changes)
    {
        _logger.LogInformation("Changes detected. Sending notification.");

        var table = new List<string>();
        table.Add(@"
            <style>
                table {
                  font-family: arial, sans-serif;
                  border-collapse: collapse;                  
                }

                table * {
                  font-size: 10px;                  
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
        table.Add("<table>");

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

                if (oldValue is not null && !oldValue.DeepEquals(newValue))
                {
                    var strikethroughValue = oldValue.ToString();
                    if (string.IsNullOrEmpty(strikethroughValue))
                        strikethroughValue = "&nbsp;&nbsp;&nbsp;&nbsp;";
                    row += $"<del style='color: red;'>{strikethroughValue}</del> ";
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
            var subject = $"[{databaseName}]";
            if (!string.IsNullOrWhiteSpace(_options.SmtpSubject))
            {
                subject += $" {_options.SmtpSubject} - ";
            }
            var subjectSummary = changes.Count == 1 ? "1 change" : $"{changes.Count} changes";
            subject += $"{subjectSummary}";
            
            var htmlContent = @$"
                    <h3>{subject} {sinceText}</h3>              
                    <p>You are receiving this email because you are subscribed to receive notifications for changes to the following SQL query on the <b>{databaseName}</b> database: </p>
                    <pre style='color: orange; margin-bottom: 16px;'>{_options.Query}</pre>                    
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

    private string ComputeHash(byte[] input)
    {
        using var sha256Hash = SHA256.Create();
        var hashBytes = sha256Hash.ComputeHash(input);
        return Convert.ToBase64String(hashBytes);
    }

}