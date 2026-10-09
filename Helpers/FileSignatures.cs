using System.Text;

namespace GharsPlatform.Helpers;

/// <summary>
/// Checks that an upload's first bytes match the format its extension claims. The extension and
/// the declared content type are both chosen by the browser; the bytes are what the file is.
/// </summary>
/// <remarks>
/// A signature check is not a malware scan: it stops a renamed file (an HTML page saved as .jpg,
/// an executable saved as .pdf) from being stored under a type it is not. Every extension any
/// <see cref="FileValidationHelper"/> profile accepts has a rule here; an extension without one
/// is refused.
/// </remarks>
public static class FileSignatures
{
    private const int HeadLength = 4096;

    public static bool Matches(IFormFile file, string extension)
    {
        byte[] head;
        try
        {
            using var stream = file.OpenReadStream();
            head = new byte[Math.Min(HeadLength, Math.Max(0, (int)Math.Min(file.Length, HeadLength)))];
            var read = 0;
            while (read < head.Length)
            {
                var n = stream.Read(head, read, head.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read < head.Length) Array.Resize(ref head, read);
        }
        catch (IOException)
        {
            return false;
        }

        return Matches(head, extension);
    }

    public static bool Matches(ReadOnlySpan<byte> head, string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".pdf":
                // "%PDF-" normally starts the file; the format allows it within the first 1024 bytes.
                return IndexOf(head[..Math.Min(head.Length, 1024)], "%PDF-"u8) >= 0;
            case ".png":
                return head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            case ".jpg":
            case ".jpeg":
                return head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF });
            case ".gif":
                return head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8);
            case ".webp":
                return head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8);
            case ".mp4":
            case ".mov":
                // ISO base media: a box size, then the box type. MP4 and current QuickTime files open
                // with "ftyp"; older QuickTime files can open with another top-level box.
                if (head.Length < 8) return false;
                var box = head[4..8];
                return box.SequenceEqual("ftyp"u8)
                       || (extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                           && (box.SequenceEqual("moov"u8) || box.SequenceEqual("mdat"u8) || box.SequenceEqual("wide"u8)
                               || box.SequenceEqual("free"u8) || box.SequenceEqual("skip"u8) || box.SequenceEqual("pnot"u8)));
            case ".webm":
                return head.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 });
            case ".docx":
            case ".xlsx":
            case ".pptx":
                return head.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            case ".doc":
            case ".xls":
            case ".ppt":
                return head.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });
            case ".csv":
                // Plain text: no NUL bytes, and valid UTF-8 (or a UTF-8/UTF-16 byte order mark that
                // spreadsheet exports sometimes write).
                if (head.StartsWith(new byte[] { 0xFF, 0xFE }) || head.StartsWith(new byte[] { 0xFE, 0xFF })) return true;
                if (head.IndexOf((byte)0) >= 0) return false;
                return IsUtf8Prefix(head);
            default:
                return false;
        }
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) => haystack.IndexOf(needle);

    // The head may cut a multi-byte character in two, so up to three trailing bytes are ignored.
    private static bool IsUtf8Prefix(ReadOnlySpan<byte> head)
    {
        var decoder = new UTF8Encoding(false, throwOnInvalidBytes: true);
        for (var trim = 0; trim <= Math.Min(3, head.Length); trim++)
        {
            try
            {
                decoder.GetCharCount(head[..(head.Length - trim)]);
                return true;
            }
            catch (DecoderFallbackException)
            {
            }
        }
        return false;
    }
}
