using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Utils;

public static class MySqlCommandExtensions
{
    public static MySqlParameter AddParam(this MySqlCommand command, string paramName, object? paramValue)
    {
        return command.Parameters.AddWithValue(paramName,paramValue);
    }
}

