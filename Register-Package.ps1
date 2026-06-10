# Builds a FULL signed MSIX (exe + runtime inside the package).
# When installed, Windows places the exe in C:\Program Files\WindowsApps\<pkg>\.
# Exes in that path automatically receive package identity — no sparse-package registry mapping required.
#
# Launch after registration by searching "Notification Relay" in the Start menu.
#
# Needs to be re-run any time rebuilt.

$ErrorActionPreference = "Stop"
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishDir = Join-Path $ScriptDir "publish"

$SdkBin   = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
$MakeAppx = Join-Path $SdkBin "makeappx.exe"
$SignTool  = Join-Path $SdkBin "signtool.exe"

#1. Publish 
Write-Host "Building and publishing..." -ForegroundColor Cyan
dotnet publish "$ScriptDir\NotificationRelay.csproj" `
    -c Release -o $PublishDir --nologo -v quiet

if (-not (Test-Path "$PublishDir\NotificationRelay.exe")) {
    Write-Error "Publish failed -- NotificationRelay.exe not found"
    exit 1
}

#2. Build MSIX layout (all published files + assets + manifest) 
Write-Host "Building MSIX layout..." -ForegroundColor Cyan
$PkgLayout = Join-Path $env:TEMP "NotificationRelayPkg"
Remove-Item $PkgLayout -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $PkgLayout | Out-Null

# Copy all published binaries into the layout
Get-ChildItem $PublishDir -File | Copy-Item -Destination $PkgLayout -Force

# Create placeholder PNG assets
Add-Type -AssemblyName System.Drawing
$AssetsDir = Join-Path $PkgLayout "Assets"
New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null

function New-PlaceholderPng([string]$path, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(130, 80, 220))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
New-PlaceholderPng (Join-Path $AssetsDir "StoreLogo.png")          50
New-PlaceholderPng (Join-Path $AssetsDir "Square44x44Logo.png")    44
New-PlaceholderPng (Join-Path $AssetsDir "Square150x150Logo.png") 150

# Write AppxManifest.xml — standard Desktop Bridge (exe inside the package)
$manifest = @'
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap5 rescap">

  <Identity Name="com.NotificationRelay.Desktop"
            Publisher="CN=NotificationRelay"
            Version="1.0.0.0"
            ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>Notification Relay</DisplayName>
    <PublisherDisplayName>NotificationRelay</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop"
                        MinVersion="10.0.17763.0"
                        MaxVersionTested="10.0.22621.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-US" />
  </Resources>

  <Applications>
    <Application Id="App"
                 Executable="NotificationRelay.exe"
                 EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="Notification Relay"
        Description="Relays Windows notifications to your phone"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
      <Extensions>
        <uap5:Extension Category="windows.startupTask" Executable="NotificationRelay.exe" EntryPoint="Windows.FullTrustApplication">
          <uap5:StartupTask TaskId="NotificationRelayStartup" Enabled="true" DisplayName="Notification Relay" />
        </uap5:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" /> 
  </Capabilities>

</Package>
'@
# The "runFullTrust" is required just to not block notifications

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $PkgLayout "AppxManifest.xml"), $manifest, $utf8NoBom)

#  3. Certificate
# Its a workaround since i dont have a real ceritificate (that costs money) to sign the MSIX. The self-signed certificate is created if not found, and added to the Trusted Root store (UAC prompt will appear).
Write-Host "Checking certificate..." -ForegroundColor Cyan
$certSubject = "CN=NotificationRelay"
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $certSubject -and $_.NotAfter -gt (Get-Date) } |
    Select-Object -First 1

if (-not $cert) {
    Write-Host "  Creating self-signed certificate..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type Custom -Subject $certSubject `
        -KeyUsage DigitalSignature -FriendlyName "NotificationRelay" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}

$alreadyTrusted = [bool](Get-ChildItem Cert:\LocalMachine\Root |
    Where-Object { $_.Thumbprint -eq $cert.Thumbprint })

if (-not $alreadyTrusted) {
    $cerPath = Join-Path $env:TEMP "NotificationRelay.cer"
    Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
    Write-Host "  UAC prompt will appear -- click Yes (one-time cert trust)." -ForegroundColor Yellow
    $encoded = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes(
            "Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\Root"))
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-ExecutionPolicy Bypass -EncodedCommand $encoded"
    Remove-Item $cerPath -ErrorAction SilentlyContinue
    Write-Host "  Certificate trusted." -ForegroundColor Cyan
} else {
    Write-Host "  Certificate already trusted." -ForegroundColor Cyan
}

$pfxPath     = Join-Path $env:TEMP "NotificationRelay_sign.pfx"
$pfxPassword = ConvertTo-SecureString -String "NR_build_temp!" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword | Out-Null

#  4. Pack and sign 
Write-Host "Packing MSIX..." -ForegroundColor Cyan
$msixPath = Join-Path $env:TEMP "NotificationRelay.msix"
& $MakeAppx pack /d $PkgLayout /p $msixPath /nv /o
if ($LASTEXITCODE -ne 0) { Write-Error "makeappx failed"; exit 1 }

Write-Host "Signing MSIX..." -ForegroundColor Cyan
& $SignTool sign /fd SHA256 /f $pfxPath /p "NR_build_temp!" $msixPath
if ($LASTEXITCODE -ne 0) { Write-Error "signtool failed"; exit 1 }
Remove-Item $pfxPath

#  5. Install 
Write-Host "Removing previous registration (if any)..." -ForegroundColor Cyan
try { Get-AppxPackage "com.NotificationRelay.Desktop*" | Remove-AppxPackage } catch { }

Write-Host "Installing MSIX..." -ForegroundColor Cyan
Add-AppxPackage -Path $msixPath

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host ""
Write-Host "Launch the app:" -ForegroundColor White
Write-Host '  Start-Process "shell:AppsFolder\com.NotificationRelay.Desktop_fmsbxbq82ser0!App"' -ForegroundColor Yellow
Write-Host ""
Write-Host "Or search 'Notification Relay' in the Start menu."
Write-Host "Re-run this script any time you rebuild."
