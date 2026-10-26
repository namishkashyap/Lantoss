param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath($ReleaseDirectory)
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $root 'source/Explorer/Publisher.cer'))
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root 'app/Lantoss.Explorer.msix'))
function EntryBytes([string]$name) {
    $entry = $zip.GetEntry($name)
    if (-not $entry) { throw ('Missing sparse-package entry: '+$name) }
    $stream = $entry.Open(); $buffer = [IO.MemoryStream]::new()
    try { $stream.CopyTo($buffer); return ,$buffer.ToArray() } finally { $stream.Dispose(); $buffer.Dispose() }
}
try {
    $signature = EntryBytes 'AppxSignature.p7x'
    if ([Text.Encoding]::ASCII.GetString($signature,0,4) -ne 'PKCX') { throw 'Invalid AppX signature header.' }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]$signature[4..($signature.Length-1)])
    $cms.CheckSignature($true) # Cryptographic check only; do not alter host trust.
    if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Sparse package signer differs from embedded publisher.' }
    [xml]$manifest = [Text.Encoding]::UTF8.GetString((EntryBytes 'AppxManifest.xml'))
    if ($manifest.Package.Identity.Name -ne 'Lantoss.ExplorerIntegration' -or $manifest.Package.Identity.Version -ne '1.9.0.0' -or $manifest.Package.Identity.ProcessorArchitecture -ne 'x64') { throw 'Sparse identity mismatch.' }
    $namespaces = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaces.AddNamespace('com','http://schemas.microsoft.com/appx/manifest/com/windows10')
    $namespaces.AddNamespace('d5','http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')
    $class = $manifest.SelectSingleNode('//com:Class',$namespaces)
    $verbs = $manifest.SelectNodes('//d5:Verb',$namespaces)
    if ($class.Id -ne '18F23026-095D-4FD5-AEF4-7DDF06847DE9' -or $class.Path -ne 'LantossExplorer.dll' -or $verbs.Count -ne 2 -or @($verbs | Where-Object Clsid -ne $class.Id).Count) { throw 'Native COM / file-and-folder menu registration mismatch.' }
    if ($zip.Entries | Where-Object FullName -match '\.(exe|dll)$') { throw 'Sparse identity must not duplicate external binaries.' }
    $dll = Join-Path $root 'app/LantossExplorer.dll'
    if ((Get-Item $dll).VersionInfo.FileVersion -ne '1.9.0.0') { throw 'Native DLL version mismatch.' }
    $dllSignature = Get-AuthenticodeSignature -LiteralPath $dll
    if ($dllSignature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or $dllSignature.Status -eq 'HashMismatch') { throw 'Native DLL signature mismatch.' }
    foreach ($asset in @('Assets/Logo.png','Assets/SmallLogo.png','RegisterPackage.ps1','Lantoss.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root ('app/'+$asset)))) { throw ('Missing external content: '+$asset) }
    }
    [pscustomobject]@{SparseSignatureValid=$true;NativeDllSignerMatches=$true;NativeDllTrustStatus=[string]$dllSignature.Status;PublisherThumbprint=$certificate.Thumbprint;IdentityVersion='1.9.0.0';NativeComClass=$class.Id;FileAndFolderVerbs=$verbs.Count;NoHostTrustChanged=$true} | ConvertTo-Json
} finally { $zip.Dispose(); $certificate.Dispose() }
