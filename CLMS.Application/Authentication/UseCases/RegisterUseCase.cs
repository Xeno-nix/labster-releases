using CLMS.Application.Authentication.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Authentication;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Shared.AuthRequests;
using System;
using System.Threading.Tasks;

namespace CLMS.Application.Authentication.UseCases;

public sealed class RegisterUseCase(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher)
{
    /// <summary>
    /// Registers User
    /// </summary>
    /// <param name="request"> Request details. <see cref="RegisterRequest"/></param>
    /// <exception cref="InvalidOperationException">Email already been used.</exception>
    public async Task ExecuteAsync(RegisterRequest request)
    {
        // Search if user with same email already exists
        var existingUser = await userRepository.FindByEmailAsync(request.Email);

        if (existingUser is not null)
            throw new InvalidOperationException("A user with this email already exists.");

        // hash password
        var passwordHash = passwordHasher.Hash(request.Password);

        Console.WriteLine("RegisterUseCase : " + request.Role);
        
        // generate new user entity
        var user = new User(
            Guid.NewGuid(),
            new Name(request.FirstName),
            new Name(request.LastName),
            request.MiddleName is not null
                ? new Name(request.MiddleName)
                : null,
            request.Suffix is not null
                ? new Name(request.Suffix)
                : null,
            request.Email,
            request.Role,
            passwordHash);

        // Saves user entity
        await userRepository.AddAsync(user);
    }
}