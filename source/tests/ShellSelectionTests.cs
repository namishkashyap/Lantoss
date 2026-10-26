using System.Text;
using Lantoss.Core;

internal static partial class Program
{
    private static void ShellSelectionCases()
    {
        string folder = Path.Combine(root, "shell-selection");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(root, "Photo & family 😀.txt");
        string Request(Action<BinaryWriter> content)
        {
            string path = Path.Combine(folder, Guid.NewGuid().ToString("D") + ".req");
            using BinaryWriter writer = new(File.Create(path), Encoding.Unicode);
            content(writer);
            return path;
        }
        void Text(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        void Valid(BinaryWriter writer)
        {
            writer.Write("LTSSREQ1"u8);
            Text(writer, new string('A', 64));
            writer.Write(2);
            Text(writer, file);
            Text(writer, folder);
        }
        string valid = Request(Valid);
        LaunchRequest selection = ShellSelection.Read(valid, folder);
        Check(selection.PeerKey == new string('A', 64) && selection.Paths.SequenceEqual([file, folder]) && !File.Exists(valid), "Native Explorer batch preserves Unicode, folder selection and recipient; consumed request is deleted");
        bool Reject(string path, string allowed)
        {
            try
            {
                ShellSelection.Read(path, allowed);
                return false;
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException) { return true; }
        }
        string malformed = Request(writer => { writer.Write("LTSSREQ1"u8); writer.Write(int.MaxValue); });
        Check(Reject(malformed, folder) && !File.Exists(malformed), "Malformed selection lengths are bounded and consumed");
        string excessive = Request(writer => { writer.Write("LTSSREQ1"u8); Text(writer, ""); writer.Write(257); });
        Check(Reject(excessive, folder), "Native Explorer batches cannot exceed 256 selected items");
        string trailing = Request(writer => { Valid(writer); writer.Write((byte)1); });
        Check(Reject(trailing, folder), "Explorer selection rejects trailing bytes");
        string outside = Request(Valid);
        Check(Reject(outside, Path.Combine(root, "other")) && File.Exists(outside), "An outside request path is rejected without reading or deleting it");
        File.Delete(outside);
        // The native harness emits this fixture using Windows IShellItemArray and the real COM Invoke implementation.
        string native = Environment.GetEnvironmentVariable("LANTOSS_NATIVE_REQUEST") ?? "";
        if (File.Exists(native))
        {
            LaunchRequest actual = ShellSelection.Read(native, Path.GetDirectoryName(native));
            Check(actual.PeerKey == new string('A', 64) && actual.Paths.Length == 2 && Path.GetFileName(actual.Paths[0]) == "Photo & family 😀.txt" && Directory.Exists(actual.Paths[1]), "Actual native COM multiselection is readable by the managed desktop pipeline");
        }
    }
}
