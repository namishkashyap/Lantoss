param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
# The PFX password is read only from the environment so it never appears on a command line or in shell history.
$Password = $env:LANTOSS_PFX_PASSWORD
if (-not $Password) { throw 'Set the LANTOSS_PFX_PASSWORD environment variable to the password that will protect the new PFX, then run this script again.' }
$path = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $path) { throw 'Keep the existing signing key; choose a new output path to create a different key.' }
New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
$rsa = [Security.Cryptography.RSA]::Create(3072)
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Lantoss Development',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
    $usages = [Security.Cryptography.OidCollection]::new()
    [void]$usages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages,$true))
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1),[DateTimeOffset]::UtcNow.AddYears(3))
    try { [IO.File]::WriteAllBytes($path,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$Password)) } finally { $certificate.Dispose() }
} finally { $rsa.Dispose() }
Write-Output 'Development key created. No certificate was added to any Windows trust store. Keep the PFX private and outside source/release archives.'
