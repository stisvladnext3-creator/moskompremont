"""Apply reviewed, exact-version overlays to upstream v2rayN 7.25.6. No network access."""
from pathlib import Path
import shutil,sys,re
root=Path(sys.argv[1]).resolve();own=Path(__file__).resolve().parents[1]
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
edit(app,'            desktop.MainWindow = mainWindow;','            mainWindow.EnableMoskomLauncher(mainWindowViewModel);\n            desktop.MainWindow = mainWindow;')
main='v2rayN/v2rayN.Desktop/Views/MainWindow.axaml.cs'
edit(main,'        KeyDown += MainWindow_KeyDown;','        // Launcher owns its controls; hidden advanced shortcuts cannot alter proxy state.')
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
print('Prepared MosKomRemont VPN launcher over upstream 7.25.6.')
