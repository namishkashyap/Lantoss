namespace Lantoss.Core;

/// <summary>Small, bounded messages from Explorer/sign-in to the existing desktop instance.</summary>
public sealed record LaunchRequest(string Kind, string[] Paths, string? PeerKey = null)
{
    public static LaunchRequest Parse(string[] args)
    {
        if (args.Length == 2 && args[0] == "--shell-request")
            return ShellSelection.Read(args[1]);
        List<string> paths = [];
        string? key = null;
        bool background = args.Contains("--background");
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--share")
                paths.Add(++i < args.Length ? args[i] : throw new InvalidDataException("Choose a file to share."));
            else if (args[i] == "--share-with")
                key = ++i < args.Length ? args[i] : throw new InvalidDataException("Choose a recipient PC.");
            else if (args[i] == "--data-dir")
                i++;
            else if (args[i] != "--background")
                throw new InvalidDataException("Unknown launch option: " + args[i]);
        }
        return new LaunchRequest(paths.Count > 0 ? "share" : background ? "background" : "activate", paths.ToArray(), key).Checked();
    }
    public LaunchRequest Checked()
    {
        if (Kind is not ("share" or "activate" or "background") || Paths == null || Paths.Length > 256 || (Kind == "share") != (Paths.Length > 0) || (Kind != "share" && PeerKey != null))
            throw new InvalidDataException("Invalid Lantoss launch request.");
        if (PeerKey != null && (PeerKey.Length != 64 || !PeerKey.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Invalid recipient identity.");
        if (Paths.Any(path => path == null || path.Length > 32760 || path.Any(char.IsControl) || !Path.IsPathFullyQualified(path)))
            throw new InvalidDataException("Choose existing local files or folders using absolute paths.");
        return this with
        {
            Paths = Paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            PeerKey = PeerKey?.ToUpperInvariant()
        };
    }
}
