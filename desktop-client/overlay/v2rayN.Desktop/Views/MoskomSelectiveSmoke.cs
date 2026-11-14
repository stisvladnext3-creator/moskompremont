using ServiceLib.Services;
using Avalonia.VisualTree;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace v2rayN.Desktop.Views;
internal static class MoskomSelectiveSmoke {
 private static T Find<T>(Window owner,string name)where T:Control=>owner.GetVisualDescendants().OfType<T>().First(c=>c.Name==name);
 private static Window Dialog(Window owner,string title)=>((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.First(w=>w!=owner&&w.Title!.StartsWith(title,StringComparison.Ordinal));
 internal static async Task<bool> RunAsync(Window owner,string output){
  var original=JsonSerializer.Deserialize<MoskomSelectiveSettings>(JsonSerializer.Serialize(MoskomLauncherRuntime.Preferences.Selective))!;
  var routing=await ConfigHandler.GetDefaultRouting(AppManager.Instance.Config);var good=true;
  try {
   good&=Find<ComboBox>(owner,"MoskomRouting").Items.Count==5;
   var modal=MoskomSelectiveViews.ServicesAsync(owner);await Task.Delay(150);var dialog=Dialog(owner,"Сервисы");
   good&=dialog.GetVisualDescendants().OfType<CheckBox>().Count(c=>c.Name=="MoskomChoice")==27;
   foreach(var label in new[]{"YouTube","FaceTime","Google Meet"})dialog.GetVisualDescendants().OfType<CheckBox>().First(c=>(string?)c.Content==label).IsChecked=true;
   Find<Button>(dialog,"MoskomSaveServices").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await modal;
   good&=MoskomLauncherRuntime.Preferences.Selective.Services.Order().SequenceEqual(new[]{"facetime","google-meet","youtube"});
   var stored=JsonSerializer.Deserialize<MoskomPreferences>(await File.ReadAllTextAsync(Path.Combine(Utils.StartupPath(),"launcher-settings.json")))!;
   good&=stored.Selective.Services.Count==3;
   var service= new RoutingItem{Id=MoskomSelectivePolicy.ServicesId};await MoskomLauncherRuntime.SelectRoutingAsync(service);
   good&=MoskomLauncherRuntime.SelectiveMode&&MoskomLauncherRuntime.SelectionReady;
   var path=Environment.ProcessPath!;var app=new MoskomAppChoice(path,"МосКомпРемонт · проверка",path);
   await MoskomLauncherRuntime.SaveSelectiveAsync([app],MoskomLauncherRuntime.Preferences.Selective.Services);
   modal=MoskomSelectiveViews.ApplicationsAsync(owner);await Task.Delay(150);dialog=Dialog(owner,"Приложения");
   for(var i=0;i<100&&!Find<Button>(dialog,"MoskomSaveApps").IsEnabled;i++)await Task.Delay(50);
   good&=dialog.GetVisualDescendants().OfType<CheckBox>().Any(c=>(string?)c.Content==app.Name&&c.IsChecked==true);
   Find<Button>(dialog,"MoskomSaveApps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await modal;
   var config=AppManager.Instance.Config;var profile=await ConfigHandler.GetDefaultServer(config);profile.CoreType=ECoreType.sing_box;
   var built=await ServiceLib.Handler.Builder.CoreConfigContextBuilder.Build(config,profile);
   var file=Path.Combine(output,"selective-source.json");var generated=await CoreConfigHandler.GenerateClientConfig(built.Context with{IsTunEnabled=false,RunCoreType=ECoreType.sing_box},file);
   good&=built.Success&&generated.Success;
   var json=JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
   foreach(var mode in new[]{"apps","services"}){
    var settings=new MoskomSelectiveSettings{Mode=mode,Apps=[app],Services=MoskomLauncherRuntime.ServiceCatalog.Services.Select(s=>s.Id).ToList()};
    var next=MoskomSelectivePolicy.Configure(json,settings,MoskomLauncherRuntime.ServiceCatalog,AppManager.Instance.GetLocalPort(EInboundProtocol.socks),OperatingSystem.IsMacOS()?"utun179":"MoskomVPN",OperatingSystem.IsMacOS());
    await File.WriteAllTextAsync(Path.Combine(output,"selective-"+mode+".json"),next.ToJsonString());
   }
   await MoskomLauncherRuntime.SelectRoutingAsync(new(){Id=MoskomSelectivePolicy.AppsId});await MoskomLauncherRuntime.SaveSelectiveAsync([],[]);
   good&=MoskomLauncherRuntime.SelectiveMode&&!MoskomLauncherRuntime.SelectionReady;
   await Task.Delay(150);good&=!Find<Button>(owner,"MoskomPower").IsEnabled;
  } finally {
   await MoskomLauncherRuntime.SelectRoutingAsync(routing);await MoskomLauncherRuntime.SaveSelectiveAsync(original.Apps,original.Services);
  }
  return good;
 }
}
