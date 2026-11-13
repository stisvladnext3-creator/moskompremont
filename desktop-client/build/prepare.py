"""Apply reviewed, exact-version overlays to upstream v2rayN 7.25.6. No network access."""
from pathlib import Path
import shutil,sys,re
root=Path(sys.argv[1]).resolve();own=Path(__file__).resolve().parents[1]
def edit(path,old,new,count=1):
 p=root/path;s=p.read_text(encoding='utf-8-sig')
 if s.count(old)!=count:raise SystemExit(f'Upstream anchor mismatch: {path}: {old[:70]!r}')
 p.write_text(s.replace(old,new,count),encoding='utf-8')
for f in (own/'overlay').rglob('*'):
 if f.is_file():
  p=root/'v2rayN'/f.relative_to(own/'overlay');p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,p)
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
# No unattended upstream updater may replace the branded application or bundled rules.
edit('v2rayN/ServiceLib/Manager/TaskManager.cs','        Task.Run(ScheduledTasks);','        // Updates are explicit in the launcher; binary updates are signed/versioned installer releases.')
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
edit(program, '        if (OnStartup(args) == false)', '        if (OperatingSystem.IsWindows())\n        {\n            MoskomAppMutex = new Mutex(true, @"Local\\MosKomRemontVPN.App", out var first);\n            if (!first)\n            {\n                var key = Utils.GetMd5(Utils.StartupPath() + "|MoskomVPN");\n                for (var i = 0; i < 30; i++)\n                {\n                    try { using var signal = EventWaitHandle.OpenExisting(key); signal.Set(); break; }\n                    catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }\n                }\n                return;\n            }\n        }\n        if (OnStartup(args) == false)')
# Keep the original safe tray/show/exit behavior, without unmanaged proxy/TUN toggles.
p=root/'v2rayN/v2rayN.Desktop/App.axaml';s=p.read_text(encoding='utf-8-sig')
a=s.index('                    <NativeMenu>');b=s.index('                    </NativeMenu>',a)+len('                    </NativeMenu>')
s=s[:a]+'''                    <NativeMenu>
                        <NativeMenuItem Command="{Binding ShowWindowCmd}" Header="Открыть МосКомпРемонт VPN" />
                        <NativeMenuItem Click="MenuExit_Click" Header="Выход" />
                    </NativeMenu>'''+s[b:];p.write_text(s,encoding='utf-8')
print('Prepared MosKomRemont VPN launcher over upstream 7.25.6.')
