namespace Linqux.LinqEngine;

/// <summary>
/// Builds connection strings for interactive Azure AD sign-in, so users can authenticate
/// with a browser popup instead of embedding a username and password.
/// </summary>
public static class ConnectionStringBuilder
{
    /// <summary>Connection string keywords that carry credentials or an existing auth mode; removed so the browser is used.</summary>
    private static readonly HashSet<string> CredentialKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "User ID", "UID", "User", "Password", "Pwd", "Authentication", "Access Token",
    };

    /// <summary>
    /// Rewrites <paramref name="connectionString"/> to use Azure AD interactive authentication
    /// (Microsoft.Data.SqlClient "Authentication=Active Directory Interactive"). Credential-bearing
    /// keywords are stripped so the browser sign-in prompt is shown.
    /// </summary>
    public static string EnableInteractiveAuth(string connectionString)
    {
        var kept = new List<string>();

        foreach (var part in connectionString.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;

            var separator = trimmed.IndexOf('=');
            if (separator > 0 && CredentialKeywords.Contains(trimmed[..separator].Trim()))
            {
                continue;
            }

            kept.Add(trimmed);
        }

        kept.Add("Authentication=Active Directory Interactive");
        return string.Join(";", kept) + ";";
    }

    /// <summary>
    /// Returns true when the connection string itself already requests browser-based interactive
    /// authentication (for example a saved connection that embeds <c>Authentication=Active Directory Interactive</c>).
    /// </summary>
    public static bool UsesInteractiveAuth(string connectionString)
    {
        foreach (var part in connectionString.Split(';'))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;

            if (!part[..separator].Trim().Equals("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return part[(separator + 1)..].Contains("Interactive", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
