using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly string _connectionString;

    public BookingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<BookingDTO>> GetAllAsync()
    {
        var bookings = new List<BookingDTO>();

        const string sql = """
             SELECT
                b.booking_id,
                b.lab_id,
                l.lab_name,
                b.requestor_name,
                b.email,
                b.purpose,
                b.booking_date,
                b.start_time,
                b.end_time
            FROM bookings b
            INNER JOIN laboratories l
                ON b.lab_id = l.lab_id
            ORDER BY b.booking_date, b.start_time;
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

    public async Task<BookingDTO?> GetByIdAsync(int id)
    {
        const string sql = """
            SELECT
                b.booking_id,
                b.lab_id,
                l.lab_name,
                b.requestor_name,
                b.email,
                b.purpose,
                b.booking_date,
                b.start_time,
                b.end_time
            FROM bookings b
            INNER JOIN laboratories l
                ON b.lab_id = l.lab_id
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

    public async Task<IEnumerable<Booking>> FindByRequestorNameAndDateAsync(string requestorName, DateOnly date)
    {
        // Added for the logging terminal's Guest Time-In flow.
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
            WHERE requestor_name = @RequestorName
            AND booking_date = @Date
            ORDER BY start_time;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@RequestorName", requestorName);
        command.Parameters.AddWithValue("@Date", date.ToDateTime(TimeOnly.MinValue));

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bookings.Add(MapBooking(reader));
        }

        return bookings;
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

    private static BookingDTO MapBooking(
        MySqlDataReader reader)
    {
        return new BookingDTO
        {
            Id = reader.GetInt32("booking_id"),

            LaboratoryId = reader.GetInt32("lab_id"),

            LaboratoryName = reader.GetString("lab_name"),

            RequestorName = reader.GetString("requestor_name"),

            Email = reader.GetString("email"),

            Purpose = reader.GetString("purpose"),

            Date = DateOnly.FromDateTime(reader.GetDateTime("booking_date")),

            StartTime = TimeOnly.FromTimeSpan(reader.GetTimeSpan("start_time")),

            EndTime =TimeOnly.FromTimeSpan(reader.GetTimeSpan("end_time"))
        };
    }
}