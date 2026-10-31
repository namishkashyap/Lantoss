using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Lantoss.UI;

/// <summary>Owns only Lantoss's current-user shell/startup entries. Preview mode never calls this class.</summary>
internal sealed class WindowsIntegration
{
    private readonly string executable = DesktopRegistration.Executable(Environment.ProcessPath ?? "");
    private string signature = "\0";
    public static void RemoveUserIntegration()
    {
        ExplorerPackage.Remove();
        // Installer cleanup must not open metadata, create an identity or start a server.
        using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(DesktopRegistration.RunKey, true))
            run?.DeleteValue("Lantoss", false);
        foreach (string key in DesktopRegistration.MenuKeys)
            Registry.CurrentUser.DeleteSubKeyTree(key, false);
        Registry.CurrentUser.DeleteSubKeyTree(DesktopRegistration.SettingsKey, false);
        NotifyExplorer();
    }
    public bool LaunchAtSignIn
    {
        get
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(DesktopRegistration.RunKey);
            return string.Equals(run?.GetValue("Lantoss") as string, DesktopRegistration.StartupCommand(executable), StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(DesktopRegistration.RunKey);
            if (value)
                run.SetValue("Lantoss", DesktopRegistration.StartupCommand(executable));
            else
                run.DeleteValue("Lantoss", false);
        }
    }
    public bool ShellEnabled
    {
        get
        {
            using RegistryKey? settings = Registry.CurrentUser.OpenSubKey(DesktopRegistration.SettingsKey);
            return settings?.GetValue("ExplorerEnabled") is int enabled && enabled == 1;
        }
    }
    public void SetShellEnabled(bool enabled, Peer[] peers)
    {
        if (ExplorerPackage.Supported)
        {
            if (enabled && !ExplorerPackage.InstalledHere)
                ExplorerPackage.Install();
            else if (!enabled && ExplorerPackage.InstalledHere)
                ExplorerPackage.Remove();
        }
        using (RegistryKey settings = Registry.CurrentUser.CreateSubKey(DesktopRegistration.SettingsKey))
            settings.SetValue("ExplorerEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
        signature = "\0";
        if (enabled)
            Refresh(peers);
        else
        {
            foreach (string key in DesktopRegistration.MenuKeys)
                Registry.CurrentUser.DeleteSubKeyTree(key, false);
            NotifyExplorer();
        }
    }
    public void Refresh(Peer[] peers)
    {
        if (!ShellEnabled)
            return;
        if (ExplorerPackage.Supported)
        {
            // The native command reads a tiny cached list; never touch the metadata store from Explorer.
            Peer[] targets = peers.Where(peer => peer.Paired && !peer.Blocked && peer.Fingerprint.Length == 64 && peer.Fingerprint.All(Uri.IsHexDigit))
                .DistinctBy(peer => peer.Fingerprint, StringComparer.OrdinalIgnoreCase).OrderBy(peer => peer.Name, StringComparer.OrdinalIgnoreCase).Take(32).ToArray();
            string nextTargets = string.Join('\n', targets.Select(peer => peer.Fingerprint + "|" + peer.Name));
            if (signature == nextTargets)
                return;
            foreach (string root in DesktopRegistration.MenuKeys)
                Registry.CurrentUser.DeleteSubKeyTree(root, false);
            Registry.CurrentUser.DeleteSubKeyTree(DesktopRegistration.SettingsKey + @"\Peers", false);
            foreach (Peer peer in targets)
            {
                using RegistryKey target = Registry.CurrentUser.CreateSubKey(DesktopRegistration.SettingsKey + @"\Peers\" + peer.Fingerprint.ToUpperInvariant());
                target.SetValue("Name", new string(peer.Name.Where(c => !char.IsControl(c)).Take(64).ToArray()));
            }
            signature = nextTargets;
            NotifyExplorer();
            return;
        }
        RegistryText[] plan = DesktopRegistration.Menu(executable, peers);
        string next = string.Join('\n', plan.Select(item => item.Key + "|" + item.Name + "|" + item.Value));
        if (next == signature)
            return;
        // Remove stale PC verbs after Unpair/Block, then rewrite the small owned subtree.
        // The fallback Choose a PC item is always present, even before first pairing.
        foreach (string root in DesktopRegistration.MenuKeys)
            Registry.CurrentUser.DeleteSubKeyTree(root, false);
        foreach (RegistryText item in plan)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(item.Key);
            key.SetValue(item.Name, item.Value, RegistryValueKind.String);
        }
        signature = next;
        NotifyExplorer();
    }
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    /// <summary>
    /// Gives the window its own taskbar identity: app ID, relaunch command, the name "hopo" and the exe's icon. The Explorer
    /// package (1.9.0.0) would otherwise lend the process its old Lantoss logo, so taskbar buttons and new pins use these instead.
    /// </summary>
    public static void Taskbar(IntPtr window, string executable)
    {
        Guid store = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        if (SHGetPropertyStoreForWindow(window, ref store, out IPropertyStore properties) != 0)
            return;
        try
        {
            // Relaunch details must be set with the ID: 2 command, 3 icon, 4 display name, 5 app ID.
            foreach ((uint id, string value) in new[] { (2u, "\"" + executable + "\""), (3u, executable + ",0"), (4u, "hopo"), (5u, "hopo.LocalFileSharing") })
            {
                PropertyKey key = new()
                {
                    Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                    Id = id
                };
                PropVariant text = new()
                {
                    Type = 31,
                    Value = Marshal.StringToCoTaskMemUni(value)
                };
                try
                {
                    properties.SetValue(ref key, ref text);
                }
                finally { Marshal.FreeCoTaskMem(text.Value); }
            }
            properties.Commit();
        }
        finally { Marshal.ReleaseComObject(properties); }
    }
    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid interfaceId, out IPropertyStore store);
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }
    // PROPVARIANT is 24 bytes on x64; only the type and the string pointer are used.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Value;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
