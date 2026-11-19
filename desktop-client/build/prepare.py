"""Apply reviewed, exact-version overlays to upstream v2rayN 7.25.6. No network access."""
from pathlib import Path
import shutil,sys,re
root=Path(sys.argv[1]).resolve();own=Path(__file__).resolve().parents[1]
legacy='--legacy' in sys.argv
upstream_version='7.20.0' if legacy else '7.25.6'
version=sys.argv[sys.argv.index('--version')+1] if '--version' in sys.argv else '0.3.0'
if not re.fullmatch(r'\d+\.\d+\.\d+',version):raise SystemExit('Invalid application version')
def edit(path,old,new,count=1):
 p=root/path;s=p.read_text(encoding='utf-8-sig')
 if s.count(old)!=count:raise SystemExit(f'Upstream anchor mismatch: {path}: {old[:70]!r}')
 p.write_text(s.replace(old,new,count),encoding='utf-8')
for f in (own/'overlay').rglob('*'):
 if f.is_file():
  p=root/'v2rayN'/f.relative_to(own/'overlay');p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,p)
info=root/'v2rayN/ServiceLib/Services/MoskomBuildInfo.cs';info.write_text(info.read_text().replace('"0.3.0"',f'"{version}"'),encoding='utf-8')
# The legacy channel is built from the same source tree with a .NET 6 target.
# v2rayN 7.20 has one Windows-only project that declares a Windows 10 TFM
# explicitly, so lower that declaration as well as the shared target.
if legacy:
 props=root/'v2rayN/Directory.Build.props';s=props.read_text(encoding='utf-8');s=s.replace('<TargetFramework>net8.0</TargetFramework>','<TargetFramework>net6.0</TargetFramework>');s=s.replace('<TargetLatestRuntimePatch>true</TargetLatestRuntimePatch>','<TargetLatestRuntimePatch>true</TargetLatestRuntimePatch>\n        <LangVersion>12.0</LangVersion>');props.write_text(s,encoding='utf-8')
 winproj=root/'v2rayN/v2rayN/v2rayN.csproj';s=winproj.read_text(encoding='utf-8');s=s.replace('net8.0-windows10.0.19041.0','net6.0-windows');winproj.write_text(s,encoding='utf-8')
 packages=root/'v2rayN/Directory.Packages.props';s=packages.read_text(encoding='utf-8')
 for old,new in [('ReactiveUI.Avalonia" Version="11.4.12','ReactiveUI.Avalonia" Version="11.3.8'),('Downloader" Version="5.1.0','Downloader" Version="3.3.4'),('ReactiveUI" Version="23.1.8','ReactiveUI" Version="22.2.1')]: s=s.replace(old,new)
 packages.write_text(s,encoding='utf-8')
 hotkeys=root/'v2rayN/GlobalHotKeys/src/GlobalHotKeys/GlobalHotKeys.csproj'
 if hotkeys.exists():
  s=hotkeys.read_text(encoding='utf-8').replace('<TargetFramework>net8.0</TargetFramework>','<TargetFramework>net6.0</TargetFramework>');hotkeys.write_text(s,encoding='utf-8')
 fileutils=root/'v2rayN/ServiceLib/Common/FileUtils.cs'
 s=fileutils.read_text(encoding='utf-8').replace('using System.Formats.Tar;\n','')
 s=s.replace('''            using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            using var gz = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: true);
            TarFile.ExtractToDirectory(gz, toPath, overwriteFiles: true);''','''            throw new PlatformNotSupportedException("Tar extraction is unavailable in the Windows 8 compatibility build.");''')
 fileutils.write_text(s,encoding='utf-8')
 # v2rayN 7.20 predates several APIs used by the current overlay. Keep the
 # compatibility code in the generated legacy tree so the normal channel is
 # built from the unmodified upstream APIs.
 for name in ('v2rayN/ServiceLib/Handler/SysProxy/ProxySettingWindows.cs', 'v2rayN/ServiceLib/Services/WindowsJobService.cs'):
  p=root/name;s=p.read_text(encoding='utf-8').replace('nint.Zero','IntPtr.Zero');p.write_text(s,encoding='utf-8')
 for p in (root/'v2rayN').rglob('*.cs'):
  s=p.read_text(encoding='utf-8')
  if 'nint.Zero' in s or 'new nint(' in s: p.write_text(s.replace('nint.Zero','IntPtr.Zero').replace('new nint(','new IntPtr('),encoding='utf-8')
 downloader=root/'v2rayN/ServiceLib/Helper/DownloaderHelper.cs'
 s=downloader.read_text(encoding='utf-8')
 s=s.replace('            BlockTimeout = timeout * 1000,\n','')
 s=s.replace('            MaxTryAgainOnFailure = 2,\n','')
 s=s.replace('                    ConnectTimeout = timeout * 1000,\n','').replace('                    ConnectTimeout= timeout * 1000,\n','')
 s=s.replace('await reader.ReadToEndAsync(cts.Token)','await reader.ReadToEndAsync()')
 downloader.write_text(s,encoding='utf-8')
 utils_legacy=root/'v2rayN/ServiceLib/Common/Utils.cs'
 s=utils_legacy.read_text(encoding='utf-8')
 s=s.replace('''                var currentMode = File.GetUnixFileMode(fileName);
                File.SetUnixFileMode(fileName, currentMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                return true;''','''                // Unix permission bits are unavailable in the net6 Windows 8
                // target. The legacy payload only executes on Windows.
                return false;''')
 utils_legacy.write_text(s,encoding='utf-8')
 compat=root/'v2rayN/ServiceLib/Services/MoskomNet6Compat.cs'
 compat.write_text('''using System;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute(string feature) : Attribute
    {
        public string FeatureName { get; } = feature;
        public bool IsOptional { get; set; }
    }
    internal static class IsExternalInit { }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}
''',encoding='utf-8')
 # Unix-only file mode calls are intentionally no-ops in the Windows 8 tree.
 selective=root/'v2rayN/ServiceLib/Services/MoskomSelectiveRuntime.cs'
 s=selective.read_text(encoding='utf-8').replace('if(!OperatingSystem.IsWindows())File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);','')
 selective.write_text(s,encoding='utf-8')
 settings_smoke=root/'v2rayN/v2rayN.Desktop/Views/MoskomSettingsSmoke.cs'
 s=settings_smoke.read_text(encoding='utf-8').replace('if(!OperatingSystem.IsWindows())File.SetUnixFileMode(copy,UnixFileMode.UserRead|UnixFileMode.UserWrite);','')
 settings_smoke.write_text(s,encoding='utf-8')
 tun=root/'v2rayN/ServiceLib/Services/MoskomTunSupervisor.cs'
 s=tun.read_text(encoding='utf-8')
 s=s.replace('Directory.CreateDirectory(owner._socketDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);','Directory.CreateDirectory(owner._socketDirectory);')
 s=s.replace('File.SetUnixFileMode(endpoint,UnixFileMode.UserRead|UnixFileMode.UserWrite);','')
 s=s.replace('var directory=Directory.CreateTempSubdirectory("moskom-tun-").FullName;','var directory=Path.Combine(Path.GetTempPath(),"moskom-tun-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);')
 s=s.replace('    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);\n','')
 s=s.replace(';if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;',';')
 tun.write_text(s,encoding='utf-8')
 # The 7.20 view model exposes the status bar as a singleton.
 for name in ('v2rayN/v2rayN.Desktop/Views/MoskomLauncherView.cs', 'v2rayN/v2rayN.Desktop/Views/MoskomSettingsSmoke.cs', 'v2rayN/v2rayN.Desktop/Views/MoskomWebsiteSmoke.cs', 'v2rayN/v2rayN.Desktop/Views/MoskomWindowIntegration.cs'):
  p=root/name;s=p.read_text(encoding='utf-8').replace('_vm.StatusBarViewModel','StatusBarViewModel.Instance').replace('vm.StatusBarViewModel','StatusBarViewModel.Instance');p.write_text(s,encoding='utf-8')
 for p in (root/'v2rayN/v2rayN.Desktop/Views').glob('Moskom*.cs'):
  s=p.read_text(encoding='utf-8').replace('await StatusBarViewModel.Instance.RefreshRoutingsMenu();','AppEvents.RoutingsMenuRefreshRequested.Publish();')
  p.write_text(s,encoding='utf-8')
 smoke_engine=root/'v2rayN/v2rayN.Desktop/Views/MoskomConnectionSmokeEngine.cs'
 s=smoke_engine.read_text(encoding='utf-8')
 if 'using System.IO;\n' not in s: s='using System.IO;\n'+s
 smoke_engine.write_text(s,encoding='utf-8')
 selective_smoke=root/'v2rayN/v2rayN.Desktop/Views/MoskomSelectiveSmoke.cs'
 s=selective_smoke.read_text(encoding='utf-8').replace('Preferences.Selective.Services.Order().SequenceEqual','Preferences.Selective.Services.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual')
 selective_smoke.write_text(s,encoding='utf-8')
 settings=root/'v2rayN/ServiceLib/Services/MoskomSettingsRuntime.cs'
 s=settings.read_text(encoding='utf-8').replace('await ViewModel.StatusBarViewModel.RefreshRoutingsMenu()','ServiceLib.Events.AppEvents.RoutingsMenuRefreshRequested.Publish()')
 settings.write_text(s,encoding='utf-8')
 website=root/'v2rayN/ServiceLib/Services/MoskomWebsiteRuntime.cs'
 s=website.read_text(encoding='utf-8').replace('''            await test.RunLoop(ESpeedActionType.Realping, [selected], timeout.Token);
            return new(selected.Remarks, delay, DateTimeOffset.Now);''','''            test.RunLoop(ESpeedActionType.Realping, [selected]);
            await test.WaitForCompletionAsync(timeout.Token);
            return new(selected.Remarks, delay, DateTimeOffset.Now);''')
 website.write_text(s,encoding='utf-8')
 speedtest=root/'v2rayN/ServiceLib/Services/SpeedtestService.cs'
 s=speedtest.read_text(encoding='utf-8')
 s=s.replace('''    public void RunLoop(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        Task.Run(async () =>''','''    private Task? _moskomCompletion;
    public Task WaitForCompletionAsync(CancellationToken ct) => (_moskomCompletion ?? Task.CompletedTask).WaitAsync(ct);

    public void RunLoop(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        _moskomCompletion = Task.Run(async () =>''')
 speedtest.write_text(s,encoding='utf-8')
 launcher=root/'v2rayN/v2rayN.Desktop/Views/MoskomLauncherView.cs';s=launcher.read_text(encoding='utf-8').replace('v2rayN 7.25.6',f'v2rayN {upstream_version}');launcher.write_text(s,encoding='utf-8')
