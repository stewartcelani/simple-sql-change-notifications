using DataAbstractions.Dapper;

namespace SimpleSqlChangeNotifications.Data;

public interface IDbConnectionFactory
{
    IDataAccessor CreateConnection(string connectionString);
}