using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Authentication;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Persistence.MySql.Utils;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class UserRepository(MySqlConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<bool> AddAsync(User user)
    {
        const string sql = """
            INSERT INTO users
            (
                user_id,
                first_name,
                last_name,
                middle_name,
                suffix,
                email,
                password_hash,
                role_type
            )
            VALUES
            (
                @Id,
                @FirstName,
                @LastName,
                @MiddleName,
                @Suffix,
                @Email,
                @PasswordHash,
                @RoleType
            );
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql,connection);
        
        Console.WriteLine("UserRepository.AddAsync() : " + user.UserRole);
        
        command.AddParam("@Id", user.Id);
        command.AddParam("@FirstName", user.FirstName.Value);
        command.AddParam("@LastName", user.LastName.Value);
        command.AddParam("@MiddleName", user.MiddleName?.Value);
        command.AddParam("@Suffix", user.Suffix?.Value);
        command.AddParam("@Email", user.Email);
        command.AddParam("@PasswordHash", user.PasswordHash);
        command.AddParam("@RoleType", user.UserRole.ToString());
        
        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected == 1;
    }

    public async Task<bool> DeleteAsync(User user)
    {
        const string sql = """
            UPDATE users
            SET
                deleted_at = @DeletedAt
            WHERE user_id = @UserId;
        """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command =  new MySqlCommand(sql, connection);
        command.AddParam("@UserId", user.Id);
        command.AddParam("@DeletedAt", DateTime.UtcNow);
        
        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected == 1;
    }

    public async Task<IReadOnlyList<User>> FindAllAsync()
    {
        var users =  new List<User>();
        
        const string sql = """
            SELECT
                user_id,
                first_name,
                last_name,
                middle_name,
                suffix,
                email,
                role_type,
                password_hash
            FROM users;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            string? middleName = reader.IsDBNull(reader.GetOrdinal("middle_name")) 
                ? null : 
                reader.GetString("middle_name");
            
            string? suffix = reader.IsDBNull(reader.GetOrdinal("middle_name")) 
                ? null : 
                reader.GetString("middle_name");
            
            users.Add(new(
                reader.GetGuid("user_id"),
                new Name(reader.GetString("first_name")),
                new Name(reader.GetString("last_name")),
                middleName is null ? null : new Name(middleName),
                suffix is null ? null : new Name(suffix),
                reader.GetString("email"),
                Enum.Parse<Role>(reader.GetString("role_type"),true),
                reader.GetString("password_hash")
            ));
        }

        return users;
    }

    public async Task<IReadOnlyList<User>> FindAllByRoleAsync(Role role)
    {
        var users =  new List<User>();
        
        const string sql = """
           SELECT
               user_id,
               first_name,
               last_name,
               middle_name,
               suffix,
               email,
               role_type,
               password_hash
           FROM users
           WHERE role_type = @RoleType;
           """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@RoleType", role);
        
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            string? middleName = reader.IsDBNull(reader.GetOrdinal("middle_name")) 
                ? null : 
                reader.GetString("middle_name");
            
            string? suffix = reader.IsDBNull(reader.GetOrdinal("middle_name")) 
                ? null : 
                reader.GetString("middle_name");
            
            users.Add(new(
                reader.GetGuid("user_id"),
                new Name(reader.GetString("first_name")),
                new Name(reader.GetString("last_name")),
                middleName is null ? null : new Name(middleName),
                suffix is null ? null : new Name(suffix),
                reader.GetString("email"),
                Enum.Parse<Role>(reader.GetString("role_type"),true),
                reader.GetString("password_hash")
            ));
        }

        return users;
    }

    public async Task<User?> FindByEmailAsync(string email)
    {
        var users =  new List<User>();
        
        const string sql = """
           SELECT
               user_id,
               first_name,
               last_name,
               middle_name,
               suffix,
               email,
               role_type,
               password_hash
           FROM users
           WHERE email = @Email
           LIMIT 1;
           """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@Email", email);
        
        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            string? middleName = reader.IsDBNull(reader.GetOrdinal("middle_name")) 
                ? null : 
                reader.GetString("middle_name");
            
            string? suffix = reader.IsDBNull(reader.GetOrdinal("middle_name")) 
                ? null : 
                reader.GetString("middle_name");
            
            Console.WriteLine("UserRepository.FindByEmailAsync() : " + reader.GetString("role_type"));
            
            return new(
                reader.GetGuid("user_id"),
                new Name(reader.GetString("first_name")),
                new Name(reader.GetString("last_name")),
                middleName is null ? null : new Name(middleName),
                suffix is null ? null : new Name(suffix),
                reader.GetString("email"),
                Enum.Parse<Role>(reader.GetString("role_type"),true),
                reader.GetString("password_hash")
            );
        }

        return null;
    }

    public async Task<User?> FindByIdAsync(Guid id)
    {
        var users =  new List<User>();
        
        const string sql = """
           SELECT
               user_id,
               first_name,
               last_name,
               middle_name,
               suffix,
               email,
               role_type,
               password_hash
           FROM users
           WHERE user_id = @UserId
           LIMIT 1;
           """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", id);
        
        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            string? middleName = reader.IsDBNull(reader.GetOrdinal("middle_name")) 
                ? null : 
                reader.GetString("middle_name");
            
            string? suffix = reader.IsDBNull(reader.GetOrdinal("middle_name")) 
                ? null : 
                reader.GetString("middle_name");
            
            return new(
                reader.GetGuid("user_id"),
                new Name(reader.GetString("first_name")),
                new Name(reader.GetString("last_name")),
                middleName is null ? null : new Name(middleName),
                suffix is null ? null : new Name(suffix),
                reader.GetString("email"),
                Enum.Parse<Role>(reader.GetString("role_type"),true),
                reader.GetString("password_hash")
            );
        }

        return null;
    }

    public async Task<bool> UpdateAsync(User user)
    {
        const string sql = """
            UPDATE users
            SET
                first_name = @FirstName,
                last_name = @LastName,
                middle_name = @MiddleName,
                suffix = @Suffix,
                email = @Email,
                password_hash = @PasswordHash,
                role_type = @RoleType
            WHERE user_id = @Id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql,connection);
        
        command.AddParam("@Id", user.Id);
        command.AddParam("@FirstName", user.FirstName.Value);
        command.AddParam("@LastName", user.LastName.Value);
        command.AddParam("@MiddleName", user.MiddleName?.Value);
        command.AddParam("@Suffix", user.Suffix?.Value);
        command.AddParam("@Email", user.Email);
        command.AddParam("@PasswordHash", user.PasswordHash);
        command.AddParam("@RoleType", user.UserRole);
        
        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected == 1;
    }
}