assets=root/'v2rayN/v2rayN.Desktop/Assets'
for name in ['v2rayN.ico','moskom-vpn.ico']+[f'NotifyIcon{i}.ico' for i in range(1,6)]:shutil.copyfile(own/'branding/moskom-vpn.ico',assets/name)
shutil.copyfile(own/'branding/app-logo.png',assets/'moskom-logo.png')
utils='v2rayN/ServiceLib/Common/Utils.cs'
a='''        if (Environment.GetEnvironmentVariable(Global.LocalAppData) == "1")
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "v2rayN");
        }

        return GetBaseDirectory();'''
b='''        var smoke = Environment.GetEnvironmentVariable("MOSKOM_SMOKE_HOME");
        return string.IsNullOrEmpty(smoke)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MosKomRemontVPN")
            : Path.GetFullPath(smoke);'''
edit(utils,a,b)
edit(utils,'var tempPath = Path.Combine(StartupPath(), "bin");','var tempPath = Path.Combine(GetBaseDirectory(), "bin");')
edit('v2rayN/ServiceLib/Services/CoreConfig/Singbox/SingboxRulesetService.cs','Utils.GetBinPath("srss")','ServiceLib.Services.MoskomLauncherRuntime.RoutingSrsDirectory')
edit('v2rayN/ServiceLib/Manager/CoreManager.cs','''        var procService = new ProcessService(''','''        if (environmentVars.ContainsKey(Global.XrayLocalAsset)) environmentVars[Global.XrayLocalAsset] = ServiceLib.Services.MoskomLauncherRuntime.RoutingAssetDirectory;
        if (environmentVars.ContainsKey(Global.V2RayLocalAsset)) environmentVars[Global.V2RayLocalAsset] = ServiceLib.Services.MoskomLauncherRuntime.RoutingAssetDirectory;
        var procService = new ProcessService(''')
