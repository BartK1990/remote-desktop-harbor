using Harbor.Core.Identity;

namespace Harbor.Core.Tests;

// SPEC FR-44, ADR-0015
public class AnonymousIdentityTests
{
    [Fact]
    public async Task Uses_the_entered_name_marked_as_not_signed_in()
    {
        var user = await new AnonymousIdentityProvider("  Bart K  ").SignInAsync(allowInteractive: true);

        Assert.Equal("Bart K", user.UserName);
        Assert.Equal("Bart K" + AnonymousIdentityProvider.Marker, user.DisplayName);
        Assert.Null(user.Email);
        Assert.Equal(IdentitySource.Anonymous, user.Source);
        Assert.Equal("Bart K (not signed in)", user.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Requires_a_name(string name) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AnonymousIdentityProvider(name).SignInAsync(true));

    [Fact]
    public void Signed_in_identities_keep_their_format() =>
        Assert.Equal("Alice <alice@contoso.com>", new UserIdentity("CONTOSO\\alice", "Alice", "alice@contoso.com", IdentitySource.Windows).ToString());
}
