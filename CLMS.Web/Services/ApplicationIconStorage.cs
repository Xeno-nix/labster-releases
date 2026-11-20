using System.Buffers.Binary;
using CLMS.Application.Identity.Interfaces;
using CLMS.Domain.Entities.UnitManagement;

namespace CLMS.Web.Services;

public sealed class ApplicationIconStorage(IWebHostEnvironment environment, ICurrentUser user)
{
    public const int MaximumBytes = 256 * 1024;
    private string DirectoryPath => Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "application-icons");

    public async Task<string> UploadAsync(Stream stream, string contentType, CancellationToken ct = default)
    {
        await user.RequireAdminAsync();
        using var data = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (data.Length + read > MaximumBytes) throw new ArgumentException("Icons must be at most 256 KB.");
            await data.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var bytes = data.ToArray();
        var extension = ValidateImage(bytes, contentType);
        var file = Guid.NewGuid().ToString("N") + extension;
        Directory.CreateDirectory(DirectoryPath);
        await File.WriteAllBytesAsync(Path.Combine(DirectoryPath, file), bytes, ct);
        return "/application-icons/" + file;
    }

    internal static string ValidateImage(byte[] bytes, string contentType)
    {
        var png = bytes.Length >= 33 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) is > 0 and <= 4096
            && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) is > 0 and <= 4096;
        var webp = bytes.Length >= 20 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) == bytes.Length - 8
            && (bytes.AsSpan(12, 4).SequenceEqual("VP8 "u8) || bytes.AsSpan(12, 4).SequenceEqual("VP8L"u8)
                || bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8));
        if (bytes.Length <= MaximumBytes && contentType == "image/png" && png) return ".png";
        if (bytes.Length <= MaximumBytes && contentType == "image/webp" && webp) return ".webp";
        throw new ArgumentException("Upload a PNG or WebP image of at most 256 KB.");
    }

    public async Task DeleteAsync(string? path)
    {
        await user.RequireAdminAsync();
        if (path is not null && ApplicationProcessNames.IsSafeIconPath(path))
            File.Delete(Path.Combine(DirectoryPath, Path.GetFileName(path)));
    }
}
