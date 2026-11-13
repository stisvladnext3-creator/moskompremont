param([string]$Version = '0.1.0', [string]$Output = "$PSScriptRoot/../out")
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Client = (Resolve-Path "$PSScriptRoot/..").Path
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $Output | Out-Null
$TempBase = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$Work = Join-Path $TempBase ('moskom-vpn-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $Work | Out-Null
$Sources = Get-Content "$PSScriptRoot/sources.json" -Raw | ConvertFrom-Json
function Run([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Command failed: $Command" } }
function Download([string]$Url, [string]$To, [string]$Sha = '') {
    Invoke-WebRequest -Uri $Url -OutFile $To -MaximumRedirection 10
    if ($Sha -and (Get-FileHash -Algorithm SHA256 $To).Hash.ToLowerInvariant() -ne $Sha.ToLowerInvariant()) { throw "Checksum mismatch: $([IO.Path]::GetFileName($To))" }
}
$Upstream = Join-Path $Work 'upstream'
Run { git clone --quiet --depth 1 --branch $Sources.upstream.version https://github.com/2dust/v2rayN.git $Upstream }
$Actual = (git -C $Upstream rev-parse HEAD).Trim()
if ($Actual -ne $Sources.upstream.commit) { throw 'Upstream commit mismatch.' }
Run { git -C $Upstream submodule update --init --recursive --depth 1 }
Run { python "$PSScriptRoot/prepare.py" $Upstream }
$Project = Join-Path $Upstream 'v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj'
$Publish = Join-Path $Work 'publish'
Run { dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:RestorePackagesWithLockFile=true -o $Publish }
$Payload = Join-Path $Work 'payload'
New-Item -ItemType Directory $Payload | Out-Null
Copy-Item "$Publish/*" $Payload -Recurse -Force
Rename-Item "$Payload/v2rayN.exe" 'MoskomVPN.exe'
$StockZip = Join-Path $Work 'stock.zip'
Download $Sources.upstream.url $StockZip $Sources.upstream.sha256
$Stock = Join-Path $Work 'stock'
Expand-Archive $StockZip $Stock
$StockExe = Get-ChildItem $Stock -Recurse -File -Filter v2rayN.exe | Select-Object -First 1
if (!$StockExe) { throw 'Official archive has no v2rayN.exe.' }
Copy-Item (Join-Path $StockExe.DirectoryName 'bin') $Payload -Recurse -Force
$Preset = Join-Path $Payload 'preset-russia'
New-Item -ItemType Directory $Preset -Force | Out-Null
$RoutingBase = "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-custom-routing-list/$($Sources.russia.routingCommit)/v2rayN/"
Download ($RoutingBase + 'template.json') "$Preset/template.json"
$Template = Get-Content "$Preset/template.json" -Raw | ConvertFrom-Json
$RuleTexts = @()
for ($i = 0; $i -lt $Template.routingItems.Count; $i++) {
    $Name = [IO.Path]::GetFileName(([uri]$Template.routingItems[$i].url).AbsolutePath)
    Download ($RoutingBase + $Name) "$Preset/route-$i.json"
    $RuleTexts += Get-Content "$Preset/route-$i.json" -Raw
}
foreach ($Name in @('v2ray.json','sing_box.json','simple_dns.json')) {
    Download ($RoutingBase + $Name) "$Preset/$Name"
}
# The official DNS templates may reference separate raw JSON files. Resolve them while building.
foreach ($Name in @('v2ray.json','sing_box.json')) {
    $Doc = Get-Content "$Preset/$Name" -Raw | ConvertFrom-Json
    foreach ($Field in @('normalDNS','tunDNS')) {
        if ($Doc.PSObject.Properties.Name -contains $Field -and [string]$Doc.$Field -match '^https://(?:raw\.githubusercontent\.com/runetfreedom/|cdn\.jsdelivr\.net/gh/runetfreedom/russia-v2ray-custom-routing-list@)' ) {
            $Resource = [IO.Path]::GetFileName(([uri]$Doc.$Field).AbsolutePath)
            $Temp = Join-Path $Work ('dns-' + $Resource)
            Download ($RoutingBase + $Resource) $Temp
            $Doc.$Field = Get-Content $Temp -Raw
        }
    }
    $Doc | ConvertTo-Json -Depth 100 | Set-Content "$Preset/$Name" -Encoding utf8
}
foreach ($File in $Sources.russia.files) { Download $File.url (Join-Path "$Payload/bin" $File.name) $File.sha256 }
# sing-box already supports local .srs files in bin/srss; bundle the exact rules used by this preset.
$AllRules = ($RuleTexts -join "`n") + (Get-Content "$Preset/simple_dns.json" -Raw) + (Get-Content "$Preset/v2ray.json" -Raw) + (Get-Content "$Preset/sing_box.json" -Raw)
$Srs = Join-Path $Payload 'bin/srss'
New-Item -ItemType Directory -Force $Srs | Out-Null
$Tags = [regex]::Matches($AllRules, '(geoip|geosite):([A-Za-z0-9_@!.-]+)') | ForEach-Object { $_.Groups[1].Value + '-' + $_.Groups[2].Value } | Sort-Object -Unique
foreach ($Tag in $Tags) {
    $Type = if ($Tag.StartsWith('geosite-')) { 'geosite' } else { 'geoip' }
    $Url = "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/$($Sources.russia.rulesCommit)/sing-box/rule-set-$Type/$Tag.srs"
    Download $Url "$Srs/$Tag.srs"
}
Copy-Item "$Client/LICENSE" "$Payload/LICENSE-v2rayN-GPL-3.0.txt"
Copy-Item "$Client/КАК-ПОЛЬЗОВАТЬСЯ.txt" $Payload
Copy-Item "$Client/THIRD-PARTY-NOTICES.txt" $Payload
Copy-Item "$PSScriptRoot/sources.json" "$Payload/sources.json"
# Every delivered executable/preset gets a manifest. The installer never needs to download it.
Get-ChildItem $Payload -Recurse -File | ForEach-Object {
    $Rel = [IO.Path]::GetRelativePath($Payload, $_.FullName).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $Rel
} | Set-Content "$Payload/SHA256SUMS.txt" -Encoding utf8
# UI smoke check uses a disposable profile and cannot acquire the real Windows proxy.
function ProxyState {
    $Key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Internet Settings')
    $Values = [ordered]@{}
    foreach ($Name in @('ProxyEnable','ProxyServer','ProxyOverride','AutoConfigURL')) { $Values[$Name] = if ($Key) { $Key.GetValue($Name,$null) } else { $null } }
    if ($Key) { $Key.Dispose() }
    return ($Values | ConvertTo-Json -Compress)
}
function SmokeCheck([string]$Exe, [string]$Profile, [string]$Destination) {
    New-Item -ItemType Directory $Destination -Force | Out-Null
    $env:MOSKOM_SMOKE_TEST = '1'; $env:MOSKOM_SMOKE_HOME = $Profile; $env:MOSKOM_SMOKE_OUTPUT = $Destination
    $BeforeProxy = ProxyState
    try {
        $Process = Start-Process $Exe -PassThru
        if (!$Process.WaitForExit(90000)) { $Process.Kill(); throw 'UI smoke check timed out.' }
        $Process.Refresh()
        if ((ProxyState) -ne $BeforeProxy) { throw 'Smoke test modified real Windows proxy.' }
        if (Test-Path "$Destination/smoke-error.txt") { throw (Get-Content "$Destination/smoke-error.txt" -Raw) }
        if ($Process.ExitCode -ne 0) {
            if (Test-Path "$Destination/smoke-error.txt") { Write-Host (Get-Content "$Destination/smoke-error.txt" -Raw) }
            throw "UI smoke check failed: $($Process.ExitCode)"
        }
        $Report = Get-Content "$Destination/smoke.json" -Raw | ConvertFrom-Json
        if ($Report.status -ne 'Disconnected' -or !$Report.proxyUnchanged -or !$Report.tunDisabled -or $Report.routing.Count -lt 3 -or !$Report.subscriptionImported -or !$Report.allServersVisible -or !$Report.smokeConnectionBlocked -or !$Report.routingRulesLoaded) { throw 'Invalid launcher state.' }
        foreach ($Name in @('MoskomPower','MoskomPowerIcon','MoskomAdd','MoskomServers','MoskomRouting','MoskomAvailability','MoskomWebsiteStatus','MoskomLogin')) { if ($Report.controls -notcontains $Name) { throw "UI missing $Name" } }
        if (!$Report.vectorIcon) { throw 'Power icon is not a vector.' }
        foreach ($Name in @('LoginForm','MultipleSubscriptions','RotatedLink','ExpiredSkipped','RevokedRemoved','SessionEncrypted','LogoutPreservedManual','AvailabilityChecked')) { if (!$Report.website.$Name) { throw "Website smoke failed: $Name" } }
    }
    finally {
        $ProfileLogs = Join-Path $Profile 'guiLogs'
        if (Test-Path $ProfileLogs) { Copy-Item $ProfileLogs "$Destination/logs" -Recurse -Force }
        Remove-Item Env:MOSKOM_SMOKE_TEST,Env:MOSKOM_SMOKE_HOME,Env:MOSKOM_SMOKE_OUTPUT
    }
}
SmokeCheck "$Payload/MoskomVPN.exe" (Join-Path $Work 'smoke-profile') (Join-Path $Output 'smoke')
$Compiler = "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe"
if (!(Test-Path $Compiler)) { throw 'Inno Setup compiler not found.' }
Run { & $Compiler "/DAppVersion=$Version" "/DPayloadDir=$Payload" "/DOutputDir=$Output" "$Client/installer/MoskomVPN.iss" }
# Install the actual package, launch it, then uninstall on this disposable Windows runner.
$Install = Join-Path $Work 'Installed App'
$Setup = Join-Path $Output "MoskomVPN-Setup-$Version.exe"
# Verify an in-place upgrade from the installer already delivered to clients.
$Previous = Join-Path $Work 'previous-setup.exe'
Download 'https://github.com/stisvladnext3-creator/moskompremont/releases/download/moskom-vpn-v0.1.3/MoskomVPN-Setup-0.1.3.exe' $Previous 'e515d91c961bb7865a764a2addc65711c9b5ffa2edce106887c6096dbc51051f'
$OldInstall = Start-Process $Previous -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('"/DIR={0}"' -f $Install)) -Wait -PassThru
if ($OldInstall.ExitCode -ne 0) { throw 'Previous version installation failed.' }
$PreviousHash = (Get-FileHash "$Install/MoskomVPN.exe").Hash
$InstallLog = Join-Path $Output 'smoke/install.log'
$Installed = Start-Process $Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('"/DIR={0}"' -f $Install),('"/LOG={0}"' -f $InstallLog)) -Wait -PassThru
if ($Installed.ExitCode -ne 0) { throw "Installer failed: $($Installed.ExitCode)" }
foreach ($File in @('MoskomVPN.exe','preset-russia/route-0.json','preset-russia/route-1.json','preset-russia/route-2.json','bin/geosite.dat','bin/geoip.dat','LICENSE-v2rayN-GPL-3.0.txt')) {
    if (!(Test-Path (Join-Path $Install $File))) { throw "Installer omitted $File" }
}
if ((Get-FileHash "$Install/MoskomVPN.exe").Hash -ne (Get-FileHash "$Payload/MoskomVPN.exe").Hash) { throw 'Installed binary differs from payload.' }
if ((Get-FileHash "$Install/MoskomVPN.exe").Hash -eq $PreviousHash) { throw 'Upgrade did not replace the previous application.' }
$DesktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'МосКомпРемонт VPN.lnk'
$StartLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'МосКомпРемонт VPN.lnk'
if (!(Test-Path $DesktopLink) -or !(Test-Path $StartLink)) { throw 'Installer did not create shortcuts.' }
SmokeCheck "$Install/MoskomVPN.exe" (Join-Path $Work 'installed-smoke-profile') (Join-Path $Output 'smoke/installed')
$Uninstalled = Start-Process "$Install/unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
if ($Uninstalled.ExitCode -ne 0) { throw "Uninstaller failed: $($Uninstalled.ExitCode)" }
if ((Test-Path "$Install/MoskomVPN.exe") -or (Test-Path $DesktopLink) -or (Test-Path $StartLink)) { throw 'Uninstaller left binaries or shortcuts.' }
@{installed=$true;upgradedFrom013=$true;installedAppLaunched=$true;shortcutsVerified=$true;uninstalled=$true;realProxyUnchanged=$true} | ConvertTo-Json | Set-Content "$Output/smoke/installer-validation.json" -Encoding utf8
Copy-Item "$Output/smoke/launcher.png" "$Output/MoskomVPN-Preview.png"
Copy-Item "$Output/smoke/login.png" "$Output/MoskomVPN-Login.png"
# Corresponding modified application source and build instructions, without secrets/cache/output.
$SourceBundle = Join-Path $Work 'source-bundle'
Run { python "$PSScriptRoot/source-bundle.py" $Upstream $Client $SourceBundle }
Compress-Archive "$SourceBundle/*" "$Output/MoskomVPN-Source-$Version.zip" -CompressionLevel Optimal
Get-ChildItem $Output -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } | Set-Content "$Output/SHA256SUMS.txt" -Encoding utf8
Write-Host "Installer, source, SHA256SUMS and smoke report prepared in $Output"
