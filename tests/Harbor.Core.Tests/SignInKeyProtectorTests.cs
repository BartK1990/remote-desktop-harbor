using Harbor.Core.Security;

namespace Harbor.Core.Tests;

// SPEC FR-28, ADR-0016, ADR-0018
public class SignInKeyProtectorTests
{
    [Fact]
    public void Wraps_and_unwraps_without_a_passphrase()
    {
        var key = SecretCipher.GenerateKey();

        var blob = new SignInKeyProtector().Protect(key);

        Assert.Equal(key, new SignInKeyProtector().Unprotect(blob));
        Assert.Equal(SignInKeyProtector.SchemeName, new SignInKeyProtector().Scheme);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1 })]
    [InlineData(new byte[] { 9, 1, 2, 3 })]
    public void Unknown_formats_are_rejected(byte[] blob) =>
        Assert.Throws<VaultKeyException>(() => new SignInKeyProtector().Unprotect(blob));
}
