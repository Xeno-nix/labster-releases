using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Domain.Enums;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class AcademicTermRepository(MySqlConnectionFactory connectionFactory) : IAcademicTermRepository
{
    

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