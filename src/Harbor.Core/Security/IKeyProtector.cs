namespace Harbor.Core.Security;

/// <summary>
/// Wraps (protects) a data-encryption key so that only the intended principal can unwrap it:
/// the current Windows user on this machine, an AD group, or a passphrase holder. See ADR-0005/0006.
/// </summary>
public interface IKeyProtector
{
    /// <summary>Stable identifier persisted next to the wrapped key, e.g. <c>dpapi-user+machine</c>.</summary>
    string Scheme { get; }

    byte[] Protect(ReadOnlySpan<byte> key);

    byte[] Unprotect(ReadOnlySpan<byte> protectedKey);
}

/// <summary>Maps a persisted scheme name back to a protector able to unwrap it.</summary>
public interface IKeyProtectorResolver
{
    IKeyProtector Resolve(string scheme);
}

public sealed class KeyProtectorResolver(IEnumerable<IKeyProtector> protectors) : IKeyProtectorResolver
{
    private readonly Dictionary<string, IKeyProtector> _byScheme =
        protectors.ToDictionary(p => p.Scheme, StringComparer.OrdinalIgnoreCase);

    public IKeyProtector Resolve(string scheme) =>
        _byScheme.TryGetValue(scheme, out var p)
            ? p
            : throw new VaultKeyException($"No key protector is available for scheme '{scheme}'.");
}

public class VaultKeyException : Exception
{
    public VaultKeyException(string message) : base(message) { }
    public VaultKeyException(string message, Exception inner) : base(message, inner) { }
}
