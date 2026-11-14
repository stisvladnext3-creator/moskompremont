#!/usr/bin/env python3
"""Reuse the exact Windows UI/logic, then adapt only macOS integration."""
from pathlib import Path
import argparse,subprocess,re,shutil,json
OWN=Path(__file__).resolve().parents[1];REPO=OWN.parent
p=argparse.ArgumentParser();p.add_argument('upstream',type=Path);p.add_argument('--version',required=True);p.add_argument('--flavor',choices=['legacy','modern'],required=True);p.add_argument('--rid',choices=['osx-x64','osx-arm64'],required=True);p.add_argument('--probe',action='store_true');a=p.parse_args();root=a.upstream.resolve()
subprocess.run(['python3',str(REPO/'desktop-client/build/prepare.py'),str(root),'--version',a.version],check=True)
def edit(path,old,new,count=1):
 q=root/'v2rayN'/path;s=q.read_text(encoding='utf-8-sig')
 if s.count(old)!=count:raise SystemExit('macOS anchor mismatch: '+path+': '+old[:80])
 q.write_text(s.replace(old,new,count))
if a.flavor=='legacy':
 edit('Directory.Build.props','<TargetFramework>net10.0</TargetFramework>','<TargetFramework>net8.0</TargetFramework>\n        <LangVersion>preview</LangVersion>')
 for project in (root/'v2rayN').rglob('*.csproj'):
  code=project.read_text(encoding='utf-8-sig');project.write_text(code.replace('net10.0','net8.0'))
 for file in ['ServiceLib/Events/EventChannel.cs','ServiceLib/Services/SpeedtestService.cs']:
  q=root/'v2rayN'/file;s=q.read_text();q.write_text(s.replace('readonly Lock ','readonly object '))
edit('ServiceLib/Services/MoskomLauncherRuntime.cs','config.UiItem.AutoHideStartup = false;','config.UiItem.AutoHideStartup = false;\n        config.UiItem.MacOSShowInDock = true;')
edit('ServiceLib/Common/Utils.cs','Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MosKomRemontVPN")','Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "MosKomRemontVPN")')
edit('v2rayN.Desktop/Program.cs','_ = new Mutex(true, "v2rayN", out var bOnlyOneInstance);','MoskomAppMutex = new Mutex(true, "MosKomRemontVPN.Mac", out var bOnlyOneInstance);')
for file in ['v2rayN.Desktop/Views/MoskomLauncherView.cs','v2rayN.Desktop/Views/MoskomHelpView.cs']:
 q=root/'v2rayN'/file;s=q.read_text().replace('системный прокси Windows','системный прокси macOS').replace('прокси Windows','прокси macOS').replace('в трей; откройте его значком рядом с часами','в строку меню; откройте его значком сверху экрана');q.write_text(s)
if not a.probe:
 for f in (OWN/'overlay').rglob('*.cs'):
  target=root/'v2rayN'/f.relative_to(OWN/'overlay');target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,target)
 edit('ServiceLib/Services/MoskomLauncherRuntime.cs','if (OperatingSystem.IsWindows() && !SmokeTest) MoskomProxyLease.RecoverAfterCrash();','if (!SmokeTest) MoskomMacProxyLease.RecoverAfterCrashAsync().GetAwaiter().GetResult();')
 edit('ServiceLib/Services/MoskomLauncherRuntime.cs','SmokeTest ? new NoProxy() : new WindowsLease()','SmokeTest ? new NoProxy() : new MoskomMacProxyLease()')
 edit('v2rayN.Desktop/Program.cs','        if (OperatingSystem.IsWindows() && args.Length == 3', '        if (args.Length == 3 && args[0] == "--moskom-watch-mac-proxy")\n        {\n            if (int.TryParse(args[1], out var parent)) ServiceLib.Services.MoskomMacProxyLease.WatchAsync(parent,args[2]).GetAwaiter().GetResult();\n            return;\n        }\n        if (OperatingSystem.IsWindows() && args.Length == 3')
 # Platform build metadata is also used to select the correct update channel/CPU.
 (root/'v2rayN/ServiceLib/Services/MoskomMacBuildInfo.cs').write_text('namespace ServiceLib.Services;\npublic static class MoskomMacBuildInfo { public const string Flavor="'+a.flavor+'"; public const string Rid="'+a.rid+'"; public const string MinimumOS="'+('11.0' if a.flavor=='legacy' else '12.0')+'"; }\n')
print('Prepared macOS',a.flavor,a.rid,a.version)
