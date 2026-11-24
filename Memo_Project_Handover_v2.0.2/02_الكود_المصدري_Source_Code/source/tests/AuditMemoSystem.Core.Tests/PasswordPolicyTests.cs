using AuditMemoSystem.Core.Validation;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("a secure phrase")]
    [InlineData("عبارة مرور آمنة")]
    public void ValidateConfirmation_AcceptsConfirmedPasswordsOfAnyComplexity(string password)
    {
        var accepted = PasswordPolicy.ValidateConfirmation(password, password);

        Assert.Equal(password, accepted);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("password123")]
    [InlineData("a secure phrase")]
    [InlineData("عبارة مرور آمنة")]
    public void EnsureValid_AcceptsPasswordsOfMinimumLength(string password)
    {
        PasswordPolicy.EnsureValid(password);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("short")]
    [InlineData("   ")]
    public void EnsureValid_RejectsShortPasswords(string password)
    {
        Assert.Throws<InvalidOperationException>(() => PasswordPolicy.EnsureValid(password));
    }

    [Fact]
    public void EnsureValid_RejectsOnlyAnEmptyPassword()
    {
        Assert.Throws<InvalidOperationException>(() => PasswordPolicy.EnsureValid(""));
        Assert.Throws<InvalidOperationException>(() => PasswordPolicy.EnsureValid(null));
    }

    [Fact]
    public void ValidateConfirmation_RejectsMismatchedValuesBeforePersistence()
    {
        Assert.Throws<InvalidOperationException>(() => PasswordPolicy.ValidateConfirmation("new-password", "different-password"));
    }

    [Fact]
    public void BCryptHash_DoesNotExposeThePlaintextPassword()
    {
        const string password = "كلمة";
        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        Assert.NotEqual(password, hash);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, hash));
    }
}
