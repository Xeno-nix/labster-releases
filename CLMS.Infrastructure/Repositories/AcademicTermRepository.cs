using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Domain.Enums;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class AcademicTermRepository : IAcademicTermRepository
{
    private readonly string _connectionString;

    public AcademicTermRepository(string connectionString)
    {
        _connectionString = connectionString;
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

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            academicTerms.Add(MapAcademicTerm(reader));
        }

        return academicTerms;
    }

    private static AcademicTerm MapAcademicTerm(MySqlDataReader reader)
    {
        return new AcademicTerm
        {
            Id = reader.GetInt32("academic_term_id"),
            AcademicYear = reader.GetString("academic_year"),
            Semester = (Semester)Enum.Parse(typeof(Semester), reader.GetString("semester")),
            StartDate = reader.GetDateOnly("start_date"),
            EndDate = reader.GetDateOnly("end_date")
        };
    }   

}

        