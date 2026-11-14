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
Run { python "$PSScriptRoot/prepare.py" $Upstream --version $Version }
$Project = Join-Path $Upstream 'v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj'
$Publish = Join-Path $Work 'publish'
Run { dotnet publish $Project -c Release -r win-x64 --self-contained true -p:Version=$Version -p:PublishSingleFile=true -p:DebugType=None -p:RestorePackagesWithLockFile=true -o $Publish }
$Payload = Join-Path $Work 'payload'
New-Item -ItemType Directory $Payload | Out-Null
Copy-Item "$Publish/*" $Payload -Recurse -Force
Rename-Item "$Payload/v2rayN.exe" 'MoskomVPN.exe'
Copy-Item "$Client/branding" "$Payload/brand" -Recurse -Force
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
@{routingCommit=$Sources.russia.routingCommit;rulesCommit=$Sources.russia.rulesCommit;geoRelease=$Sources.russia.geoRelease;fetchedAt=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content "$Preset/manifest.json" -Encoding utf8NoBOM
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
function ValidateSmokeReport([string]$Destination, [string]$AppDirectory) {
    if (Test-Path "$Destination/smoke-error.txt") { throw (Get-Content "$Destination/smoke-error.txt" -Raw) }
    $Report = Get-Content "$Destination/smoke.json" -Raw | ConvertFrom-Json
    if ($Report.status -ne 'Disconnected' -or !$Report.proxyUnchanged -or !$Report.tunDisabled -or $Report.routing.Count -lt 3 -or !$Report.subscriptionImported -or !$Report.allServersVisible -or !$Report.smokeConnectionBlocked -or !$Report.routingRulesLoaded) { throw 'Invalid launcher state.' }
    foreach ($Name in @('MoskomPower','MoskomPowerIcon','MoskomAdd','MoskomSubscriptions','MoskomServers','MoskomRouting','MoskomAvailability','MoskomWebsiteStatus','MoskomLogin','MoskomMenu','MoskomSiteLogo')) { if ($Report.controls -notcontains $Name) { throw "UI missing $Name" } }
    if (!$Report.vectorIcon) { throw 'Power icon is not a vector.' }
    foreach ($Name in @('LoginForm','MultipleSubscriptions','RotatedLink','ExpiredSkipped','RevokedRemoved','SessionEncrypted','LogoutPreservedManual','AvailabilityChecked','SubscriptionPicker','ServerIsolation','SelectionPreserved','EmptySubscriptionSafe','StandaloneGroup','SameSelectionKeptConnection','RefreshKeptConnection','BackgroundSyncKeptConnection','OtherSubscriptionKeptConnection','UnchangedSubscriptionKeptConnection','FailedDownloadKeptConnection','ActiveChangeReconnected','ManualSelectionReconnected','RevocationStoppedConnection')) { if (!$Report.website.$Name) { throw "Website smoke failed: $Name" } }
    foreach ($Name in @('Burger','Brand','Dns','RulesApplied','RulesRollback','UpdatePrompt','LaterDefers','Help')) { if (!$Report.settings.$Name) { throw "Settings smoke failed: $Name" } }
    if (!$Report.management.Passed) { throw "Subscription management failed: $($Report.management | ConvertTo-Json -Compress)" }
    $Xray = Get-ChildItem "$AppDirectory/bin" -Recurse -File -Filter xray.exe | Select-Object -First 1
    if (!$Xray) { throw 'Packaged Xray is missing.' }
    $OldAsset = $env:XRAY_LOCATION_ASSET; $env:XRAY_LOCATION_ASSET = Join-Path $AppDirectory 'bin'
    try { foreach ($ConfigFile in Get-ChildItem $Destination -File -Filter 'dns-*.json') { Run { & $Xray.FullName run -test -c $ConfigFile.FullName } } }
    finally { if ($null -eq $OldAsset) { Remove-Item Env:XRAY_LOCATION_ASSET -ErrorAction SilentlyContinue } else { $env:XRAY_LOCATION_ASSET = $OldAsset } }
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
        ValidateSmokeReport $Destination (Split-Path $Exe -Parent)
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
Download 'https://github.com/stisvladnext3-creator/moskompremont/releases/download/moskom-vpn-v0.3.12/MoskomVPN-Setup-0.3.12.exe' $Previous 'e3b52d591ee796643cc8723749ac1552fd8f84054fa5a3da18c42ebf0e7752c1'
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
Add-Type -Path "$PSScriptRoot/ShellLinkReader.cs"
function VerifyInstallerShortcuts([string]$Label) {
    $ExpectedTargetHash = (Get-FileHash "$Install/MoskomVPN.exe").Hash
    $ExpectedIconHash = (Get-FileHash "$Client/branding/moskom-vpn.ico").Hash
    $Details = foreach ($Link in @($DesktopLink, $StartLink)) {
        if (!(Test-Path -LiteralPath $Link)) { throw "Installer did not create $Link" }
        $CopyName = if ($Link -eq $DesktopLink) { 'desktop' } else { 'start-menu' }
        Copy-Item -LiteralPath $Link "$Output/smoke/$CopyName-$Label.lnk"
        $Shortcut = [Moskom.Build.ShellLinkReader]::Read($Link)
        # Windows can return short DOS paths from a shell link. Verify the actual
        # linked files, rather than requiring one spelling of the installation path.
        $Target = [Environment]::ExpandEnvironmentVariables($Shortcut.Target).Trim('"')
        $Icon = [Environment]::ExpandEnvironmentVariables($Shortcut.Icon).Trim('"')
        [ordered]@{link=$Link;target=$Target;icon=$Icon;expectedTarget="$Install/MoskomVPN.exe"}
    }
    $Details | ConvertTo-Json | Tee-Object -FilePath "$Output/smoke/shortcuts-$Label.json" | Write-Host
    foreach ($Detail in $Details) {
        if (!(Test-Path -LiteralPath $Detail.target -PathType Leaf) -or (Get-FileHash -LiteralPath $Detail.target).Hash -ne $ExpectedTargetHash) { throw "Shortcut does not launch the installed application: $($Detail.link)" }
        if (!(Test-Path -LiteralPath $Detail.icon -PathType Leaf) -or (Get-FileHash -LiteralPath $Detail.icon).Hash -ne $ExpectedIconHash) { throw "Shortcut does not use the website logo: $($Detail.link)" }
    }
}
VerifyInstallerShortcuts 'upgrade'
if ((Get-FileHash "$Install/brand/moskom-vpn.ico").Hash -ne (Get-FileHash "$Client/branding/moskom-vpn.ico").Hash) { throw 'Installed icon differs from website branding.' }
SmokeCheck "$Install/MoskomVPN.exe" (Join-Path $Work 'installed-smoke-profile') (Join-Path $Output 'smoke/installed')
# Exercise the same parent-wait + silent install + automatic relaunch used by the app updater.
$UpdateSmoke = Join-Path $Output 'smoke/updater'; New-Item -ItemType Directory -Force $UpdateSmoke | Out-Null
$env:MOSKOM_SMOKE_TEST='1'; $env:MOSKOM_SMOKE_HOME=Join-Path $Work 'updater-smoke-profile'; $env:MOSKOM_SMOKE_OUTPUT=$UpdateSmoke
$BeforeUpdateProxy=ProxyState
try {
    $Parent=Start-Process powershell.exe -ArgumentList @('-NoProfile','-Command','Start-Sleep -Seconds 2') -PassThru
    $Updated=Start-Process $Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/MOSKOMUPDATE=1',("/MOSKOMPID="+$Parent.Id),('"/DIR={0}"' -f $Install)) -Wait -PassThru
    if($Updated.ExitCode -ne 0){throw 'Automatic update installer failed.'}
    $Deadline=[DateTime]::UtcNow.AddSeconds(90)
    while(!(Test-Path "$UpdateSmoke/smoke.json") -and !(Test-Path "$UpdateSmoke/smoke-error.txt") -and [DateTime]::UtcNow -lt $Deadline){Start-Sleep -Milliseconds 250}
    foreach($Running in Get-Process -Name MoskomVPN -ErrorAction SilentlyContinue){if($Running.Path -eq "$Install\MoskomVPN.exe"){if(!$Running.WaitForExit(10000)){throw 'Relaunched application did not finish its smoke check.'}}}
    if((ProxyState) -ne $BeforeUpdateProxy){throw 'Updater smoke modified the Windows proxy.'}
    ValidateSmokeReport $UpdateSmoke $Install
}
finally{Remove-Item Env:MOSKOM_SMOKE_TEST,Env:MOSKOM_SMOKE_HOME,Env:MOSKOM_SMOKE_OUTPUT}
$Uninstalled = Start-Process "$Install/unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
if ($Uninstalled.ExitCode -ne 0) { throw "Uninstaller failed: $($Uninstalled.ExitCode)" }
if ((Test-Path "$Install/MoskomVPN.exe") -or (Test-Path $DesktopLink) -or (Test-Path $StartLink)) { throw 'Uninstaller left binaries or shortcuts.' }
# Verify the desktop icon also appears when there is no previous installation.
$Fresh=Start-Process $Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('"/DIR={0}"' -f $Install)) -Wait -PassThru
if($Fresh.ExitCode -ne 0 -or !(Test-Path $DesktopLink)){throw 'Fresh install did not create the desktop shortcut.'}
VerifyInstallerShortcuts 'fresh'
$Removed=Start-Process "$Install/unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
if($Removed.ExitCode -ne 0){throw 'Fresh installation could not be uninstalled.'}
@{installed=$true;upgradedFrom0312=$true;automaticRelaunch=$true;freshDesktopShortcut=$true;installedAppLaunched=$true;shortcutsVerified=$true;uninstalled=$true;realProxyUnchanged=$true} | ConvertTo-Json | Set-Content "$Output/smoke/installer-validation.json" -Encoding utf8
Copy-Item "$Output/smoke/launcher.png" "$Output/MoskomVPN-Preview.png"
Copy-Item "$Output/smoke/login.png" "$Output/MoskomVPN-Login.png"
foreach($Name in @('menu','dns','update','help','subscriptions')){Copy-Item "$Output/smoke/$Name.png" "$Output/MoskomVPN-$Name.png"}
# Corresponding modified application source and build instructions, without secrets/cache/output.
$SourceBundle = Join-Path $Work 'source-bundle'
Run { python "$PSScriptRoot/source-bundle.py" $Upstream $Client $SourceBundle }
Compress-Archive "$SourceBundle/*" "$Output/MoskomVPN-Source-$Version.zip" -CompressionLevel Optimal
Get-ChildItem $Output -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } | Set-Content "$Output/SHA256SUMS.txt" -Encoding utf8
Write-Host "Installer, source, SHA256SUMS and smoke report prepared in $Output"
