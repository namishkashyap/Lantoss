using System.Text;

namespace Lantoss.Core;

/// <summary>Consumes the bounded, one-use selection written by the native Explorer extension.</summary>
public static class ShellSelection
{
    public static string RequestRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lantoss", "ShellRequests");

    public static LaunchRequest Read(string path, string? requestRoot = null)
    {
        string root = Path.GetFullPath(requestRoot ?? RequestRoot);
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase)
            || !Path.GetExtension(full).Equals(".req", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(full), "D", out _))
            throw new InvalidDataException("Invalid Explorer selection location.");
        // No traversal into junctions/symlinks; never delete an arbitrary caller-supplied path.
        for (string? parent = full; parent != null; parent = Path.GetDirectoryName(parent))
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Explorer selection must be a regular local file.");
        using FileStream file = new(full, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose);
        if (file.Length < 16 || file.Length > DesktopInstance.MaxMessageBytes)
            throw new InvalidDataException("Explorer selection is too large or incomplete.");
        using BinaryReader reader = new(file, new UnicodeEncoding(false, false, true));
        if (!reader.ReadBytes(8).SequenceEqual("LTSSREQ1"u8.ToArray()))
            throw new InvalidDataException("Unknown Explorer selection format.");
        string key = Text(128);
        int count = reader.ReadInt32();
        if (count < 1 || count > 256)
            throw new InvalidDataException("Choose between 1 and 256 items.");
        string[] paths = new string[count];
        for (int index = 0; index < count; index++)
            paths[index] = Text(65520);
        if (file.Position != file.Length)
            throw new InvalidDataException("Unexpected Explorer selection data.");
        return new LaunchRequest("share", paths, key.Length == 0 ? null : key).Checked();

        string Text(int maximum)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximum || length % 2 != 0 || length > file.Length - file.Position)
                throw new InvalidDataException("Invalid Explorer selection text.");
            return new UnicodeEncoding(false, false, true).GetString(reader.ReadBytes(length));
        }
    }
}
