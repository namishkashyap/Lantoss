using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lantoss.Core;

internal static partial class Program
{
    private static async Task FirewallCasesAsync()
    {
        Peer item = new()
        {
            Name = "NK",
            Fingerprint = new string('A', 64),
            Address = "192.168.1.181",
            Port = 48585,
            Paired = true
        };
        Check(item.ToString() == "NK", "A device's display fallback is its name, never the internal peer record");
        MetadataStore settings = Store("Firewall preference");
        Check(settings.Preferences.AutoConfigureFirewall, "Automatic Private-network firewall setup is enabled by default");
        settings.SetPreferences(settings.Preferences with
        {
            AutoConfigureFirewall = false
        });
        Check(!new MetadataStore(settings.Root).Preferences.AutoConfigureFirewall, "The automatic-firewall opt-out survives reopening preferences");
        string executable = Path.Combine(root, "O'Brien ` $ folder", "Lantoss.exe");
        PrivateFirewallPlan plan = new(executable, [48586, 48585, 48585]);
        Check(plan.TcpPorts.SequenceEqual([48585, 48586]), "Firewall plans include only the actual deduplicated native/browser TCP ports");
        bool Reject(string path, int[] ports)
        {
            try
            {
                _ = new PrivateFirewallPlan(path, ports);
                return false;
            }
            catch (InvalidDataException) { return true; }
        }
        Check(Reject("Lantoss.exe", [48585]) && Reject(Path.Combine(root, "other.exe"), [48585]) && Reject(executable, [0]) && Reject(executable, [1, 2, 3]) && Reject(executable, [65536]), "Firewall plans reject another program, a relative executable, invalid ports, and broad port sets");
        PrivateFirewallPlan changed = new(executable, [48585, 50123]);
        Check(plan.RulePrefix == changed.RulePrefix && plan.Signature != changed.Signature, "Changed listening ports update the same app-scoped rules and invalidate the setup signature");
        string mocks = """
            $global:flowRules=@{}; $global:flowNew=0
            function Get-NetFirewallRule {
              [CmdletBinding()]param($PolicyStore,$Name,[Parameter(ValueFromPipeline)]$Filter)
              process{if($Name){if($global:flowRules.ContainsKey($Name)){$global:flowRules[$Name]}}elseif($Filter.RuleKey){$global:flowRules[$Filter.RuleKey]}}
            }
            function Get-NetFirewallApplicationFilter {
              [CmdletBinding()]param($PolicyStore,[Parameter(ValueFromPipeline)]$Rule)
              process{if($Rule){[pscustomobject]@{Program=$Rule.Program}}else{foreach($key in @($global:flowRules.Keys)){if($PolicyStore -eq 'PersistentStore' -and $global:flowRules[$key].Managed){continue};[pscustomobject]@{Program=$global:flowRules[$key].Program;RuleKey=$key}}}}
            }
            function Get-NetFirewallPortFilter {param([Parameter(ValueFromPipeline)]$Rule)process{[pscustomobject]@{Protocol=$Rule.Protocol;LocalPort=@($Rule.LocalPort)}}}
            function Get-NetFirewallAddressFilter {param([Parameter(ValueFromPipeline)]$Rule)process{[pscustomobject]@{RemoteAddress=@($Rule.RemoteAddress)}}}
            function New-NetFirewallRule {
              [CmdletBinding()]param($Name,$DisplayName,$PolicyStore,$Program,$Protocol,[string[]]$LocalPort,[string[]]$RemoteAddress,[string[]]$Profile,$Direction,$Action,$Enabled,$EdgeTraversalPolicy)
              if($Program -ne $expectedProgram -or $PolicyStore -ne 'PersistentStore' -or ($Profile -join ',') -ne 'Private' -or ($RemoteAddress -join ',') -ne 'LocalSubnet4' -or $LocalPort -contains 'Any'){throw 'Scope expanded'}
              $global:flowRules[$Name]=[pscustomobject]@{Name=$Name;Program=$Program;Protocol=$Protocol;LocalPort=$LocalPort;RemoteAddress=$RemoteAddress;Profile=($Profile -join ',');Direction=$Direction;Action=$Action;Enabled=$Enabled;EdgeTraversalPolicy=$EdgeTraversalPolicy;Managed=$false};$global:flowNew++
            }
            function Set-NetFirewallRule {
              [CmdletBinding()]param($Name,$DisplayName,$PolicyStore,$Program,$Protocol,[string[]]$LocalPort,[string[]]$RemoteAddress,[string[]]$Profile,$Direction,$Action,$Enabled,$EdgeTraversalPolicy)
              $r=$global:flowRules[$Name];if($r.Managed -or $r.Program -ne $expectedProgram){throw 'Unrelated policy touched'}
              foreach($key in $PSBoundParameters.Keys){if($key -eq 'PolicyStore' -or $key -eq 'DisplayName'){continue};$r.$key=if($key -eq 'Profile'){$PSBoundParameters[$key] -join ','}else{$PSBoundParameters[$key]}}
            }
            function Add-Block($name,$program,$profile,$managed=$false){$global:flowRules[$name]=[pscustomobject]@{Name=$name;Program=$program;Profile=$profile;Enabled='True';Direction='Inbound';Action='Block';Managed=$managed}}
            """;
        string mockProgram = "$expectedProgram='" + executable.Replace("'", "''") + "'\n";
        string setup = "$flowSetup={\n" + plan.SetupScript + "\n}\n";
        string assertions = """
            Add-Block old-private $expectedProgram Private
            Add-Block old-any $expectedProgram Any
            Add-Block old-public $expectedProgram Public
            Add-Block unrelated 'C:\Other\other.exe' Any
            & $flowSetup; & $flowSetup
            $idempotent=$global:flowNew -eq 2
            $tcp=$global:flowRules.Values | Where-Object Action -eq Allow | Where-Object Protocol -eq TCP
            $udp=$global:flowRules.Values | Where-Object Action -eq Allow | Where-Object Protocol -eq UDP
            $scope=($tcp.Program -eq $expectedProgram -and $tcp.Profile -eq 'Private' -and ($tcp.RemoteAddress -join ',') -eq 'LocalSubnet4' -and ($tcp.LocalPort -join ',') -eq '48585,48586' -and ($udp.LocalPort -join ',') -eq '5353,48587' -and $udp.EdgeTraversalPolicy -eq 'Block')
            $preserved=($global:flowRules['old-private'].Enabled -eq 'False' -and $global:flowRules['old-any'].Profile -eq 'Domain,Public' -and $global:flowRules['old-public'].Profile -eq 'Public' -and $global:flowRules['unrelated'].Profile -eq 'Any')
            Add-Block managed $expectedProgram Any $true
            $managedRejected=$false;try{& $flowSetup}catch{$managedRejected=$true}
            @{idempotent=$idempotent;scope=$scope;preserved=$preserved;managedRejected=$managedRejected;managedUntouched=($global:flowRules['managed'].Profile -eq 'Any')} | ConvertTo-Json -Compress
            """;
        string script = mocks + "\n" + mockProgram + setup + assertions;
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        if (encoded.Length > 30000)
            throw new IOException("Mock firewall command is too long.");
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo start = new(shell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        string result = await output, errorText = await errors;
        if (process.ExitCode != 0)
            throw new IOException("Mock firewall checks failed: " + errorText);
        using JsonDocument json = JsonDocument.Parse(result.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Last(line => line.StartsWith('{')));
        Check(json.RootElement.GetProperty("scope").GetBoolean(), "Mocked setup creates only exact-program Private/local-subnet rules on the intended ports, including a quoted executable path");
        Check(json.RootElement.GetProperty("idempotent").GetBoolean(), "Repeating firewall setup updates the two existing app rules without duplicating them");
        Check(json.RootElement.GetProperty("preserved").GetBoolean(), "Repairing a previous app denial preserves Public/Domain blocks and unrelated applications' rules");
        Check(json.RootElement.GetProperty("managedRejected").GetBoolean() && json.RootElement.GetProperty("managedUntouched").GetBoolean(), "A managed blocking rule remains untouched and prevents a false success result");
    }
}
