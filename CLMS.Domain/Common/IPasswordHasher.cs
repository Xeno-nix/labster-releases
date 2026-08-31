namespace CLMS.Domain.Common;

public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a plaintext password and returns a string containing parameters, salt and hash.
    /// Format: {iterations}.{base64salt}.{base64hash}
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Verifies a plaintext password against a previously hashed value.
    /// </summary>
    bool VerifyHashedPassword(string hashedPassword, string providedPassword);
}
