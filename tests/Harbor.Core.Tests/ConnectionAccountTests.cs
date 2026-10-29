using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-09, FR-33, ADR-0019
public class ConnectionAccountTests
{
    private static readonly ConnectionEntry Entry = ConnectionEntry.CreateNew("srv") with
    {
        Host = "srv", Domain = "CONTOSO", UserName = "admin", PasswordCipher = [1],
        Accounts = [ConnectionAccount.CreateNew("svc"), ConnectionAccount.CreateNew("ops", "LAB"), ConnectionAccount.CreateNew(@".\local")],
    };

    [Fact]
    public void Account_without_a_domain_uses_the_entry_domain()
    {
        Assert.Equal(@"CONTOSO\admin", Entry.QualifiedUserNameFor(null));
        Assert.Equal(@"CONTOSO\svc", Entry.QualifiedUserNameFor(Entry.Accounts[0]));
        Assert.Equal(@"LAB\ops", Entry.QualifiedUserNameFor(Entry.Accounts[1]));
        Assert.Equal(@".\local", Entry.QualifiedUserNameFor(Entry.Accounts[2]));
    }

    [Fact]
    public void For_account_connects_as_that_account_without_the_default_password()
    {
        Assert.Same(Entry, Entry.ForAccount(null));

        var launched = Entry.ForAccount(Entry.Accounts[1]);
        Assert.Equal(@"LAB\ops", launched.QualifiedUserName);
        Assert.Equal((Entry.Id, "srv"), (launched.Id, launched.Host));
        Assert.Null(launched.PasswordCipher);
        Assert.Empty(launched.Accounts);
    }

    [Fact]
    public void Find_account_returns_null_for_the_default_account()
    {
        Assert.Null(Entry.FindAccount(null));
        Assert.Same(Entry.Accounts[1], Entry.FindAccount(Entry.Accounts[1].Id));
        Assert.Null(Entry.FindAccount(Guid.NewGuid()));
    }

    [Fact]
    public void Session_without_an_account_counts_as_using_any_account()
    {
        var now = DateTimeOffset.UtcNow;
        var old = new ActiveSession(Guid.NewGuid(), Entry.Id, "bob", null, null, "PC", now, now, now);
        Assert.True(old.UsesAccount(@"CONTOSO\admin"));
        Assert.False((old with { AccountName = @"CONTOSO\svc" }).UsesAccount(@"CONTOSO\admin"));
    }
}
