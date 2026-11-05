using System.Data.Common;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Infrastructure.Persistence.MySql;

namespace CLMS.Infrastructure.Persistence.MySql;

/// <summary>
/// Adapts the concrete <see cref="MySqlConnectionFactory"/> to the Application-layer
/// <see cref="IDbConnectionFactory"/> so services can open BCL <see cref="DbConnection"/>s
/// without referencing Infrastructure types. The returned connection is a
/// <see cref="MySqlConnection"/> at runtime — callers may cast when they need it.
/// </summary>
public class DbConnectionFactoryAdapter(MySqlConnectionFactory inner) : IDbConnectionFactory
{
    public DbConnection Create() => inner.CreateConnection();
}