namespace Harbor.Core.Services;

/// <summary>What to do with an entry's password when saving it.</summary>
public readonly record struct SecretUpdate
{
    private SecretUpdate(SecretUpdateMode mode, string? value) => (Mode, Value) = (mode, value);

    public SecretUpdateMode Mode { get; }
    public string? Value { get; }

    public static SecretUpdate Keep => new(SecretUpdateMode.Keep, null);
    public static SecretUpdate Clear => new(SecretUpdateMode.Clear, null);
    public static SecretUpdate Set(string value) => new(SecretUpdateMode.Set, value);
}

public enum SecretUpdateMode
{
    Keep = 0,
    Clear = 1,
    Set = 2,
}
