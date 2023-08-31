namespace SimpleSqlChangeNotifications;

public class QueryCache
{
    public string SimpleSqlChangeNotificationOptionsHash { get; set; }
    public string ExecutableHash { get; set; }
    public List<DataItem> Data { get; set; }
}


public class DataItem
{
    public string Hash { get; set; }
    public Dictionary<string, object> Value { get; set; }
}

public class DataItemChange
{
    public DataItem? OldRow { get; init; } = null;
    public DataItem NewRow { get; init; } = default!;
    public string Status => OldRow == null ? "Added" : "Updated";
    
}