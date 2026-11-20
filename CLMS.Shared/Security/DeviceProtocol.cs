using System.Security.Cryptography;
using System.Text;

namespace CLMS.Shared.Security;

/// <summary>The versioned, culture-independent wire contract shared by the agent and server.</summary>
public static class DeviceProtocol
{
    public const string Algorithm = "ES256";
    public const string Authenticate = "authenticate";
    public const string Complete = "complete";
    public const string RotateCurrent = "rotate-current";
    public const string RotateReplacement = "rotate-replacement";

    /// <summary>Builds signed UTF-8 bytes. Every field has a fixed encoding and the payload ends in a newline.</summary>
    public static byte[] Payload(string purpose, string audience, Guid device, Guid credential,
        Guid context, Guid challenge, string nonce, string replacementFingerprint = "")
    {
        if (string.IsNullOrWhiteSpace(audience) || audience.Any(char.IsControl) || audience.Length > 200)
            throw new ArgumentException("Invalid authentication audience.");
        Decode(nonce, 32);
        if (replacementFingerprint.Length != 0) Decode(replacementFingerprint, 32);
        if (purpose is not (Authenticate or Complete or RotateCurrent or RotateReplacement))
            throw new ArgumentException("Unsupported proof purpose.");
        return Encoding.UTF8.GetBytes(string.Join('\n', "CLMS.Device.v1", purpose, audience,
            device.ToString("D"), credential.ToString("D"), context.ToString("D"), challenge.ToString("D"),
            nonce, replacementFingerprint) + "\n");
    }

    /// <summary>Encodes binary wire values as canonical, unpadded base64url.</summary>
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Rejects oversized, padded, noncanonical or incorrectly sized wire values before use.</summary>
    public static byte[] Decode(string value, int? size = null)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 1024 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new FormatException("Invalid binary encoding.");
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        if (Encode(bytes) != value || size.HasValue && bytes.Length != size.Value) throw new FormatException("Invalid binary encoding.");
        return bytes;
    }

    /// <summary>Identifies a canonical public key without retaining private key material.</summary>
    public static string Fingerprint(ReadOnlySpan<byte> spki) => Encode(SHA256.HashData(spki));
}