manager='v2rayN/ServiceLib/Manager/AppManager.cs'
edit(manager,'        return true;\n    }\n\n    public bool InitComponents()', '        ServiceLib.Services.MoskomLauncherRuntime.PrepareAsync(_config).GetAwaiter().GetResult();\n        Thread.CurrentThread.CurrentUICulture = new(_config.UiItem.CurrentLanguage);\n        return true;\n    }\n\n    public bool InitComponents()')
edit(manager,'            await SysProxyHandler.UpdateSysProxy(_config, true);','            await ServiceLib.Services.MoskomLauncherRuntime.ExitAsync();\n            await SysProxyHandler.UpdateSysProxy(_config, true);')
vm='v2rayN/ServiceLib/ViewModels/MainWindowViewModel.cs'
edit(vm,'            SetReloadEnabled(false);','''            SetReloadEnabled(false);
            if (!ServiceLib.Services.MoskomLauncherRuntime.AllowCoreReload)
            {
                await CoreManager.Instance.CoreStop();
                return;
            }''')
edit(vm,'    #region core job','''    public async Task RefreshForLauncherAsync()
    {
        await RefreshSubscriptions();
        await RefreshServersDispatcherAsync();
    }
    public async Task StopForLauncherAsync()
    {
        ServiceLib.Services.MoskomLauncherRuntime.AllowCoreReload = false;
        await _reloadSemaphore.WaitAsync();
        try { await CoreManager.Instance.CoreStop(); }
        finally { _reloadSemaphore.Release(); }
    }

    #region core job''')
