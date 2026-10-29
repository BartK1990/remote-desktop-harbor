namespace Harbor.Core.Model;

/// <summary>
/// An additional user account of an entry, e.g. a second administrator on a Windows Server that allows several
/// sessions at once. The entry's own <see cref="ConnectionEntry.UserName"/> is its default account; these come after
/// it. Like the entry's, the password is only held as an encrypted envelope bound to <see cref="Id"/>.
/// See SPEC FR-09, ADR-0019.
/// </summary>
public sealed record ConnectionAccount
{
    public required Guid Id { get; init; }

    /// <summary>Optional; when empty the entry's <see cref="ConnectionEntry.Domain"/> is used.</summary>
    public string? Domain { get; init; }

    public required string UserName { get; init; }
    public byte[]? PasswordCipher { get; init; }

    public bool HasPassword => PasswordCipher is { Length: > 0 };

    public static ConnectionAccount CreateNew(string userName, string? domain = null) =>
        new() { Id = Guid.NewGuid(), UserName = userName, Domain = domain };
}
