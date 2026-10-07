using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using System.Text;
using CLMS.Application.Identity.Interfaces;

namespace CLMS.Infrastructure.Auth;

public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private const int DegreeOfParallelism = 4;
    private const int MemorySize = 65536; // 64 MB
    private const int Iterations = 3;
    
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        byte[] hash = DeriveHash(
            password,
            salt,
            HashSize);

        // Store everything required to verify the password.
        return $"{DegreeOfParallelism}:{MemorySize}:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(passwordHash);

        string[] parts = passwordHash.Split(':');

        if (parts.Length != 5)
            return false;

        if (!int.TryParse(parts[0], out int parallelism) ||
            !int.TryParse(parts[1], out int memorySize) ||
            !int.TryParse(parts[2], out int iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;

        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedHash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actualHash = DeriveHash(
            password,
            salt,
            expectedHash.Length,
            parallelism,
            memorySize,
            iterations);

        return CryptographicOperations.FixedTimeEquals(
            actualHash,
            expectedHash);
    }
    
    private static byte[] DeriveHash(
        string password,
        byte[] salt,
        int hashSize,
        int parallelism = DegreeOfParallelism,
        int memorySize = MemorySize,
        int iterations = Iterations)
    {
        using var argon2 = new Argon2id(
            Encoding.UTF8.GetBytes(password));

        argon2.Salt = salt;
        argon2.DegreeOfParallelism = parallelism;
        argon2.MemorySize = memorySize;
        argon2.Iterations = iterations;

        return argon2.GetBytes(hashSize);
    }
}