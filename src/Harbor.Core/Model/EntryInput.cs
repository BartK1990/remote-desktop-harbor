namespace Harbor.Core.Model;

/// <summary>
/// Splits what people paste into one box into the entry's fields: <c>host:port</c> into host and port, and
/// <c>DOMAIN\user</c> into domain and user name. SPEC FR-05, FR-46
/// </summary>
public static class EntryInput
{
    /// <summary>
    /// <c>srv:3390</c> → (<c>srv</c>, 3390); <c>[::1]:3390</c> → (<c>[::1]</c>, 3390). A value without a port, or an IPv6
    /// address without brackets, is returned unchanged with no port.
    /// </summary>
    public static (string Host, int? Port) SplitHostPort(string value)
    {
        // IPv6 literal: [::1]:3390
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close > 0 && close + 1 < value.Length && value[close + 1] == ':' && int.TryParse(value[(close + 2)..], out var p6))
            {
                return (value[..(close + 1)], p6);
            }

            return (value, null);
        }

        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon && int.TryParse(value[(colon + 1)..], out var p))
        {
            return (value[..colon], p);
        }

        return (value, null);
    }

    /// <summary>
    /// <c>CONTOSO\jdoe</c> → (<c>CONTOSO</c>, <c>jdoe</c>); <c>.\admin</c> and <c>jdoe@contoso.com</c> are kept as the user
    /// name (no domain), because Windows reads them as written.
    /// </summary>
    public static (string? Domain, string UserName) SplitDomainUser(string value)
    {
        var slash = value.IndexOf('\\');
        if (slash <= 0 || slash == value.Length - 1 || value[..slash] == "." || value.IndexOf('\\', slash + 1) >= 0)
        {
            return (null, value);
        }

        return (value[..slash], value[(slash + 1)..]);
    }
}
