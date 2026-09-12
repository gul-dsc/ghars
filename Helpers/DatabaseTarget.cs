namespace GharsPlatform.Helpers;

/// <summary>
/// Answers "is the database this process is configured to write to on this machine?".
/// </summary>
/// <remarks>
/// <para>
/// The operational commands are expected to be run from a developer working copy against a remote
/// database, which avoids a deployment and an application-pool recycle. That makes
/// <c>ASPNETCORE_ENVIRONMENT</c> an unreliable guard on its own: <c>dotnet run</c> reads
/// <c>launchSettings.json</c>, which forces <c>Development</c>, so an environment-based gate waves a
/// command through even when it is pointed straight at production.
/// </para>
/// <para>
/// The connection string is the honest signal, and it can be read without opening a connection — so a
/// command can refuse before it touches the database at all.
/// </para>
/// </remarks>
internal static class DatabaseTarget
{
    /// <summary>True for a SQL Server instance on this machine.</summary>
    public static bool IsLocalServer(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource)) return false;

        // Strip the instance name and port: "localhost\SQLEXPRESS" is as local as "localhost".
        var host = dataSource.Split('\\')[0].Split(',')[0].Trim().ToLowerInvariant();

        return host is "." or "(local)" or "localhost" or "127.0.0.1" or "::1"
               || host.StartsWith("(localdb)", StringComparison.Ordinal)
               || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }
}
