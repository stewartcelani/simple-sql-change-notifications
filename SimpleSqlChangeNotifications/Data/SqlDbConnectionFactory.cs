using DataAbstractions.Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace SimpleSqlChangeNotifications.Data;

public class SqlDbConnectionFactory : IDbConnectionFactory
{
    private readonly ILogger<SqlDbConnectionFactory> _logger;

    public SqlDbConnectionFactory(ILogger<SqlDbConnectionFactory> logger)
    {
        _logger = logger;
    }

    public IDataAccessor CreateConnection(string connectionString)
    {
        return new DataAccessor(new SqlConnection(connectionString));
    }
}