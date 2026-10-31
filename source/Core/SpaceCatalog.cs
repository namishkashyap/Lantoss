namespace Lantoss.Core;

public sealed class SpacePermissionException(string message) : IOException(message);
// Host-owned folder registrations. Browser callers only provide IDs and relative paths.
/// <summary>
/// Maps public space IDs and relative paths to explicitly registered local roots. Root paths stay local; permissions must be checked again when committing a transfer.
/// </summary>
public sealed class SpaceCatalog(MetadataStore store)
{
    public event Action<string>? Changed;
    public SharedSpace[] Spaces => store.Spaces;
    public SharedSpace Add(string folder, string? name = null)
    {
        string root = Path.GetFullPath(folder);
        CheckFolder(root);
        if (Spaces.Any(space => space.Folder.Equals(root, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("This folder is already a Shared Space.");
        string label = MetadataStore.ValidateName(name ?? new DirectoryInfo(root).Name[..Math.Min(64, new DirectoryInfo(root).Name.Length)]);
        SharedSpace created = new(Guid.NewGuid().ToString("N"), label, root);
        try
        {
            store.SetSpaces([.. Spaces, created]);
        }
        finally { Changed?.Invoke(created.Id); }
        return created;
    }
    public void Update(string id, SpaceAccess access, bool enabled)
    {
        SharedSpace selected = Spaces.FirstOrDefault(space => space.Id == id) ?? throw new IOException("This space is no longer shared.");
        try
        {
            store.SetSpaces(Spaces.Select(space => space.Id == id ? selected with { Access = access, Enabled = enabled } : space));
        }
        finally { Changed?.Invoke(id); }
    }
    /// <summary>Null or empty: every paired PC may open the space. Otherwise only the listed device keys.</summary>
    public void SetAllowed(string id, string[]? peers)
    {
        SharedSpace selected = Spaces.FirstOrDefault(space => space.Id == id) ?? throw new IOException("This space is no longer shared.");
        try
        {
            store.SetSpaces(Spaces.Select(space => space.Id == id ? selected with { AllowedPeers = peers is { Length: > 0 } ? peers.Distinct().ToArray() : null } : space));
        }
        finally { Changed?.Invoke(id); }
    }
    public void Remove(string id)
    {
        try
        {
            store.SetSpaces(Spaces.Where(space => space.Id != id));
        }
        finally { Changed?.Invoke(id); }
    }
    public SharedSpace Get(string id, bool upload = false)
    {
        SharedSpace? selected = Spaces.FirstOrDefault(space => space.Id == id && space.Enabled);
        if (selected == null)
            throw new SpacePermissionException("This space is no longer shared.");
        if (upload && selected.Access != SpaceAccess.ReadAndUpload)
            throw new SpacePermissionException("This space is read only. The host must enable uploads.");
        CheckFolder(selected.Folder);
        return selected;
    }
    public void CheckAccess(string id, string expectedRoot, bool upload)
    {
        if (!Spaces.Any(space => space.Id == id && space.Enabled && space.Folder.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase) && (!upload || space.Access == SpaceAccess.ReadAndUpload)))
            throw new OperationCanceledException("Access to this shared space was revoked.");
    }
    public static bool Available(SharedSpace space)
    {
        try
        {
            CheckFolder(space.Folder);
            return true;
        }
        catch (IOException) { return false; }
    }
    private static void CheckFolder(string folder)
    {
        try
        {
            FileCatalog.CheckSafeDirectory(folder);
            if (!Directory.Exists(folder))
                throw new IOException();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { throw new IOException("This shared folder is unavailable. Check its drive and permissions on the host PC."); }
    }
    public string Resolve(string id, string relative, bool directory, bool upload = false)
    {
        SharedSpace space = Get(id, upload);
        string target = relative.Length == 0 ? space.Folder : FileCatalog.Within(space.Folder, relative);
        try
        {
            FileCatalog.CheckSafePath(target);
        }
        catch (IOException) { throw new IOException("Links and cloud placeholders cannot be accessed through a shared space."); }
        if (directory ? !Directory.Exists(target) : !File.Exists(target))
            throw new IOException("This file or folder is no longer available.");
        return target;
    }
    public SpaceEntry[] List(string id, string relative)
    {
        string folder = Resolve(id, relative, true);
        List<SpaceEntry> entries = [];
        foreach (string child in Directory.EnumerateFileSystemEntries(folder))
        {
            if (entries.Count >= 1000)
                throw new IOException("This folder has more than 1,000 entries. Choose a smaller folder as a space.");
            FileAttributes attributes = File.GetAttributes(child);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            string name = Path.GetFileName(child);
            try
            {
                FileCatalog.ValidateSegment(name);
            }
            catch (InvalidDataException) { continue; }
            bool isFolder = (attributes & FileAttributes.Directory) != 0;
            entries.Add(new(name, relative.Length == 0 ? name : relative + "/" + name, isFolder, isFolder ? 0 : new FileInfo(child).Length));
        }
        return entries.OrderByDescending(entry => entry.Directory).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
