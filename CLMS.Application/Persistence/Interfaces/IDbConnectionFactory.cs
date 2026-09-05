using System.Data;

namespace CLMS.Application.Persistence.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
