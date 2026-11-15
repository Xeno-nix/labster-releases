using CLMS.Shared.Dtos;

namespace CLMS.Web.Services.ScreenCasting;

public static class JpegFrameValidator
{
    public static bool IsValid(ScreenCastFrame frame)
    {
        var bytes = frame.JpegBytes;
        if (bytes is null || bytes.Length is < 12 or > ScreenCastProtocol.MaximumFrameBytes
            || frame.Width is < 1 or > 1280 || frame.Height is < 1 or > 4096
            || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            return false;

        // Check the JPEG's own dimensions rather than trusting client metadata.
        var offset = 2;
        while (offset < bytes.Length - 2)
        {
            if (bytes[offset++] != 0xff) return false;
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker is 0xda or 0xd9) return false;
            if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
            if (offset + 2 > bytes.Length) return false;
            var length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || offset + length > bytes.Length) return false;
            var isFrameHeader = marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc);
            if (isFrameHeader)
                return length >= 8
                    && ((bytes[offset + 3] << 8) | bytes[offset + 4]) == frame.Height
                    && ((bytes[offset + 5] << 8) | bytes[offset + 6]) == frame.Width;
            offset += length;
        }
        return false;
    }
}
