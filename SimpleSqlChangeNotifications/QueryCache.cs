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
