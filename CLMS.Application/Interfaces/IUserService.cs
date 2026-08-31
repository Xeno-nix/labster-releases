namespace CLMS.Application.Interfaces;
using CLMS.Application.DTOs;
public interface IUserService
{
    Task<IEnumerable<UserDTO>> GetFacultyAsync();
}