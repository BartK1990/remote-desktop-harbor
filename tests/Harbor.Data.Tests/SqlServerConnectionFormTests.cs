using Harbor.Data.Providers;
using Microsoft.Data.SqlClient;

namespace Harbor.Data.Tests;

// SPEC FR-27, ADR-0014
public sealed class SqlServerConnectionFormTests
{
    private readonly SqlServerProvider _provider = new();

    [Fact]
    public void Sql_login_round_trips()
    {
        var form = new DatabaseServerConnection
        {
            Server = "10.0.0.5,1433", Database = "Harbor", Authentication = DatabaseAuthentication.SqlLogin,
            UserName = "harbor", Password = "p;a'ss", TrustServerCertificate = true,
        };

        var cs = _provider.BuildConnectionString(form);
        var builder = new SqlConnectionStringBuilder(cs);

        Assert.Equal("10.0.0.5,1433", builder.DataSource);
        Assert.Equal("Harbor", builder.InitialCatalog);
        Assert.False(builder.IntegratedSecurity);
        Assert.Equal("p;a'ss", builder.Password);
        Assert.Equal(form, _provider.ParseConnectionString(cs));
        Assert.True(_provider.CanEditAsFields(cs));
    }

    [Theory]
    [InlineData(DatabaseAuthentication.Windows)]
    [InlineData(DatabaseAuthentication.EntraId)]
    [InlineData(DatabaseAuthentication.EntraIdInteractive)]
    public void Other_methods_round_trip_without_a_login(DatabaseAuthentication authentication)
    {
        var form = new DatabaseServerConnection { Server = "sql01", Database = "Harbor", Authentication = authentication };

        var cs = _provider.BuildConnectionString(form);

        Assert.DoesNotContain("Password", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(form, _provider.ParseConnectionString(cs));
    }

    [Fact]
    public void Switching_away_from_a_login_drops_the_password_and_keeps_other_keywords()
    {
        var existing = "Server=sql01;Database=Harbor;User ID=a;Password=secret;Application Name=Harbor;Connect Timeout=5";
        var form = _provider.ParseConnectionString(existing) with { Authentication = DatabaseAuthentication.Windows };

        var builder = new SqlConnectionStringBuilder(_provider.BuildConnectionString(form, existing));

        Assert.True(builder.IntegratedSecurity);
        Assert.Equal("", builder.Password);
        Assert.Equal("", builder.UserID);
        Assert.Equal("Harbor", builder.ApplicationName);
        Assert.Equal(5, builder.ConnectTimeout);
    }

    [Theory]
    [InlineData("Server=sql01;Database=Harbor;Integrated Security=true;Encrypt=true")]
    [InlineData("Server=tcp:x.database.windows.net;Database=Harbor;Authentication=Active Directory Default")]
    [InlineData("Server=sql01;Database=Harbor;Integrated Security=true;Application Name=Harbor")]
    public void Documented_strings_can_be_edited_as_fields(string connectionString) =>
        Assert.True(_provider.CanEditAsFields(connectionString));

    [Theory]
    [InlineData("Server=x;Database=Harbor;Authentication=Active Directory Password;User ID=a@b.c;Password=x")]
    [InlineData("not a connection string")]
    public void Strings_the_form_cannot_describe_stay_raw(string connectionString) =>
        Assert.False(_provider.CanEditAsFields(connectionString));
}