# The launcher server picker includes every subscription and must not inherit the tray's limit.
if legacy:
 edit('v2rayN/ServiceLib/ViewModels/StatusBarViewModel.cs', '''        var lstModel = await AppManager.Instance.ProfileModels(_config.SubIndexId, "");

        Servers.Clear();
        if (lstModel.Count > _config.GuiItem.TrayMenuServersLimit)
        {
            BlServers = false;
            return;
        }''', '''        var lstModel = await AppManager.Instance.ProfileModels("", "") ?? [];

        Servers.Clear();''')
 edit('v2rayN/ServiceLib/ViewModels/StatusBarViewModel.cs', 'var name = it.GetSummary();', 'var name = it.SubRemarks?.StartsWith("Кабинет · ", StringComparison.Ordinal) == true ? it.SubRemarks + " · " + it.Remarks : it.GetSummary();')
 # The 7.20 speed-test API is fire-and-forget, and its MainWindowViewModel
 # refresh method is synchronous around the subscription event.
 if legacy:
  edit(vm, '''    public async Task RefreshForLauncherAsync()
    {
        await RefreshSubscriptions();
        await RefreshServersDispatcherAsync();
    }
    public async Task StopForLauncherAsync()
    {
        ServiceLib.Services.MoskomLauncherRuntime.AllowCoreReload = false;
        await _reloadSemaphore.WaitAsync();
        try { await CoreManager.Instance.CoreStop(); }
        finally { _reloadSemaphore.Release(); }
    }

    #region core job''', '''    public async Task RefreshForLauncherAsync()
    {
        RefreshSubscriptions();
        await RefreshServers();
    }
    public async Task StopForLauncherAsync()
    {
        ServiceLib.Services.MoskomLauncherRuntime.AllowCoreReload = false;
        await _reloadSemaphore.WaitAsync();
        try { await CoreManager.Instance.CoreStop(); }
        finally { _reloadSemaphore.Release(); }
    }

    #region core job''')
else:
 edit('v2rayN/ServiceLib/ViewModels/StatusBarViewModel.cs', '''        var lstModel = await AppManager.Instance.ProfileModels(_config.SubIndexId, "");

        if (lstModel?.Count > _config.GuiItem.TrayMenuServersLimit)
        {
            BlServers = false;
            return;
        }''', '''        var lstModel = await AppManager.Instance.ProfileModels("", "") ?? [];''')
 edit('v2rayN/ServiceLib/ViewModels/StatusBarViewModel.cs', 'Text = it.GetSummary()', 'Text = it.SubRemarks?.StartsWith("Кабинет · ", StringComparison.Ordinal) == true ? it.SubRemarks + " · " + it.Remarks : it.GetSummary()')
# Default-server recovery must remain inside the selected subscription, including
# an empty subscription. The upstream fallback otherwise picks any stored server.
edit('v2rayN/ServiceLib/Handler/ConfigHandler.cs', '''        var item = await AppManager.Instance.GetProfileItem(config.IndexId);
        if (item is null)
        {
            var item2 = await SQLiteHelper.Instance.TableAsync<ProfileItem>().FirstOrDefaultAsync();
            await SetDefaultServerIndex(config, item2?.IndexId);
            return item2;
        }

        return item;''', '''        var item = await AppManager.Instance.GetProfileItem(config.IndexId);
        var group = ServiceLib.Services.MoskomLauncherRuntime.Preferences.SelectedSubscriptionId;
        if (item != null && (group == null || (item.Subid ?? "") == group)) return item;
        var candidates = await AppManager.Instance.ProfileItems("") ?? [];
        var candidate = candidates.FirstOrDefault(p => group == null || (p.Subid ?? "") == group);
        if (candidate != null) await SetDefaultServerIndex(config, candidate.IndexId);
        return candidate;''')
