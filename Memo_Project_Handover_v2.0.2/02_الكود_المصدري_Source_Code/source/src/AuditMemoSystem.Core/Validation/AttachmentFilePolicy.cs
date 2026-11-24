namespace AuditMemoSystem.Core.Validation;

public static class AttachmentFilePolicy
{
    public const int MaximumFileSizeBytes = 10 * 1024 * 1024;

    public static void Validate(string? fileName, byte[]? data)
    {
        if (!IsSupported(fileName))
        {
            throw new InvalidOperationException("الملفات المدعومة هي الصور وملفات PDF فقط.");
        }

        if (data is null || data.Length == 0)
        {
            throw new InvalidOperationException("الملف المختار فارغ أو تعذر قراءته.");
        }

        if (data.Length > MaximumFileSizeBytes)
        {
            throw new InvalidOperationException("حجم المرفق يتجاوز الحد المسموح (10 ميغابايت).");
        }

        if (!HasExpectedSignature(fileName!, data))
        {
            throw new InvalidOperationException("محتوى المرفق لا يطابق نوع الملف المحدد.");
        }
    }

    public static void ValidateFileForRead(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.Exists)
        {
            throw new FileNotFoundException("الملف المرفق غير موجود.", file.FullName);
        }
        if (file.Length == 0 || file.Length > MaximumFileSizeBytes)
        {
            throw new InvalidOperationException("حجم المرفق غير مسموح به.");
        }
        if (!IsSupported(file.Name))
        {
            throw new InvalidOperationException("نوع المرفق غير مدعوم.");
        }
    }

    public static string ResolveLegacyPath(string trustedRoot, string legacyPath)
    {
        if (string.IsNullOrWhiteSpace(trustedRoot) || string.IsNullOrWhiteSpace(legacyPath) || Path.IsPathRooted(legacyPath))
        {
            throw new InvalidOperationException("مسار المرفق القديم غير موثوق.");
        }

        var root = Path.GetFullPath(trustedRoot);
        var candidate = Path.GetFullPath(Path.Combine(root, legacyPath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("مسار المرفق القديم يقع خارج مجلد التخزين الموثوق.");
        }
        return candidate;
    }

    public static bool IsSupported(string? fileName)
    {
        return Path.GetExtension(fileName ?? "").ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".pdf";
    }

    public static bool IsImage(string? fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Path.GetExtension(fileName ?? "").ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff";
    }

    public static bool IsPdf(string? fileName, string? contentType)
    {
        return string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(fileName ?? ""), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".tif" or ".tiff" => "image/tiff",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    private static bool HasExpectedSignature(string fileName, byte[] data)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".png" => StartsWith(data, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ".jpg" or ".jpeg" => StartsWith(data, 0xFF, 0xD8, 0xFF),
            ".gif" => StartsWithAscii(data, "GIF87a") || StartsWithAscii(data, "GIF89a"),
            ".bmp" => StartsWithAscii(data, "BM"),
            ".tif" or ".tiff" => StartsWith(data, 0x49, 0x49, 0x2A, 0x00) || StartsWith(data, 0x4D, 0x4D, 0x00, 0x2A),
            ".pdf" => StartsWithAscii(data, "%PDF-") && ContainsPdfEndMarker(data),
            _ => false
        };
    }

    private static bool StartsWith(byte[] data, params byte[] signature) =>
        data.Length >= signature.Length && data.AsSpan(0, signature.Length).SequenceEqual(signature);

    private static bool StartsWithAscii(byte[] data, string signature) =>
        data.Length >= signature.Length && data.AsSpan(0, signature.Length).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(signature));

    private static bool ContainsPdfEndMarker(byte[] data)
    {
        var tail = System.Text.Encoding.ASCII.GetString(data, Math.Max(0, data.Length - 1024), Math.Min(1024, data.Length));
        return tail.Contains("%%EOF", StringComparison.Ordinal);
    }
}
