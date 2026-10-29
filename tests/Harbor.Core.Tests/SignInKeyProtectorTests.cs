using Harbor.Core.Identity;
using Harbor.Core.Security;

namespace Harbor.Core.Tests;

// SPEC FR-28, ADR-0016
public class SignInKeyProtectorTests
{
    private static readonly UserIdentity Windows = new("CONTOSO\\alice", "Alice", null, IdentitySource.Windows);
    private static readonly UserIdentity Entra = new("bob@contoso.com", "Bob", "bob@contoso.com", IdentitySource.EntraId);
    private static readonly UserIdentity Anonymous = new("Carol", "Carol (not signed in)", null, IdentitySource.Anonymous);

    [Fact]
    public void Signed_in_users_wrap_and_unwrap_without_a_passphrase()
    {
        var key = SecretCipher.GenerateKey();

        var blob = new SignInKeyProtector(Windows).Protect(key);

        Assert.Equal(key, new SignInKeyProtector(Entra).Unprotect(blob));
        Assert.Equal(SignInKeyProtector.SchemeName, new SignInKeyProtector(Entra).Scheme);
    }

    [Fact]
    public void Users_who_are_not_signed_in_are_refused()
    {
        var blob = new SignInKeyProtector(Windows).Protect(SecretCipher.GenerateKey());
        var anonymous = new SignInKeyProtector(Anonymous);

        Assert.Throws<VaultKeyException>(() => anonymous.Unprotect(blob));
        Assert.Throws<VaultKeyException>(() => anonymous.Protect(SecretCipher.GenerateKey()));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1 })]
    [InlineData(new byte[] { 9, 1, 2, 3 })]
    public void Unknown_formats_are_rejected(byte[] blob) =>
        Assert.Throws<VaultKeyException>(() => new SignInKeyProtector(Windows).Unprotect(blob));
}
