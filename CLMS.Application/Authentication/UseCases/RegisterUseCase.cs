using CLMS.Application.Authentication.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Authentication;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Shared.AuthRequests;
using System;
using System.Threading.Tasks;

namespace CLMS.Application.Authentication.UseCases;

public sealed class RegisterUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public RegisterUseCase(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task ExecuteAsync(RegisterRequest request)
    {
        var existingUser =
            await _userRepository.FindByEmailAsync(request.Email);

        if (existingUser is not null)
        {
            throw new InvalidOperationException(
                "A user with this email already exists.");
        }

        var passwordHash =
            _passwordHasher.Hash(request.Password);

        var user = new User(
            Guid.NewGuid(),
            new Name(request.FirstName),
            new Name(request.LastName),
            request.MiddleInitial is not null
                ? new Name(request.MiddleInitial)
                : null,
            request.Suffix is not null
                ? new Name(request.Suffix)
                : null,
            request.Email,
            Role.Admin,
            passwordHash);

        await _userRepository.AddAsync(user);
    }
}