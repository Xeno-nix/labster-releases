// CLMS.Application.Services/UserService.cs
using CLMS.Application.DTOs;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Enums;

namespace CLMS.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _repository;

    public UserService(
        IUserRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<UserDTO>> GetFacultyAsync()
    {
        var users = await _repository.GetByRoleAsync(Role.Faculty);

        return users.Select(MapToDto);
    }

    private static UserDTO MapToDto(
        User user)
    {
        return new UserDTO
        {
            Id = user.Id,
            FirstName = user.FirstName.Value,
            LastName = user.LastName.Value,
            MiddleName = user.MiddleName?.Value,
            Suffix = user.Suffix?.Value,
            Email = user.Email,
            UserRole = user.UserRole
        };
    }
}