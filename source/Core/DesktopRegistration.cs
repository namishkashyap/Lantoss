namespace Lantoss.Core;

public sealed record RegistryText(string Key, string Name, string Value);

/// <summary>Pure plans for current-user Explorer and sign-in registration. Never invokes a shell.</summary>
public static class DesktopRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string SettingsKey = @"Software\Lantoss\Desktop";
    public static readonly string[] MenuKeys = [@"Software\Classes\*\shell\Lantoss", @"Software\Classes\Directory\shell\Lantoss"];

    public static string Executable(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.GetFileName(executable).Equals("Lantoss.exe", StringComparison.OrdinalIgnoreCase) || executable.Any(char.IsControl) || executable.Contains('"'))
            throw new InvalidDataException("Use the installed Lantoss executable.");
        return Path.GetFullPath(executable);
    }
    public static string StartupCommand(string executable) => '"' + Executable(executable) + "\" --background";
    public static RegistryText[] Menu(string executable, IEnumerable<Peer> peers)
    {
        string app = Executable(executable);
        // A key fingerprint is the target, never a possibly stale or spoofed network address.
        Peer[] targets = peers.Where(peer => peer.Paired && !peer.Blocked && peer.Fingerprint.Length == 64 && peer.Fingerprint.All(Uri.IsHexDigit))
            .DistinctBy(peer => peer.Fingerprint, StringComparer.OrdinalIgnoreCase).OrderBy(peer => peer.Name, StringComparer.OrdinalIgnoreCase).Take(32).ToArray();
        List<RegistryText> plan = [];
        foreach (string root in MenuKeys)
        {
            plan.Add(new(root, "MUIVerb", "Share with Lantoss"));
            plan.Add(new(root, "SubCommands", ""));
            plan.Add(new(root, "Icon", '"' + app + "\",0"));
            Add("00.choose", "Choose a PC…", null);
            foreach (Peer peer in targets)
                Add("pc." + peer.Fingerprint.ToUpperInvariant(), Label(peer.Name), peer.Fingerprint.ToUpperInvariant());
            void Add(string id, string label, string? key)
            {
                string verb = root + @"\shell\" + id;
                plan.Add(new(verb, "MUIVerb", label));
                // Windows launches one process per selected item (up to its Document-model limit).
                // The single-instance pipe coalesces those invocations before showing the batch.
                plan.Add(new(verb, "MultiSelectModel", "Document"));
                plan.Add(new(verb + @"\command", "", '"' + app + "\" " + (key == null ? "" : "--share-with " + key + " ") + "--share \"%1\""));
            }
        }
        return plan.ToArray();
    }
    private static string Label(string name) => new string(name.Where(character => !char.IsControl(character)).Take(64).ToArray()).Replace("&", "&&") is { Length: > 0 } text ? text : "Windows PC";
}