# No unattended upstream updater may replace the branded application or bundled rules.
edit('v2rayN/ServiceLib/Manager/TaskManager.cs','        Task.Run(ScheduledTasks);','        // Updates are explicit in the launcher; binary updates are versioned installer releases.')
app='v2rayN/v2rayN.Desktop/App.axaml.cs'
if legacy:
 edit(app,'            desktop.MainWindow = new MainWindow();','            var mainWindow = new MainWindow();\n            mainWindow.EnableMoskomLauncher(mainWindow.ViewModel!);\n            desktop.MainWindow = mainWindow;')
else:
 edit(app,'            desktop.MainWindow = mainWindow;','            mainWindow.EnableMoskomLauncher(mainWindowViewModel);\n            desktop.MainWindow = mainWindow;')
main='v2rayN/v2rayN.Desktop/Views/MainWindow.axaml.cs'
edit(main,'        KeyDown += MainWindow_KeyDown;','        // Launcher owns its controls; hidden advanced shortcuts cannot alter proxy state.')
if legacy:
 edit(main,'            HotkeyManager.Instance.Init(_config, OnHotkeyHandler);','            // Global proxy shortcuts are disabled in the simple launcher.')
else:
 edit(main,'                HotkeyManager.Instance.Init(_config, OnHotkeyHandler);','                // Global proxy shortcuts are disabled in the simple launcher.')
program='v2rayN/v2rayN.Desktop/Program.cs'
edit(program,'        if (OnStartup(args) == false)','''        if (OperatingSystem.IsWindows() && args.Length == 3 && args[0] == "--moskom-watch-proxy")
        {
            if (int.TryParse(args[1], out var parent)) ServiceLib.Services.MoskomProxyLease.Watch(parent, args[2]);
            return;
        }
        if (OnStartup(args) == false)''')
edit(program, 'var exePathKey = Utils.GetMd5(Utils.GetExePath());', 'var exePathKey = Utils.GetMd5(Utils.StartupPath() + "|MoskomVPN");')
edit(program, '    public static EventWaitHandle ProgramStarted;', '    public static EventWaitHandle ProgramStarted;\n    private static Mutex? MoskomAppMutex;')
edit(program,'        BuildAvaloniaApp()','        if (OperatingSystem.IsWindows()) SetCurrentProcessExplicitAppUserModelID("MosKomRemont.VPN");\n        BuildAvaloniaApp()')
edit(program,'    public static EventWaitHandle ProgramStarted;','    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]\n    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);\n    public static EventWaitHandle ProgramStarted;')
edit(program, '        if (OnStartup(args) == false)', '        if (OperatingSystem.IsWindows())\n        {\n            MoskomAppMutex = new Mutex(true, @"Local\\MosKomRemontVPN.App", out var first);\n            if (!first)\n            {\n                var key = Utils.GetMd5(Utils.StartupPath() + "|MoskomVPN");\n                for (var i = 0; i < 30; i++)\n                {\n                    try { using var signal = EventWaitHandle.OpenExisting(key); signal.Set(); break; }\n                    catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }\n                }\n                return;\n            }\n        }\n        if (OnStartup(args) == false)')
# Keep the original safe tray/show/exit behavior, without unmanaged proxy/TUN toggles.
p=root/'v2rayN/v2rayN.Desktop/App.axaml';s=p.read_text(encoding='utf-8-sig')
a=s.index('                    <NativeMenu>');b=s.index('                    </NativeMenu>',a)+len('                    </NativeMenu>')
s=s[:a]+'''                    <NativeMenu>
                        <NativeMenuItem Command="{Binding ShowWindowCmd}" Header="Открыть МосКомпРемонт VPN" />
                        <NativeMenuItem Click="MenuExit_Click" Header="Выход" />
                    </NativeMenu>'''+s[b:];s=s.replace('ToolTipText="{Binding RunningServerToolTipText}"','ToolTipText="МосКомпРемонт VPN"');p.write_text(s,encoding='utf-8')
print(f'Prepared MosKomRemont VPN launcher over upstream {upstream_version}.')

# TUN helper runs without opening the GUI or touching account storage.
edit(program, '        if (OperatingSystem.IsWindows() && args.Length == 3', '        if (args.Length == 6 && args[0] == "--moskom-tun-helper")\n        {\n            try { Environment.ExitCode = ServiceLib.Services.MoskomTunSupervisor.RunHelperAsync(args).GetAwaiter().GetResult(); } catch { Environment.ExitCode = 2; }\n            return;\n        }\n        if (OperatingSystem.IsWindows() && args.Length == 3')
