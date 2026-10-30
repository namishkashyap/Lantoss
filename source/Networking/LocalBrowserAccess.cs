namespace Lantoss.Networking;

/// <summary>Names are an explicit allowlist; a friendly address must not weaken host or same-origin checks.</summary>
public static class LocalBrowserAccess
{
    public static bool HostAllowed(string listenerUrl, string host, int? port, Func<string, bool> claimed)
        => Uri.TryCreate(listenerUrl, UriKind.Absolute, out Uri? endpoint) && (port ?? (endpoint.Scheme == "https" ? 443 : 80)) == endpoint.Port
            && (host.TrimEnd('.').Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase) || claimed(host.TrimEnd('.')));
    public static bool SameOrigin(string origin, string host, int? port, string site, string scheme = "http")
        => Uri.TryCreate(origin, UriKind.Absolute, out Uri? address) && address.Scheme == scheme && address.UserInfo.Length == 0
            && address.AbsolutePath == "/" && address.Query.Length == 0 && address.Fragment.Length == 0
            && address.Host.TrimEnd('.').Equals(host.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) && address.Port == (port ?? (scheme == "https" ? 443 : 80)) && site != "cross-site";
}
