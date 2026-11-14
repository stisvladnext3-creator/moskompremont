using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace ServiceLib.Services;

public sealed record MoskomAppChoice(string Id,string Name,string Path,string? Bundle = null);
public sealed class MoskomSelectiveSettings {
 public string Mode { get; set; } = "";
 public List<MoskomAppChoice> Apps { get; set; } = [];
 public List<string> Services { get; set; } = [];
}
public sealed class MoskomServiceChoice {
 public string Id {get;set;}="";public string Name{get;set;}="";
 public string[] Domains{get;set;}=[];public string[] Ips{get;set;}=[];
 public string[] MediaPorts{get;set;}=[];public string[] MediaIps{get;set;}=[];
 public string[] MacProcessNames{get;set;}=[];public bool Calls{get;set;}
}
public sealed class MoskomServiceCatalog {
 public int Version{get;set;}public List<MoskomServiceChoice> Services{get;set;}=[];
 public static MoskomServiceCatalog Read(string json) {
  var result=JsonSerializer.Deserialize<MoskomServiceCatalog>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Отсутствует список сервисов.");
  if(result.Version!=1||result.Services.Count>200||result.Services.Select(s=>s.Id).Distinct().Count()!=result.Services.Count)throw new InvalidDataException("Повреждён список сервисов.");
  return result;
 }
}
public static class MoskomSelectivePolicy {
 public const string AppsId="moskom-selected-apps",ServicesId="moskom-selected-services";
 public static bool Enabled(MoskomSelectiveSettings settings)=>settings.Mode is "apps" or "services";
 public static bool HasSelection(MoskomSelectiveSettings settings,MoskomServiceCatalog catalog)=>settings.Mode switch {
  "apps"=>settings.Apps.Count>0,"services"=>catalog.Services.Any(s=>settings.Services.Contains(s.Id)),_=>true
 };
 private static JsonArray Strings(IEnumerable<string> values)=>new(values.Distinct(StringComparer.Ordinal).Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());
 public static JsonObject Configure(JsonObject original,MoskomSelectiveSettings settings,MoskomServiceCatalog catalog,int port,string tunName,bool mac) {
  if(!Enabled(settings)||!HasSelection(settings,catalog))throw new InvalidOperationException(settings.Mode=="apps"?"Выберите приложения в меню «Приложения».":"Выберите сервисы в меню «Сервисы».");
  if(port<1024||port>65000||settings.Apps.Count>200||settings.Services.Count>200)throw new InvalidDataException("Некорректные настройки маршрутизации.");
  var config=(JsonObject)original.DeepClone();
  if(config["outbounds"] is not JsonArray outbounds||!outbounds.OfType<JsonObject>().Any(o=>o["tag"]?.GetValue<string>()=="proxy")||!outbounds.OfType<JsonObject>().Any(o=>o["tag"]?.GetValue<string>()=="direct"))throw new InvalidDataException("Ядро не создало нужные подключения.");
  config["inbounds"]=new JsonArray(
   new JsonObject{["type"]="mixed",["tag"]="moskom-local",["listen"]="127.0.0.1",["listen_port"]=port},
   new JsonObject{["type"]="socks",["tag"]="moskom-probe",["listen"]="127.0.0.1",["listen_port"]=port+3},
   new JsonObject{["type"]="tun",["tag"]="moskom-tun",["interface_name"]=tunName,["address"]=Strings(["172.19.0.1/30","fdfe:dcba:9876::1/126"]),["mtu"]=1500,["auto_route"]=true,["strict_route"]=true,["stack"]="system"});
  var rules=new JsonArray(new JsonObject{["action"]="sniff"},new JsonObject{["protocol"]="dns",["action"]="hijack-dns"},new JsonObject{["inbound"]=Strings(["moskom-probe"]),["outbound"]="proxy"});
  if(settings.Mode=="apps") {
   var paths=new List<string>();var regex=new List<string>();
   foreach(var app in settings.Apps) {
    if(string.IsNullOrEmpty(app.Path)||app.Path.Length>4096||app.Path.Any(char.IsControl)||!(app.Path.StartsWith('/')||Regex.IsMatch(app.Path,@"\A[A-Za-z]:[\\/]")))throw new InvalidDataException("Некорректный путь выбранного приложения.");
    paths.Add(app.Path);
    if(mac&&app.Bundle is {} bundle&&bundle.StartsWith('/')&&bundle.EndsWith(".app",StringComparison.Ordinal)&&!bundle.Any(char.IsControl))regex.Add("^"+Regex.Escape(bundle.TrimEnd('/'))+"/");
   }
   rules.Add(new JsonObject{["process_path"]=Strings(paths),["outbound"]="proxy"});
   if(regex.Count>0)rules.Add(new JsonObject{["process_path_regex"]=Strings(regex),["outbound"]="proxy"});
  } else {
   var selected=catalog.Services.Where(s=>settings.Services.Contains(s.Id)).ToList();
   var domains=selected.SelectMany(s=>s.Domains).ToList();
   var exact=domains.Where(d=>d.StartsWith("full:",StringComparison.Ordinal)).Select(d=>d[5..]).ToList();
   var suffix=domains.Where(d=>d.StartsWith("domain:",StringComparison.Ordinal)).Select(d=>d[7..]).ToList();
   if(exact.Count>0)rules.Add(new JsonObject{["domain"]=Strings(exact),["outbound"]="proxy"});
   if(suffix.Count>0)rules.Add(new JsonObject{["domain_suffix"]=Strings(suffix),["outbound"]="proxy"});
   var ips=selected.SelectMany(s=>s.Ips).ToList();if(ips.Count>0)rules.Add(new JsonObject{["ip_cidr"]=Strings(ips),["outbound"]="proxy"});
   foreach(var service in selected.Where(s=>s.MediaIps.Length>0&&s.MediaPorts.Length>0)){
    var range=service.MediaPorts.Where(p=>p.Contains(':')).ToArray();var ports=service.MediaPorts.Where(p=>!p.Contains(':')).Select(int.Parse).ToArray();
    if(range.Length>0)rules.Add(new JsonObject{["ip_cidr"]=Strings(service.MediaIps),["network"]="udp",["port_range"]=Strings(range),["outbound"]="proxy"});
    if(ports.Length>0)rules.Add(new JsonObject{["ip_cidr"]=Strings(service.MediaIps),["network"]="udp",["port"]=new JsonArray(ports.Select(p=>(JsonNode?)JsonValue.Create(p)).ToArray()),["outbound"]="proxy"});
   }
   if(mac){var processes=selected.SelectMany(s=>s.MacProcessNames).ToArray();if(processes.Length>0)rules.Add(new JsonObject{["process_name"]=Strings(processes),["outbound"]="proxy"});}
  }
  var route=config["route"] as JsonObject;if(route==null){route=new();config["route"]=route;}route["rules"]=rules;route["final"]="direct";route["auto_detect_interface"]=true;
  if(config["dns"] is JsonObject dns){dns["reverse_mapping"]=true;}
  config["log"]=new JsonObject{["level"]="warn",["timestamp"]=true};
  return config;
 }
}
