using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

// Read-only inspection runs unelevated. Only the fixed setup script crosses Windows runas/UAC; a cancelled attempt is deferred until the user explicitly retries.
namespace Lantoss.UI;

internal sealed class PrivateFirewallSetup(AppShell app)
{
    private sealed record SetupState(string Signature, bool Confirmed);
    private readonly SemaphoreSlim gate = new(1, 1);
    private string checkedSignature = "";
    public string Status { get; private set; } = "Private-network firewall setup has not run yet.";
    private string StateFile => Path.Combine(app.Store.Root, "firewall-setup-state.json");
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    private static ProcessStartInfo Command(string script, bool elevate)
    {
        ProcessStartInfo start = new(PowerShell)
        {
            UseShellExecute = elevate,
            CreateNoWindow = !elevate,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        if (encoded.Length > 28000)
            throw new IOException("The firewall setup command exceeds Windows' command length limit.");
        if (elevate)
        {
            start.Verb = "runas";
            start.Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encoded;
        }
        else
        {
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded })
                start.ArgumentList.Add(argument);
        }
        return start;
    }
    private static async Task<bool?> InspectAsync(PrivateFirewallPlan plan)
    {
        using Process check = Process.Start(Command(plan.InspectionScript, false)) ?? throw new IOException("Windows could not check Lantoss's rules.");
        Task<string> output = check.StandardOutput.ReadToEndAsync();
        Task<string> errors = check.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        try
        {
            await check.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { try { check.Kill(); } catch (InvalidOperationException) { } return null; }
        string text = await output;
        await errors;
        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return check.ExitCode != 0 ? null : lines.Contains("READY") ? true : lines.Contains("NEEDS_SETUP") ? false : null;
    }
    public async Task EnsureAsync(bool retry = false)
    {
        if (app.Preview || !app.Engine.Running || (!retry && !app.Store.Preferences.AutoConfigureFirewall) || !await gate.WaitAsync(0))
            return;
        PrivateFirewallPlan? plan = null;
        try
        {
            string executable = Environment.ProcessPath ?? throw new IOException("The running executable is unavailable.");
            FileCatalog.CheckSafePath(executable);
            List<int> ports = [app.Engine.Port];
            if (app.Engine.Browser.Running)
                ports.Add(new Uri(app.Engine.Browser.Url).Port);
            plan = new(executable, ports);
            if (!retry && checkedSignature == plan.Signature)
                return;
            bool? ready = await InspectAsync(plan);
            SetupState? saved = null;
            try
            {
                saved = File.Exists(StateFile) ? JsonSerializer.Deserialize<SetupState>(File.ReadAllText(StateFile)) : null;
            }
            catch (Exception error) when (error is IOException or JsonException) { }
            if (ready == true)
            {
                checkedSignature = plan.Signature;
                Status = "Lantoss firewall rules are configured for Private networks and the local subnet.";
                SaveState(plan, true);
                return;
            }
            if (!retry && ready == null && saved is { Confirmed: true } && saved.Signature == plan.Signature)
            {
                checkedSignature = plan.Signature;
                Status = "Private-network setup completed previously. Windows did not allow a fresh rule check.";
                return;
            }
            if (!retry && saved is { Confirmed: false } && saved.Signature == plan.Signature)
            {
                Status = "Firewall setup needs Windows administrator approval. Use Set up Private-network access in Connection diagnostics.";
                return;
            }
            Status = "Waiting for Windows administrator approval for Lantoss's Private-network rules…";
            app.Tell(Status);
            using Process helper = Process.Start(Command(plan.SetupScript, true)) ?? throw new IOException("Windows could not start the firewall setup helper.");
            await helper.WaitForExitAsync();
            if (helper.ExitCode != 0)
                throw new IOException("Windows could not confirm the rules. Retry from Connection diagnostics; organization policy may prevent changes.");
            checkedSignature = plan.Signature;
            SaveState(plan, true);
            Status = "Private-network access configured. Other PCs can discover this app when the network is marked Private.";
            app.Engine.RefreshDiscovery();
            app.Tell(Status);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Status = error is Win32Exception { NativeErrorCode: 1223 } ? "Windows administrator approval was cancelled. Retry setup from Connection diagnostics when ready." : "Firewall setup needs attention. " + error.Message;
            if (plan != null)
            {
                try
                {
                    SaveState(plan, false);
                }
                catch (Exception save) when (save is IOException or UnauthorizedAccessException) { }
            }
            app.Tell(Status);
        }
        finally { gate.Release(); }
    }
    private void SaveState(PrivateFirewallPlan plan, bool confirmed) => new MetadataStore.AtomicDiskWriter().Write(StateFile, JsonSerializer.SerializeToUtf8Bytes(new SetupState(plan.Signature, confirmed)));
}
