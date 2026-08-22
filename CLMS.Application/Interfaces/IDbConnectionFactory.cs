using System.Data;

namespace CLMS.Application.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
