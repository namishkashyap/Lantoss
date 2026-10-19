using System.Security.Cryptography;
using System.Text;

namespace Lantoss.Core;

// Generates only application-scoped Private/LAN rules. No profile, firewall,
// router, outbound, or other application's policy is changed.
/// <summary>
/// Builds and verifies exact-program, Private-profile, local-subnet firewall rules. This class only prepares scripts; UI/PrivateFirewallSetup owns the UAC boundary.
/// </summary>
public sealed class PrivateFirewallPlan
{
    public string Program
    {
        get;
    }
    public int[] TcpPorts
    {
        get;
    }
    public string RulePrefix
    {
        get;
    }
    public string Signature
    {
        get;
    }
    public PrivateFirewallPlan(string program, IEnumerable<int> tcpPorts)
    {
        if (!Path.IsPathFullyQualified(program) || !Path.GetFileName(program).Equals("Lantoss.exe", StringComparison.OrdinalIgnoreCase) || program.Any(char.IsControl))
            throw new InvalidDataException("Choose the published Lantoss executable.");
        Program = Path.GetFullPath(program);
        TcpPorts = tcpPorts.Distinct().Order().ToArray();
        if (TcpPorts.Length is < 1 or > 2 || TcpPorts.Any(port => port is < 1 or > 65535))
            throw new InvalidDataException("Only the current native and browser TCP ports may be configured.");
        RulePrefix = "Lantoss-Private-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Program.ToUpperInvariant())))[..16];
        Signature = RulePrefix + ":v1:" + string.Join(',', TcpPorts);
    }
    private string Header => "$ErrorActionPreference='Stop';\n$flowProgram='" + Program.Replace("'", "''") + "';\n$flowPrefix='" + RulePrefix + "';\n$flowTcp=@(" + string.Join(',', TcpPorts.Select(port => "'" + port + "'")) + ");\n";
    private const string Functions = """
        function Get-FlowBlocks([string]$store) {
          @(Get-NetFirewallApplicationFilter -PolicyStore $store | Where-Object { $_.Program -eq $flowProgram } | Get-NetFirewallRule | Where-Object {
            [string]$_.Enabled -eq 'True' -and [string]$_.Direction -eq 'Inbound' -and [string]$_.Action -eq 'Block' -and ([string]$_.Profile -match '(^|,\s*)(Any|Private)(\s*,|$)')
          })
        }
        function Test-FlowRules {
          foreach($protocol in @('TCP','UDP')) {
            $r=@(Get-NetFirewallRule -PolicyStore ActiveStore -Name ($flowPrefix+'-'+$protocol) -ErrorAction SilentlyContinue)
            if($r.Count -ne 1){return $false}; $r=$r[0]
            if([string]$r.Enabled -ne 'True' -or [string]$r.Profile -ne 'Private' -or [string]$r.Direction -ne 'Inbound' -or [string]$r.Action -ne 'Allow' -or [string]$r.EdgeTraversalPolicy -ne 'Block'){return $false}
            $a=@($r | Get-NetFirewallApplicationFilter); $p=@($r | Get-NetFirewallPortFilter); $s=@($r | Get-NetFirewallAddressFilter)
            if($a.Count -ne 1 -or $a[0].Program -ne $flowProgram -or $p.Count -ne 1 -or $s.Count -ne 1){return $false}
            if([string]$p[0].Protocol -notin @($protocol, $(if($protocol -eq 'TCP'){'6'}else{'17'}))){return $false}
            $wanted=if($protocol -eq 'TCP'){$flowTcp}else{@('5353','48587')}
            if(@(Compare-Object @($wanted | Sort-Object) @($p[0].LocalPort | ForEach-Object {[string]$_} | Sort-Object)).Count -ne 0){return $false}
            if(@($s[0].RemoteAddress).Count -ne 1 -or $s[0].RemoteAddress[0] -ne 'LocalSubnet4'){return $false}
          }
          return $true
        }
        """;
    public string InspectionScript => Header + Functions + "\nif((Test-FlowRules) -and @(Get-FlowBlocks 'ActiveStore').Count -eq 0){'READY'}else{'NEEDS_SETUP'}\n";
    public string SetupScript => Header + Functions + "\n" + """
        foreach($protocol in @('TCP','UDP')) {
          $ports=if($protocol -eq 'TCP'){$flowTcp}else{@('5353','48587')}
          $flowRuleArgs=@{Name=($flowPrefix+'-'+$protocol);DisplayName=('Lantoss Private LAN '+$protocol);PolicyStore='PersistentStore';Program=$flowProgram;Protocol=$protocol;LocalPort=$ports;RemoteAddress='LocalSubnet4';Profile='Private';Direction='Inbound';Action='Allow';Enabled='True';EdgeTraversalPolicy='Block'}
          if(Get-NetFirewallRule -PolicyStore PersistentStore -Name $flowRuleArgs.Name -ErrorAction SilentlyContinue){Set-NetFirewallRule @flowRuleArgs | Out-Null}else{New-NetFirewallRule @flowRuleArgs | Out-Null}
        }
        # A previous Windows "Don't allow" may have blocked this exact executable.
        # Remove only its Private scope; retain its Public/Domain blocking.
        foreach($block in @(Get-FlowBlocks 'PersistentStore')) {
          $profiles=@(([string]$block.Profile).Split(',') | ForEach-Object {$_.Trim()})
          $keep=if($profiles -contains 'Any'){@('Domain','Public')}else{@($profiles | Where-Object {$_ -ne 'Private'})}
          if(@($keep).Count -eq 0){Set-NetFirewallRule -PolicyStore PersistentStore -Name $block.Name -Enabled False | Out-Null}
          else{Set-NetFirewallRule -PolicyStore PersistentStore -Name $block.Name -Profile $keep | Out-Null}
        }
        if(-not (Test-FlowRules)){throw 'Windows did not activate the Lantoss Private-network rules. Check administrator or organization policy.'}
        if(@(Get-FlowBlocks 'ActiveStore').Count -gt 0){throw 'A managed rule still blocks Lantoss. The local Private rules are configured; contact the administrator for that policy.'}
        'READY'
        """;
}
