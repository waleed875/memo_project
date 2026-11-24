using AuditMemoSystem.Core.Validation;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class AttachmentFilePolicyTests
{
    [Theory]
    [InlineData("memo.jpg")]
    [InlineData("memo.PDF")]
    [InlineData("memo.tiff")]
    public void IsSupported_AcceptsAllowedAttachmentTypes(string fileName)
    {
        Assert.True(AttachmentFilePolicy.IsSupported(fileName));
    }

    [Fact]
    public void Validate_RejectsUnsupportedAttachmentType()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AttachmentFilePolicy.Validate("memo.exe", [1, 2, 3]));
    }

    [Fact]
    public void Validate_RejectsEmptyAttachment()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AttachmentFilePolicy.Validate("memo.pdf", []));
    }

    [Fact]
    public void Validate_RejectsFileWithMismatchedSignature()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AttachmentFilePolicy.Validate("memo.jpg", [0x25, 0x50, 0x44, 0x46]));
    }

    [Fact]
    public void Validate_AcceptsRecognizablePdf()
    {
        AttachmentFilePolicy.Validate("memo.pdf", System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF"));
    }

    [Theory]
    [InlineData("memo.pdf", null, true)]
    [InlineData("memo.bin", "application/pdf", true)]
    [InlineData("memo.jpg", "image/jpeg", false)]
    public void IsPdf_RecognizesPdfByExtensionOrContentType(string fileName, string? contentType, bool expected)
    {
        Assert.Equal(expected, AttachmentFilePolicy.IsPdf(fileName, contentType));
    }

    [Fact]
    public void Validate_RejectsOversizedAttachment()
    {
        var data = new byte[AttachmentFilePolicy.MaximumFileSizeBytes + 1];
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        Assert.Throws<InvalidOperationException>(() => AttachmentFilePolicy.Validate("memo.jpg", data));
    }

    [Fact]
    public void ResolveLegacyPath_RejectsRootedAndTraversalPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "audit-memo-attachments");
        Assert.Throws<InvalidOperationException>(() => AttachmentFilePolicy.ResolveLegacyPath(root, "..\\outside.pdf"));
        Assert.Throws<InvalidOperationException>(() => AttachmentFilePolicy.ResolveLegacyPath(root, Path.GetTempPath()));
    }

    [Fact]
    public void ResolveLegacyPath_AllowsOnlyAChildOfTrustedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "audit-memo-attachments");
        var path = AttachmentFilePolicy.ResolveLegacyPath(root, "incoming\\memo.pdf");
        Assert.StartsWith(Path.GetFullPath(root), path, StringComparison.OrdinalIgnoreCase);
    }
}
