using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql;

internal static class ProcedureCommandExtensions
{
    // Procedures explicitly return the affected-row count of their target DML.
    // CALL's aggregate RecordsAffected can include other work in the routine.
    internal static async Task<int> ExecuteAffectedRowsAsync(this MySqlCommand command,
        CancellationToken cancellationToken = default) =>
        Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
}
