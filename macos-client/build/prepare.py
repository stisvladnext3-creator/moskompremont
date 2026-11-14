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
 # API-only backports: keep SemVer ordering and locking semantics on .NET 8.
 q=root/'v2rayN/v2rayN.Desktop/Manager/WindowDialog.cs';code=q.read_text().replace('openWindows.Reverse().FirstOrDefault','openWindows.ToArray().AsEnumerable().Reverse().FirstOrDefault');q.write_text(code)
 q=root/'v2rayN/ServiceLib/Services/UpdateService.cs';code=q.read_text().replace('                Architecture.RiscV64 => coreInfo?.DownloadUrlLinuxRiscV64,\n','');q.write_text(code)
 q=root/'v2rayN/ServiceLib/Models/Dto/SemanticVersion.cs';code=q.read_text();start=code.index('        var leftSpan = left.AsSpan();');end=code.index('    private static int CompareSegment',start)
 code=code[:start]+'''        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length,b.Length); i++)
        {
            var comparison = CompareSegment(a[i].AsSpan(),b[i].AsSpan());
            if (comparison != 0) return comparison;
        }
        return a.Length.CompareTo(b.Length);
    }

'''+code[end:];q.write_text(code)
edit('ServiceLib/Services/MoskomLauncherRuntime.cs','config.UiItem.AutoHideStartup = false;','config.UiItem.AutoHideStartup = false;\n        config.UiItem.MacOSShowInDock = true;')
edit('ServiceLib/Common/Utils.cs','return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);','return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Resources", fileName);')
edit('ServiceLib/Common/Utils.cs','Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MosKomRemontVPN")','Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "MosKomRemontVPN")')
edit('v2rayN.Desktop/Program.cs','_ = new Mutex(true, "v2rayN", out var bOnlyOneInstance);','MoskomAppMutex = new Mutex(true, "MosKomRemontVPN.Mac", out var bOnlyOneInstance);')
for file in ['v2rayN.Desktop/Views/MoskomLauncherView.cs','v2rayN.Desktop/Views/MoskomHelpView.cs']:
 q=root/'v2rayN'/file;s=q.read_text().replace('системный прокси Windows','системный прокси macOS').replace('прокси Windows','прокси macOS').replace('в трей; откройте его значком рядом с часами','в строку меню; откройте его значком сверху экрана');q.write_text(s)
