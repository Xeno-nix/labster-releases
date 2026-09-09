
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly string _connectionString;

    public UserRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<User>> GetByRoleAsync(Role role)
    {
        var users = new List<User>();

        const string sql = """
            SELECT
                user_id,
                first_name,
                last_name,
                middle_name,
                suffix,
                email,
                role_type
            FROM users
            WHERE role_type = @Role
            ORDER BY last_name, first_name;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Role", role.ToString());

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    private static User MapUser(MySqlDataReader reader)
    {
        return new User
        {
            _id = reader.GetGuid("user_id"),
            FirstName = new Name(reader.GetString("first_name")),
            LastName = new Name(reader.GetString("last_name")),
            MiddleName = reader.IsDBNull(reader.GetOrdinal("middle_name"))
                ? null
                : new Name(reader.GetString("middle_name")),
            Suffix = reader.IsDBNull(reader.GetOrdinal("suffix"))
                ? null
                : new Name(reader.GetString("suffix")),
            Email = reader.GetString("email"),
            UserRole = (Role)Enum.Parse(typeof(Role), reader.GetString("role_type").Trim(), ignoreCase: true)
        };
    }
}