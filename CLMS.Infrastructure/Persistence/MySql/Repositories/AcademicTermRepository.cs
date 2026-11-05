using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Domain.Enums;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class AcademicTermRepository(MySqlConnectionFactory connectionFactory) : IAcademicTermRepository
{
    public async Task<int> CreateAsync(AcademicTerm term)
    {
        const string sql = """
            INSERT INTO academic_terms (academic_year, semester, start_date, end_date)
            VALUES (@Year, @Semester, @Start, @End);
            """;
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Year", term.AcademicYear);
        command.Parameters.AddWithValue("@Semester", term.Semester.ToString());
        command.Parameters.AddWithValue("@Start", (object?)term.StartDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("@End", (object?)term.EndDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        try
        {
            await command.ExecuteNonQueryAsync();
            return checked((int)command.LastInsertedId);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("This semester already exists for the selected school year.", ex);
        }
    }
    

    public async Task<bool> UpdateAsync(AcademicTerm term)
    {
        const string sql = """
            UPDATE academic_terms SET academic_year = @Year, semester = @Semester,
                start_date = @Start, end_date = @End WHERE academic_term_id = @Id;
            """;
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", term.Id);
        command.Parameters.AddWithValue("@Year", term.AcademicYear);
        command.Parameters.AddWithValue("@Semester", term.Semester.ToString());
        command.Parameters.AddWithValue("@Start", (object?)term.StartDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("@End", (object?)term.EndDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        try { return await command.ExecuteNonQueryAsync() > 0; }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("This semester already exists for the selected school year.", ex);
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new MySqlCommand("SELECT academic_term_id FROM academic_terms WHERE academic_term_id = @Id FOR UPDATE;", connection, transaction);
        command.Parameters.AddWithValue("@Id", id);
        if (await command.ExecuteScalarAsync() is null) return false;

        command.CommandText = "SELECT EXISTS(SELECT 1 FROM class_schedule WHERE term_id = @Id);";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("This school semester is used by class schedules. Remove or reassign those schedules before deleting it.");

        command.CommandText = "DELETE FROM academic_terms WHERE academic_term_id = @Id;";
        try
        {
            var deleted = await command.ExecuteNonQueryAsync() > 0;
            await transaction.CommitAsync();
            return deleted;
        }
        catch (MySqlException ex) when (ex.Number == 1451)
        {
            throw new InvalidOperationException("This school semester is in use and cannot be deleted.", ex);
        }
    }

    public async Task<IEnumerable<AcademicTerm>> GetAllAsync()
    {
        var academicTerms = new List<AcademicTerm>();

        const string sql = """
            SELECT
                academic_term_id,
                academic_year,
                semester,
                start_date,
                end_date
            FROM academic_terms
            ORDER BY academic_year, semester;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            academicTerms.Add(MapAcademicTerm(reader));
        }

        return academicTerms;
    }

    /// <summary>
    /// The term containing <paramref name="today"/>, falling back to the most recent term
    /// by start date when no term's date range covers today.
    /// <para>
    /// start_date and end_date are nullable in the schema, so both bounds are treated as
    /// open-ended when NULL — mirroring the NULL-tolerant term filter already used by
    /// ClassScheduleRepository.FindCurrentForStudentAsync.
    /// </para>
    /// </summary>
    public async Task<AcademicTerm?> GetCurrentAsync(DateOnly today)
    {
        const string sql = """
            SELECT
                academic_term_id,
                academic_year,
                semester,
                start_date,
                end_date
            FROM academic_terms
            WHERE (start_date IS NULL OR start_date <= @Today)
              AND (end_date IS NULL OR end_date >= @Today)
            ORDER BY start_date DESC
            LIMIT 1;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Today", today.ToDateTime(TimeOnly.MinValue));

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapAcademicTerm(reader);
    }

    private static AcademicTerm MapAcademicTerm(MySqlDataReader reader)
    {
        var startOrdinal = reader.GetOrdinal("start_date");
        var endOrdinal = reader.GetOrdinal("end_date");

        return new AcademicTerm
        {
            Id = reader.GetInt32("academic_term_id"),
            AcademicYear = reader.GetString("academic_year"),
            Semester = (Semester)Enum.Parse(typeof(Semester), reader.GetString("semester")),

            // Nullable in the schema: a term can be created without dates set.
            StartDate = reader.IsDBNull(startOrdinal)
                ? null
                : reader.GetDateOnly(startOrdinal),
            EndDate = reader.IsDBNull(endOrdinal)
                ? null
                : reader.GetDateOnly(endOrdinal)
        };
    }   

}
