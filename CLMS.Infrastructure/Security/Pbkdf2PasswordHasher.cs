using System;
using System.Security.Cryptography;
using System.Text;
using CLMS.Domain.Common;

namespace CLMS.Infrastructure.Security;

public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;       // 128-bit salt 
    private const int KeySize = 32;        // 256-bit derived key size 
    private const int Iterations = 100_000; // Number of PBKDF2 iterations to slow down brute-force attacks

    public string HashPassword(string password)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));

        // Generate a cryptographically secure 128-bit random salt.
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[SaltSize];
        rng.GetBytes(salt);

        // Derive the 256-bit hash key using PBKDF2 with SHA-256 over 100,000 iterations.
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(KeySize);

        // Convert binary values to Base64 strings for standard text storage in databases.
        var base64Salt = Convert.ToBase64String(salt);
        var base64Hash = Convert.ToBase64String(hash);

        // Return all parameters together formatted as "{iterations}.{salt}.{hash}".
        // Storing iterations alongside salt enables seamless work-factor upgrades in the future.
        return $"{Iterations}.{base64Salt}.{base64Hash}";
    }

    public bool VerifyHashedPassword(string hashedPassword, string providedPassword)
    {
        if (hashedPassword is null) throw new ArgumentNullException(nameof(hashedPassword));
        if (providedPassword is null) throw new ArgumentNullException(nameof(providedPassword));

        // Parse the stored string into its three constituent parts.
        var parts = hashedPassword.Split('.', 3);
        if (parts.Length != 3) return false;

        // Extract and validate the iteration count.
        if (!int.TryParse(parts[0], out var iterations)) return false;

        // Decode the stored Base64 salt and expected hash bytes.
        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);

        // Re-hash the incoming candidate password using the original salt and iteration count.
        using var pbkdf2 = new Rfc2898DeriveBytes(providedPassword, salt, iterations, HashAlgorithmName.SHA256);
        var actualHash = pbkdf2.GetBytes(expectedHash.Length);

        // Perform a constant-time comparison to prevent side-channel timing attacks.
        // Standard byte or string comparisons terminate early upon mismatch, exposing execution timing cues.
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}