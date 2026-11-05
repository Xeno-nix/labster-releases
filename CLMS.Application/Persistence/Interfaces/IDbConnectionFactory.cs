using System.Data.Common;

namespace CLMS.Application.Interfaces.Repositories;

/// <summary>
/// Creates ADO.NET connections for use by application services that must span multiple
/// repository calls in one transaction (class list import / delete).
/// <para>
/// Returns the BCL <see cref="DbConnection"/> (System.Data.Common) so the Application layer
/// never depends on an Infrastructure type, while the Infrastructure implementation owns the
/// actual MySql connection underneath.
/// </para>
/// </summary>
public interface IDbConnectionFactory
{
    DbConnection Create();
}