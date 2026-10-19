namespace Lantoss.Core;

/// <summary>Keeps existing device identity, paired keys, history and sync grants through the rename.</summary>
public static class AppDataPaths
{
    public static string DefaultRoot(string localApplicationData)
    {
        string current = Path.Combine(localApplicationData, "Lantoss");
        string legacy = Path.Combine(localApplicationData, "FlowShare");
        static bool HasMetadata(string root) => File.Exists(Path.Combine(root, "metadata.json")) || File.Exists(Path.Combine(root, "metadata.json.bak"));
        // Reuse the entire legacy store in place; copying only metadata would lose sync-state
        // and could let two app versions independently overwrite a shared identity's state.
        return !HasMetadata(current) && HasMetadata(legacy) ? legacy : current;
    }
}
