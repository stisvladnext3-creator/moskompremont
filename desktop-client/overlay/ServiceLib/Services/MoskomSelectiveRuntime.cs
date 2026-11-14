using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ServiceLib.Handler.Builder;
namespace ServiceLib.Services;

public static partial class MoskomLauncherRuntime {
 public static bool SelectiveMode=>MoskomSelectivePolicy.Enabled(Preferences.Selective);
 public static MoskomServiceCatalog ServiceCatalog=>MoskomServiceCatalog.Read(File.ReadAllText(Path.Combine(Utils.GetBaseDirectory(),"service-catalog.json")));
 public static bool SelectionReady=>!SelectiveMode||MoskomSelectivePolicy.HasSelection(Preferences.Selective,ServiceCatalog);
 public static Func<Task<List<MoskomAppChoice>>>? MacApplications {get;set;}
 public static async Task<List<MoskomAppChoice>> RunningApplicationsAsync(){
  if(OperatingSystem.IsMacOS())return MacApplications==null?[]:await MacApplications();
  return await Task.Run(()=>{
   var rows=new Dictionary<string,MoskomAppChoice>(StringComparer.OrdinalIgnoreCase);var session=Process.GetCurrentProcess().SessionId;
   foreach(var process in Process.GetProcesses())using(process){try{
    if(process.Id==Environment.ProcessId||process.SessionId!=session)continue;
    var path=process.MainModule?.FileName;if(string.IsNullOrEmpty(path))continue;
    var name=FileVersionInfo.GetVersionInfo(path).ProductName;if(string.IsNullOrWhiteSpace(name))name=process.ProcessName;
    rows.TryAdd(path,new(path,name,path));
   }catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException){}}
   return rows.Values.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
  });
 }
 public static List<RoutingItem> RoutingChoices(IEnumerable<RoutingItem> original)=>[..original,new RoutingItem{Id=MoskomSelectivePolicy.AppsId,Remarks="Выбранные приложения"},new RoutingItem{Id=MoskomSelectivePolicy.ServicesId,Remarks="Выбранные сервисы"}];
 public static string? CustomRoutingId=>Preferences.Selective.Mode switch{"apps"=>MoskomSelectivePolicy.AppsId,"services"=>MoskomSelectivePolicy.ServicesId,_=>null};
 public static async Task SaveSelectiveAsync(IEnumerable<MoskomAppChoice> apps,IEnumerable<string> services){
  var nextApps=apps.DistinctBy(a=>a.Id).Take(201).ToList();var nextServices=services.Distinct().Take(201).ToList();
  if(nextApps.Count>200||nextServices.Count>200||nextServices.Any(id=>!ServiceCatalog.Services.Any(s=>s.Id==id)))throw new InvalidOperationException("Некорректный выбор маршрутизации.");
  if(!await Operations.WaitAsync(0))throw new InvalidOperationException("Дождитесь завершения текущего действия.");
  var before=Preferences.Selective;var wasOn=Connection?.IsConnected==true&&SelectiveMode;
  try{
   if(wasOn)await Connection!.DisconnectAsync();
   Preferences.Selective=new(){Mode=before.Mode,Apps=nextApps,Services=nextServices};
   try{SavePreferences();}catch{Preferences.Selective=before;throw;}
   SettingsChanged?.Invoke();ListsChanged?.Invoke();if(wasOn&&SelectionReady)await Connection!.ConnectAsync();
  }finally{Operations.Release();}
 }
 private sealed class SelectiveCore : IAsyncDisposable {
  private MoskomTunSupervisor? _supervisor;public bool HasExited=>_supervisor?.HasExited!=false;
  public async Task StartAsync(ProfileItem profile,CancellationToken ct){
   if(!SelectionReady)throw new InvalidOperationException(Preferences.Selective.Mode=="apps"?"Сначала выберите приложения в меню ☰.":"Сначала выберите сервисы в меню ☰.");
   var copy=JsonUtils.Deserialize<ProfileItem>(JsonUtils.Serialize(profile))??throw new InvalidDataException("Не прочитано подключение.");copy.CoreType=ECoreType.sing_box;
   var built=await CoreConfigContextBuilder.Build(AppManager.Instance.Config,copy);if(!built.Success)throw new InvalidOperationException("Этот сервер не поддерживает режим TUN. Выберите другое подключение.");
   var directory=Path.Combine(Utils.GetTempPath(),"selective");Directory.CreateDirectory(directory);var source=Path.Combine(directory,"source.json");
   var result=await CoreConfigHandler.GenerateClientConfig(built.Context with{IsTunEnabled=false,RunCoreType=ECoreType.sing_box},source);if(!result.Success)throw new InvalidOperationException("Не удалось подготовить конфигурацию TUN.");
   var json=JsonNode.Parse(await File.ReadAllTextAsync(source,ct)) as JsonObject??throw new InvalidDataException("Не прочитана конфигурация.");
   var port=AppManager.Instance.GetLocalPort(EInboundProtocol.socks);var tun=OperatingSystem.IsMacOS()?"utun"+System.Security.Cryptography.RandomNumberGenerator.GetInt32(100,200):"MoskomVPN";
   var config=MoskomSelectivePolicy.Configure(json,Preferences.Selective,ServiceCatalog,port,tun,OperatingSystem.IsMacOS());
   _supervisor=await MoskomTunSupervisor.StartAsync(JsonSerializer.SerializeToUtf8Bytes(config),ct);
   for(var attempt=0;attempt<50;attempt++){ct.ThrowIfCancellationRequested();if(HasExited)throw new InvalidOperationException("Ядро TUN завершилось. Проверьте разрешение администратора и выбранный сервер.");if(await HasSocksAsync(port+3,ct))return;await Task.Delay(200,ct);}
   throw new InvalidOperationException("Не запущен локальный вход TUN.");
  }
  public async ValueTask DisposeAsync(){if(_supervisor!=null){await _supervisor.DisposeAsync();_supervisor=null;}}
 }
}
