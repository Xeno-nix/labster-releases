using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Domain.ValueObjects;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly string _connectionString;

    public BookingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<Booking>> GetAllAsync()
    {
        var bookings = new List<Booking>();

        const string sql = """
            SELECT
                booking_id,
                lab_id,
                requestor_name,
                email,
                purpose,
                booking_date,
                start_time,
                end_time
            FROM bookings
            ORDER BY booking_date, start_time;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bookings.Add(MapBooking(reader));
        }

        return bookings;
    }

    public async Task<Booking?> GetByIdAsync(int id)
    {
        const string sql = """
            SELECT
                booking_id,
                lab_id,
                requestor_name,
                email,
                purpose,
                booking_date,
                start_time,
                end_time
            FROM bookings
            WHERE booking_id = @Id;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapBooking(reader);
    }

    public async Task<int> CreateAsync(Booking booking)
    {
        const string sql = """
            INSERT INTO bookings
            (
                lab_id,
                requestor_name,
                email,
                purpose,
                booking_date,
                start_time,
                end_time
            )
            VALUES
            (
                @LaboratoryId,
                @RequestorName,
                @Email,
                @Purpose,
                @Date,
                @StartTime,
                @EndTime
            );

            SELECT LAST_INSERT_ID();
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@LaboratoryId",booking.LaboratoryId);

        command.Parameters.AddWithValue("@RequestorName",booking.RequestorName);

        command.Parameters.AddWithValue("@Email",booking.Email);

        command.Parameters.AddWithValue("@Purpose",booking.Purpose);

        command.Parameters.AddWithValue("@Date",booking.Date.ToDateTime(TimeOnly.MinValue));

        command.Parameters.AddWithValue("@StartTime",booking.StartTime.ToTimeSpan());

        command.Parameters.AddWithValue("@EndTime",booking.EndTime.ToTimeSpan());

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Booking booking)
    {
        const string sql = """
            UPDATE bookings
            SET
                lab_id = @LaboratoryId,
                requestor_name = @RequestorName,
                email = @Email,
                purpose = @Purpose,
                booking_date = @Date,
                start_time = @StartTime,
                end_time = @EndTime
            WHERE booking_id = @Id;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id",booking.Id);

        command.Parameters.AddWithValue("@LaboratoryId",booking.LaboratoryId);

        command.Parameters.AddWithValue("@RequestorName",booking.RequestorName);

        command.Parameters.AddWithValue("@Email",booking.Email);

        command.Parameters.AddWithValue("@Purpose",booking.Purpose);

        command.Parameters.AddWithValue("@Date",booking.Date.ToDateTime(TimeOnly.MinValue));

        command.Parameters.AddWithValue("@StartTime",booking.StartTime.ToTimeSpan());

        command.Parameters.AddWithValue("@EndTime",booking.EndTime.ToTimeSpan());

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = """
            DELETE FROM bookings
            WHERE booking_id = @Id;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteNonQueryAsync();
    }

    private static Booking MapBooking(
        MySqlDataReader reader)
    {
        return new Booking
        {
            Id = reader.GetInt32("booking_id"),

            LaboratoryId = reader.GetInt32("lab_id"),

            RequestorName = reader.GetString("requestor_name"),

            Email = reader.GetString("email"),

            Purpose = reader.GetString("purpose"),

            Date = DateOnly.FromDateTime(reader.GetDateTime("booking_date")),

            StartTime = TimeOnly.FromTimeSpan(reader.GetTimeSpan("start_time")),

            EndTime =TimeOnly.FromTimeSpan(reader.GetTimeSpan("end_time"))
        };
    }
}