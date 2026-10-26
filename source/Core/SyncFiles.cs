using System.Security.Cryptography;

namespace Lantoss.Core;

/// <summary>
/// Performs guarded sync filesystem changes. Atomic replacement retains prior contents in the excluded version directory, so interrupted writes do not destroy the original.
/// </summary>
public static class SyncFiles
{
    public static async Task<string> HashAsync(string file, CancellationToken token)
    {
        FileCatalog.CheckSafePath(file);
        if (!File.Exists(file))
            return "";
        await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
    public static void ValidateSnapshot(Manifest snapshot)
    {
        if (snapshot.Entries is null || snapshot.Entries.Count > FileCatalog.MaxEntries)
            throw new IOException("The remote folder is too large.");
        if (snapshot.Entries.Count == 0)
            return;
        FileCatalog.ValidateManifest(snapshot);
    }
    public static string PathIn(string root, string relative)
    {
        string path = FileCatalog.Within(root, relative);
        FileCatalog.CheckSafePath(path);
        return path;
    }
    public static void MakeDirectory(string root, string relative)
    {
        FileCatalog.CheckSafeDirectory(root);
        if (!Directory.Exists(root))
            throw new IOException("The sync root is unavailable.");
        string path = PathIn(root, relative);
        FileCatalog.CheckSafeDirectory(path);
        Directory.CreateDirectory(path);
    }
    // Existing copies are retained by File.Replace so an edit is recoverable.
    public static async Task CommitAsync(string temporary, string root, string relative, string expectedHash, CancellationToken token)
    {
        string target = PathIn(root, relative);
        if (!string.Equals(await HashAsync(target, token), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The destination changed during sync. No replacement was made; retrying.");
        token.ThrowIfCancellationRequested();
        FileCatalog.CheckSafePath(target);
        if (File.Exists(target))
        {
            // Keep the historical directory name so existing backups stay excluded on mixed-version PCs.
            string versions = Path.Combine(root, ".flowshare-versions", Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relative.ToUpperInvariant())))[..24]);
            FileCatalog.CheckSafeDirectory(versions);
            Directory.CreateDirectory(versions);
            File.Replace(temporary, target, Path.Combine(versions, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak"));
        }
        else
            File.Move(temporary, target, false);
    }
    public static string ConflictPath(string relative, string remoteHash)
    {
        string folder = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] + "/" : "";
        string name = Path.GetFileNameWithoutExtension(relative);
        string extension = Path.GetExtension(relative);
        if (extension.Length > 40)
            extension = extension[..40];
        string suffix = " (conflict " + remoteHash[..12].ToLowerInvariant() + ")";
        int budget = Math.Max(1, 180 - suffix.Length - extension.Length);
        return folder + name[..Math.Min(name.Length, budget)] + suffix + extension;
    }
}
