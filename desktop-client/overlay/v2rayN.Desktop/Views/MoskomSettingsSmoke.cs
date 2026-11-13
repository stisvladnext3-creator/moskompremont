using ServiceLib.Services;
using Avalonia.VisualTree;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace v2rayN.Desktop.Views;

internal static class MoskomSettingsSmoke
{
    internal sealed record Report(bool Burger, bool Brand, bool Dns, bool RulesApplied, bool RulesRollback, bool UpdatePrompt, bool LaterDefers, bool Help);
    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
    private static Window Child(Window owner, string title) => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.First(w => w != owner && w.Title!.StartsWith(title, StringComparison.Ordinal));
    private static T Control<T>(Window window, string name) where T : Avalonia.Controls.Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static void Capture(Control control, string folder, string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1,(int)control.Bounds.Width),Math.Max(1,(int)control.Bounds.Height)));
        bitmap.Render(control); bitmap.Save(Path.Combine(folder,name));
    }
    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach(var file in Directory.GetFiles(source))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
        foreach(var folder in Directory.GetDirectories(source))Copy(folder,Path.Combine(target,Path.GetFileName(folder)));
    }
    public static async Task<Report> RunAsync(Window owner, MainWindowViewModel vm)
    {
        var output = Environment.GetEnvironmentVariable("MOSKOM_SMOKE_OUTPUT")!;
        var burgerButton = Control<Button>(owner,"MoskomMenu"); var menu = burgerButton.ContextMenu!;
        menu.Open(burgerButton); await Task.Delay(150);
        var names = menu.Items.OfType<MenuItem>().Select(i=>i.Name).ToList();
        var burger = new[]{"MoskomRulesUpdate","MoskomAppUpdates","MoskomDns"}.All(names.Contains);
        Capture(menu,output,"menu.png"); menu.Close();
        Control<Button>(owner,"MoskomHelp").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
        var helpWindow = Child(owner,"Справка");
        var help = helpWindow.IsVisible && helpWindow.Icon != null && Control<StackPanel>(helpWindow,"MoskomHelpTopics").Children.Count == 14;
        Capture(helpWindow,output,"help.png");
        Control<ScrollViewer>(helpWindow,"MoskomHelpScroll").ScrollToEnd();
        Control<Button>(helpWindow,"MoskomHelpClose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
        menu.Items.OfType<MenuItem>().First(i=>i.Name=="MoskomMenuHelp").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(150);
        helpWindow = Child(owner,"Справка"); help &= helpWindow.IsVisible;
        Control<Button>(helpWindow,"MoskomHelpClose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
        var brand = owner.Icon != null && Control<Image>(owner,"MoskomSiteLogo").Source is Bitmap;
        var dnsTask = MoskomSettingsViews.ShowDnsAsync(owner); await Task.Delay(150); var dnsWindow = Child(owner,"DNS");
        Control<ComboBox>(dnsWindow,"MoskomDnsChoice").SelectedIndex = 1;
        var save = Control<Button>(dnsWindow,"MoskomDnsSave"); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var i=0;i<100&&!save.IsEnabled;i++)await Task.Delay(50);
        var dns = MoskomLauncherRuntime.Preferences.Dns=="cloudflare" && AppManager.Instance.Config.SimpleDNSItem.RemoteDNS=="https://1.1.1.1/dns-query";
        Capture(dnsWindow,output,"dns.png"); dnsWindow.Close(); await dnsTask;
        var config = AppManager.Instance.Config;
        foreach(var choice in new[]{"cloudflare","google","quad9","auto"})
        {
            await MoskomLauncherRuntime.SaveDnsAsync(choice,"");
            var profile = await ConfigHandler.GetDefaultServer(config); profile.CoreType = ECoreType.Xray;
            var built = await ServiceLib.Handler.Builder.CoreConfigContextBuilder.Build(config,profile);
            var result = await CoreConfigHandler.GenerateClientConfig(built.Context,Path.Combine(output,"dns-"+choice+".json"));
            dns &= built.Success && result.Success;
            if(choice!="auto")dns &= config.SimpleDNSItem.RemoteDNS==MoskomDnsChoices.RemoteAddress(choice);
        }
        await MoskomLauncherRuntime.SaveDnsAsync("cloudflare","");
        var bundled = Path.Combine(Utils.GetBaseDirectory(),"preset-russia");
        var manifest = JsonSerializer.Deserialize<MoskomRouteManifest>(await File.ReadAllTextAsync(Path.Combine(bundled,"manifest.json")),MoskomRouteDownloads.Json)!;
        var stage = Path.Combine(Utils.StartupPath(),"routing-stage-fixture"); Copy(bundled,stage);
        foreach(var name in new[]{"geoip.dat","geosite.dat"})File.Copy(Path.Combine(Utils.GetBinPath(""),name),Path.Combine(stage,name));
        Copy(Path.Combine(Utils.GetBinPath(""),"srss"),Path.Combine(stage,"srss"));
        var updated = manifest with { RoutingCommit = new string('c',40), FetchedAt = DateTimeOffset.UtcNow };
        await File.WriteAllTextAsync(Path.Combine(stage,"manifest.json"),JsonSerializer.Serialize(updated,MoskomRouteDownloads.Json));
        var policies = await AppManager.Instance.RoutingItems(); await MoskomLauncherRuntime.SelectRoutingAsync(policies[1]);
        var previousPolicy = (await ConfigHandler.GetDefaultRouting(config)).Remarks;
        await MoskomLauncherRuntime.ApplyRoutingBundleAsync(stage,updated,true);
        await vm.StatusBarViewModel.RefreshRoutingsMenu();
        var applied = (await AppManager.Instance.RoutingItems()).Count==3 && (await ConfigHandler.GetDefaultRouting(config)).Remarks==previousPolicy && MoskomLauncherRuntime.Preferences.Dns=="cloudflare" && config.SimpleDNSItem.RemoteDNS=="https://1.1.1.1/dns-query" && MoskomLauncherRuntime.CachedRulesDirectory!=null;
        var good = MoskomLauncherRuntime.CachedRulesDirectory!; var bad = Path.Combine(Utils.StartupPath(),"routing-invalid-fixture"); Copy(bundled,bad); await File.WriteAllTextAsync(Path.Combine(bad,"v2ray.json"),"{bad-json");
        var failed = false;
        try { await MoskomLauncherRuntime.ApplyRoutingBundleAsync(bad,updated,true); } catch { failed = true; }
        var rollback = failed && MoskomLauncherRuntime.CachedRulesDirectory==good && (await AppManager.Instance.RoutingItems()).Count==3 && (await ConfigHandler.GetDefaultRouting(config)).Remarks==previousPolicy;
        await MoskomLauncherRuntime.SaveDnsAsync("auto",""); await vm.StatusBarViewModel.RefreshRoutingsMenu();
        var availableVersion = new Version(Version.Parse(MoskomBuildInfo.Version).Major,Version.Parse(MoskomBuildInfo.Version).Minor,Version.Parse(MoskomBuildInfo.Version).Build+1);
        var tag = "moskom-vpn-v"+availableVersion; var file = "MoskomVPN-Setup-"+availableVersion+".exe"; var prefix = "https://github.com/"+MoskomBuildInfo.Repository+"/releases/download/"+tag+"/";
        var body = JsonSerializer.Serialize(new[]{new{tag_name=tag,draft=false,prerelease=true,assets=new[]{new{name=file,browser_download_url=prefix+file,size=128000000},new{name="SHA256SUMS.txt",browser_download_url=prefix+"SHA256SUMS.txt",size=300}}}});
        using var http = new HttpClient(new Handler(body)); MoskomLauncherRuntime.UseSettingsDownloadFixture(http);
        var release = await MoskomLauncherRuntime.CheckApplicationUpdateAsync(true) ?? throw new InvalidOperationException("Fixture update not found.");
        var updateTask = MoskomSettingsViews.ShowUpdateAsync(owner,release); await Task.Delay(150); var updateWindow = Child(owner,"Доступно обновление");
        var prompt = Control<Button>(updateWindow,"MoskomUpdateNow")!=null && Control<Button>(updateWindow,"MoskomUpdateLater")!=null && updateWindow.Title!.Contains(availableVersion.ToString(),StringComparison.Ordinal);
        Capture(updateWindow,output,"update.png"); Control<Button>(updateWindow,"MoskomUpdateLater").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await updateTask;
        var later = await MoskomLauncherRuntime.CheckApplicationUpdateAsync(false)==null && await MoskomLauncherRuntime.CheckApplicationUpdateAsync(true)!=null;
        return new(burger,brand,dns,applied,rollback,prompt,later,help);
    }
}
