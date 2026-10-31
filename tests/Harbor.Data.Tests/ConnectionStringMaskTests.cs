using Harbor.Data.Providers;

namespace Harbor.Data.Tests;

// SPEC FR-45, NFR-12: the Settings preview never shows a password.
public class ConnectionStringMaskTests
{
    [Fact]
    public void Passwords_are_masked_and_everything_else_is_kept()
    {
        var masked = ConnectionStringMask.Apply("Server=sql01;Database=Harbor;User ID=harbor_user;Password=\"s3cr;et!\";Encrypt=True");
        Assert.NotNull(masked);
        Assert.DoesNotContain("s3cr", masked, StringComparison.Ordinal);
        Assert.Contains("password=********", masked, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database=Harbor", masked, StringComparison.OrdinalIgnoreCase);

        var pwd = ConnectionStringMask.Apply("Server=sql01;UID=sa;PWD=hunter2");
        Assert.DoesNotContain("hunter2", pwd, StringComparison.Ordinal);
    }

    [Fact]
    public void Strings_without_a_password_are_unchanged_in_content()
    {
        var masked = ConnectionStringMask.Apply("Server=sql01;Database=Harbor;Integrated Security=true");
        Assert.Contains("integrated security=true", masked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ConnectionStringMask.Mask, masked, StringComparison.Ordinal);
        Assert.Null(ConnectionStringMask.Apply(null));
    }
}
