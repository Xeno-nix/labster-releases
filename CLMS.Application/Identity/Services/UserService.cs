using CLMS.Application.DTOs;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;

namespace CLMS.Application.Identity.Services;

public class UserService(IUserRepository userRepository)
{
    public async Task<IEnumerable<UserDto>> GetFacultyAsync()
    {
        return (await userRepository.FindAllByRoleAsync(Role.Faculty))
            .Select(MapToDto);
    }

    private static UserDto MapToDto(User user)
    {
        return new()
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName.Value,
            LastName = user.LastName.Value,
            MiddleName = user.MiddleName?.Value,
            Suffix = user.Suffix?.Value,
            UserRole = user.UserRole
        };
    }
}