# App bundles are immutable; downloaded rules, SQLite and generated configs stay in Application Support.
q=root/'v2rayN/ServiceLib/Common/Utils.cs';code=q.read_text();start=code.index('    public static string GetBinPath(');end=code.index('    public static string GetLogPath(',start);part=code[start:end];part=re.sub(r'        if \(!Directory.Exists\(tempPath\)\)\n        \{\n            Directory.CreateDirectory\(tempPath\);\n        \}\n','',part);part=re.sub(r'            if \(!Directory.Exists\(tempPath\)\)\n            \{\n                Directory.CreateDirectory\(tempPath\);\n            \}\n','',part);q.write_text(code[:start]+part+code[end:])
q=root/'v2rayN/ServiceLib/Manager/CoreManager.cs';code=q.read_text().replace('if (Utils.IsNonWindows())','if (Utils.IsNonWindows() && !OperatingSystem.IsMacOS())');q.write_text(code)
if not a.probe:
 for f in (OWN/'overlay').rglob('*.cs'):
  target=root/'v2rayN'/f.relative_to(OWN/'overlay');target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,target)
 edit('ServiceLib/Services/MoskomLauncherRuntime.cs','if (OperatingSystem.IsWindows() && !SmokeTest) MoskomProxyLease.RecoverAfterCrash();','if (!SmokeTest) MoskomMacProxyLease.RecoverAfterCrashAsync().GetAwaiter().GetResult();')
 edit('ServiceLib/Services/MoskomLauncherRuntime.cs','SmokeTest ? new NoProxy() : new WindowsLease()','SmokeTest ? new NoProxy() : new MoskomMacProxyLease()')
 edit('v2rayN.Desktop/Program.cs','        if (OperatingSystem.IsWindows() && args.Length == 3', '        if (args.Length == 3 && args[0] == "--moskom-watch-mac-proxy")\n        {\n            if (int.TryParse(args[1], out var parent)) ServiceLib.Services.MoskomMacProxyLease.WatchAsync(parent,args[2]).GetAwaiter().GetResult();\n            return;\n        }\n        if (OperatingSystem.IsWindows() && args.Length == 3')
 q=root/'v2rayN/ServiceLib/Services/MoskomSettingsRuntime.cs';code=q.read_text();start=code.index('        var args = $"/SILENT');end=code.index('        await AppManager.Instance.AppExitAsync(true);',start);q.write_text(code[:start]+'        await MoskomMacUpdateInstaller.StartAsync(installer);\n'+code[end:])
 q=root/'v2rayN/v2rayN.Desktop/Views/MoskomSettingsSmoke.cs';code=q.read_text();code=code.replace('var tag = "moskom-vpn-v"+availableVersion; var file = "MoskomVPN-Setup-"+availableVersion+".exe";','var tag = "moskom-mac-v"+availableVersion; var file = MoskomAppUpdates.FileName(availableVersion);');code=code.replace('            dns &= built.Success && result.Success;','''            dns &= built.Success && result.Success;
            profile.CoreType = ECoreType.sing_box;
            var sing = await ServiceLib.Handler.Builder.CoreConfigContextBuilder.Build(config,profile);
            var singResult = await CoreConfigHandler.GenerateClientConfig(sing.Context,Path.Combine(output,"singbox-"+choice+".json"));
            dns &= sing.Success && singResult.Success;''');q.write_text(code)
 q=root/'v2rayN/v2rayN.Desktop/Views/MoskomSettingsViews.cs';code=q.read_text().replace('После загрузки проверим файл, завершим подключение и запустим установку.','После проверки откроется образ macOS. Перетащите приложение в «Программы» и подтвердите замену. Приложение завершит подключение перед установкой.');q.write_text(code)
 q=root/'v2rayN/v2rayN.Desktop/Views/MoskomHelpView.cs';code=q.read_text().replace('«Обновить сейчас» скачивает и устанавливает её с перезапуском;','«Обновить сейчас» скачивает проверенный образ и открывает установку macOS. Перетащите приложение в «Программы» и подтвердите замену;');q.write_text(code)
 edit('v2rayN.Desktop/Program.cs','    public static void Main(string[] args)\n    {','    public static void Main(string[] args)\n    {\n        ServiceLib.Services.MoskomTunSupervisor.UnixUid = ServiceLib.Services.MoskomMacNative.mkr_uid;\n        ServiceLib.Services.MoskomTunSupervisor.VerifyUnixPeer = (fd,uid,pid) => ServiceLib.Services.MoskomMacNative.mkr_check_peer(fd,uid,pid)==0;\n        ServiceLib.Services.MoskomLauncherRuntime.MacApplications = () => Task.Run(() => { var data=new byte[262144];var length=ServiceLib.Services.MoskomMacNative.mkr_running_apps(data,data.Length);if(length<0)throw new InvalidOperationException("Не прочитан список приложений macOS.");return System.Text.Json.JsonSerializer.Deserialize<List<ServiceLib.Services.MoskomAppChoice>>(data.AsSpan(0,length),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? []; });')
 # Platform build metadata is also used to select the correct update channel/CPU.
 (root/'v2rayN/ServiceLib/Services/MoskomMacBuildInfo.cs').write_text('namespace ServiceLib.Services;\npublic static class MoskomMacBuildInfo { public const string Flavor="'+a.flavor+'"; public const string Rid="'+a.rid+'"; public const string MinimumOS="'+('11.0' if a.flavor=='legacy' else '13.0')+'"; }\n')
print('Prepared macOS',a.flavor,a.rid,a.version)
