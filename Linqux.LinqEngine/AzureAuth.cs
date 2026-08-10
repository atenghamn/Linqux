using Microsoft.Data.SqlClient;

namespace Linqux.LinqEngine;

/// <summary>
/// Performs the Azure AD interactive sign-in that backs <c>Authentication=Active Directory Interactive</c>.
/// The acquired token is cached by Microsoft.Data.SqlClient in this process and is reused silently by
/// subsequent connections (scaffolding, query execution), so the browser prompt appears only once.
/// </summary>
public static class AzureAuth
{
    /// <summary>
    /// Opens (and immediately closes) a connection using interactive auth, which launches the system
    /// browser so the user can sign in. Returns once the token has been acquired.
    /// </summary>
    public static async Task SignInInteractiveAsync(string connectionString, CancellationToken ct = default)
    {
        var interactiveConnectionString = ConnectionStringBuilder.EnableInteractiveAuth(connectionString);

        await using var connection = new SqlConnection(interactiveConnectionString);
        await connection.OpenAsync(ct);
    }
}
