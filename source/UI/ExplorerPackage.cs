using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Win32;

namespace Lantoss.UI;

/// <summary>Registers only Lantoss's signed sparse identity. No global Explorer settings are changed.</summary>
internal static class ExplorerPackage
{
    public static bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    private static string AppDirectory => Path.GetDirectoryName(DesktopRegistration.Executable(Environment.ProcessPath ?? ""))!;
    private static X509Certificate2 Publisher()
    {
        using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lantoss.Explorer.Publisher")!;
        using MemoryStream bytes = new();
        source.CopyTo(bytes);
        return X509CertificateLoader.LoadCertificate(bytes.ToArray());
    }
    private static bool Trusted(X509Certificate2 certificate)
    {
        using X509Store people = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        people.Open(OpenFlags.ReadOnly);
        return people.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count > 0;
    }
    public static void TrustPublisher()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Windows administrator approval is required.");
        // Only this build's embedded public certificate is accepted. No path or arbitrary certificate is supplied.
        using X509Certificate2 certificate = Publisher();
        using X509Store people = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        people.Open(OpenFlags.ReadWrite);
        people.Add(certificate);
    }
    private static X509Certificate2Collection DevelopmentTrust(X509Store people) => people.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, "CN=Lantoss Development", false);
    public static bool PublisherTrusted
    {
        get
        {
            using X509Store people = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
            people.Open(OpenFlags.ReadOnly);
            return DevelopmentTrust(people).Count > 0;
        }
    }
    public static void UntrustPublisher()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Windows administrator approval is required.");
        // Removes only certificates whose exact subject is CN=Lantoss Development. Other trusted people are left alone.
        using X509Store people = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        people.Open(OpenFlags.ReadWrite);
        X509Certificate2Collection found = DevelopmentTrust(people);
        try
        {
            people.RemoveRange(found);
        }
        finally { foreach (X509Certificate2 certificate in found) certificate.Dispose(); }
    }
    /// <summary>Removes this PC's machine-wide trust in the Lantoss development publisher after Windows administrator approval.</summary>
    public static void RemovePublisherTrust()
    {
        if (!PublisherTrusted)
            return;
        ProcessStartInfo untrust = new(Environment.ProcessPath!, "--untrust-explorer-publisher")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using Process process = Process.Start(untrust) ?? throw new IOException("Windows could not open publisher setup.");
        process.WaitForExit();
        if (process.ExitCode != 0 || PublisherTrusted)
            throw new IOException("Publisher trust was not removed. Windows administrator approval is required.");
    }
    public static void Install(bool prompt = true)
    {
        if (!Supported)
            return;
        using X509Certificate2 certificate = Publisher();
        if (!Trusted(certificate))
        {
            if (!prompt || MessageBox.Show("The Windows 11 sharing menu needs hopo's signed identity package.\n\nThis development build uses the publisher “Lantoss Development”. Allow Windows to trust this publisher for app packages on this PC? Windows will ask for administrator approval.\n\nCertificate: " + certificate.Thumbprint + "\n\nChoose No to cancel this feature. You can install without the Explorer feature.",
                "hopo · Windows 11 sharing menu", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                throw new OperationCanceledException("The Windows 11 menu was not installed. Install again with Explorer sharing unchecked to use the app without it.");
            ProcessStartInfo trust = new(Environment.ProcessPath!, "--trust-explorer-publisher")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using Process process = Process.Start(trust) ?? throw new IOException("Windows could not open publisher setup.");
            process.WaitForExit();
            if (process.ExitCode != 0 || !Trusted(certificate))
                throw new IOException("Publisher setup was not approved. The Windows 11 menu has not been registered.");
        }
        Run("Install");
        using RegistryKey settings = Registry.CurrentUser.CreateSubKey(DesktopRegistration.SettingsKey);
        settings.SetValue("ModernLocation", AppDirectory);
        settings.SetValue("ModernVersion", "1.9.0");
    }
    public static bool InstalledHere
    {
        get
        {
            using RegistryKey? settings = Registry.CurrentUser.OpenSubKey(DesktopRegistration.SettingsKey);
            return settings?.GetValue("ModernVersion") as string == "1.9.0" && string.Equals(settings.GetValue("ModernLocation") as string, AppDirectory, StringComparison.OrdinalIgnoreCase);
        }
    }
    public static void Remove()
    {
        if (!Supported)
            return;
        Run("Remove");
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey(DesktopRegistration.SettingsKey, true);
        settings?.DeleteValue("ModernLocation", false);
        settings?.DeleteValue("ModernVersion", false);
        // Keep publisher trust: another installed Lantoss version may still use it. Never remove unrelated trust.
    }
    private static void Run(string mode)
    {
        string script = Path.Combine(AppDirectory, "RegisterPackage.ps1");
        if (!File.Exists(script))
        {
            if (mode == "Remove")
                return;
            throw new FileNotFoundException("Install hopo with its MSI to enable the Windows 11 menu.");
        }
        ProcessStartInfo start = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Mode", mode, "-AppDirectory", AppDirectory })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new IOException("Windows package setup could not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(true);
            throw new TimeoutException("Windows package setup timed out.");
        }
        if (process.ExitCode != 0)
            throw new IOException("Windows could not " + (mode == "Install" ? "register" : "remove") + " the hopo sharing menu.\n" + error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
    }
}
