using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class BookingRepository(MySqlConnectionFactory connectionFactory) : IBookingRepository
{
    public async Task<IEnumerable<BookingDTO>> GetAllAsync()
    {
        var bookings = new List<BookingDTO>();

        const string sql = "CALL sp_booking_get_all();";

        await using var connection =
            connectionFactory.CreateConnection();

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
        const string sql = "CALL sp_booking_get_by_id(@Id);";

        await using var connection =
            connectionFactory.CreateConnection();

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
        const string sql = "CALL sp_booking_create(@LaboratoryId, @RequestorName, @Email, @Purpose, @Date, @StartTime, @EndTime);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@LaboratoryId",
            booking.LaboratoryId);

        command.Parameters.AddWithValue(
            "@RequestorName",
            booking.RequestorName);

        command.Parameters.AddWithValue(
            "@Email",
            booking.Email);

        command.Parameters.AddWithValue(
            "@Purpose",
            booking.Purpose);

        command.Parameters.AddWithValue(
            "@Date",
            booking.Date.ToDateTime(TimeOnly.MinValue));

        command.Parameters.AddWithValue(
            "@StartTime",
            booking.StartTime.ToTimeSpan());

        command.Parameters.AddWithValue(
            "@EndTime",
            booking.EndTime.ToTimeSpan());

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }

    public async Task<IEnumerable<Booking>> FindByRequestorNameAndDateAsync(string requestorName, DateOnly date)
    {
        // Added for the logging terminal's Guest Time-In flow.
        var bookings = new List<Booking>();

        const string sql = "CALL sp_booking_find_by_requestor_name_and_date(@RequestorName, @Date);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@RequestorName", requestorName);
        command.Parameters.AddWithValue("@Date", date.ToDateTime(TimeOnly.MinValue));

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
           bookings.Add(MapBookingEntity(reader));
        }

        return bookings;
    }

    public async Task UpdateAsync(Booking booking)
    {
        const string sql = "CALL sp_booking_update(@LaboratoryId, @RequestorName, @Email, @Purpose, @Date, @StartTime, @EndTime, @Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", booking.Id);

        command.Parameters.AddWithValue(
            "@LaboratoryId",
            booking.LaboratoryId);

        command.Parameters.AddWithValue(
            "@RequestorName",
            booking.RequestorName);

        command.Parameters.AddWithValue(
            "@Email",
            booking.Email);

        command.Parameters.AddWithValue(
            "@Purpose",
            booking.Purpose);

        command.Parameters.AddWithValue(
            "@Date",
            booking.Date.ToDateTime(TimeOnly.MinValue));

        command.Parameters.AddWithValue(
            "@StartTime",
            booking.StartTime.ToTimeSpan());

        command.Parameters.AddWithValue(
            "@EndTime",
            booking.EndTime.ToTimeSpan());

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "CALL sp_booking_delete(@Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteAffectedRowsAsync();
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
            Date = DateOnly.FromDateTime(
                reader.GetDateTime("booking_date")),
            StartTime = TimeOnly.FromTimeSpan(
                reader.GetTimeSpan("start_time")),
            EndTime = TimeOnly.FromTimeSpan(
                reader.GetTimeSpan("end_time"))
        };
    }

    private static Booking MapBookingEntity(
        MySqlDataReader reader)
    {
        return new Booking
        {
            Id = reader.GetInt32("booking_id"),
            LaboratoryId = reader.GetInt32("lab_id"),
            RequestorName = reader.GetString("requestor_name"),
            Email = reader.GetString("email"),
            Purpose = reader.GetString("purpose"),
            Date = DateOnly.FromDateTime(
                reader.GetDateTime("booking_date")),
            StartTime = TimeOnly.FromTimeSpan(
                reader.GetTimeSpan("start_time")),
            EndTime = TimeOnly.FromTimeSpan(
                reader.GetTimeSpan("end_time"))
        };
    }
}