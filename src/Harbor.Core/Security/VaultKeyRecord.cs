namespace Harbor.Core.Security;

/// <summary>A wrapped data-encryption key as persisted in a store's <c>VaultKeys</c> table.</summary>
public sealed record VaultKeyRecord(string Slot, Guid KeyId, string Scheme, byte[] ProtectedKey, DateTimeOffset CreatedUtc)
{
    public const string PrimarySlot = "primary";
}
