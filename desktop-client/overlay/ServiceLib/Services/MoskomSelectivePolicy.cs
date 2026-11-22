using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net;
using System.Globalization;
namespace ServiceLib.Services;

public sealed record MoskomAppChoice(string Id,string Name,string Path,string? Bundle = null);
public sealed class MoskomSelectiveSettings {
 public string Mode { get; set; } = "";
 public List<MoskomAppChoice> Apps { get; set; } = [];
 public List<string> Services { get; set; } = [];
 public List<string> CustomDomains { get; set; } = [];
}
public sealed class MoskomServiceChoice {
 public string Id {get;set;}="";public string Name{get;set;}="";
 public string[] Domains{get;set;}=[];public string[] Ips{get;set;}=[];
 public string[] MediaPorts{get;set;}=[];public string[] MediaIps{get;set;}=[];
 public string[] MacProcessNames{get;set;}=[];public string[] WinProcessNames{get;set;}=[];public bool Calls{get;set;}
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
 public static List<string> ParseCustomDomains(string text) {
  const string error="Укажите до 200 доменов через пробел, запятую или с новой строки: example.com, *.example.org, full:only.example.com.";
  if(text.Length>16384)throw new InvalidDataException(error);
  var result=new List<string>();var idn=new IdnMapping{UseStd3AsciiRules=true};
  foreach(var token in Regex.Split(text,@"[\s,;]+").Where(t=>t.Length>0)){
   var value=token;var exact=value.StartsWith("full:",StringComparison.OrdinalIgnoreCase);
   if(exact)value=value[5..];else if(value.StartsWith("domain:",StringComparison.OrdinalIgnoreCase))value=value[7..];
   if(value.StartsWith("http://",StringComparison.OrdinalIgnoreCase)||value.StartsWith("https://",StringComparison.OrdinalIgnoreCase)){
    if(!Uri.TryCreate(value,UriKind.Absolute,out var url)||url.UserInfo.Length>0)throw new InvalidDataException(error);value=url.Host;
   }
   if(value.StartsWith("*.",StringComparison.Ordinal))value=value[2..];else if(value.StartsWith('.'))value=value[1..];
   value=value.TrimEnd('.');
   try{value=idn.GetAscii(value).ToLowerInvariant();}catch(ArgumentException){throw new InvalidDataException(error);}
   if(value.Length is <1 or >253||IPAddress.TryParse(value,out _)||!Regex.IsMatch(value,@"\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*\z"))throw new InvalidDataException(error);
   var normalized=(exact?"full:":"domain:")+value;if(!result.Contains(normalized,StringComparer.Ordinal))result.Add(normalized);
   if(result.Count>200)throw new InvalidDataException(error);
  }
  return result;
 }
 public static bool Enabled(MoskomSelectiveSettings settings)=>settings.Mode is "apps" or "services";
 public static bool HasSelection(MoskomSelectiveSettings settings,MoskomServiceCatalog catalog)=>settings.Mode switch {
  "apps"=>settings.Apps.Count>0,"services"=>catalog.Services.Any(s=>settings.Services.Contains(s.Id))||settings.CustomDomains.Count>0,_=>true
 };
 private static JsonArray Strings(IEnumerable<string> values)=>new(values.Distinct(StringComparer.Ordinal).Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());
 public static JsonObject Configure(JsonObject original,MoskomSelectiveSettings settings,MoskomServiceCatalog catalog,int port,string tunName,bool mac,IReadOnlyList<string>? networkDns=null,bool ipv6Available=true) {
  if(!Enabled(settings)||!HasSelection(settings,catalog))throw new InvalidOperationException(settings.Mode=="apps"?"Выберите приложения в меню «Приложения».":"Выберите сервисы в меню «Сервисы».");
  if(port<1024||port>65000||settings.Apps.Count>200||settings.Services.Count>200)throw new InvalidDataException("Некорректные настройки маршрутизации.");
  var config=(JsonObject)original.DeepClone();
  if(config["outbounds"] is not JsonArray outbounds||!outbounds.OfType<JsonObject>().Any(o=>o["tag"]?.GetValue<string>()=="proxy")||!outbounds.OfType<JsonObject>().Any(o=>o["tag"]?.GetValue<string>()=="direct"))throw new InvalidDataException("Ядро не создало нужные подключения.");
  config["inbounds"]=new JsonArray(
   new JsonObject{["type"]="mixed",["tag"]="moskom-local",["listen"]="127.0.0.1",["listen_port"]=port},
   new JsonObject{["type"]="socks",["tag"]="moskom-probe",["listen"]="127.0.0.1",["listen_port"]=port+3},
   new JsonObject{["type"]="tun",["tag"]="moskom-tun",["interface_name"]=tunName,["address"]=Strings(["172.19.0.1/30","fdfe:dcba:9876::1/126"]),["mtu"]=1500,["auto_route"]=true,["strict_route"]=false,["stack"]="system"});
  // Split routing must not install a device-wide DNS firewall. On Windows,
  // strict_route blocks other interfaces' port 53, including local DNS filters
  // and the ordinary resolver used by apps that should stay outside the VPN.
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
   var domains=selected.SelectMany(s=>s.Domains).Concat(ParseCustomDomains(string.Join("\n",settings.CustomDomains))).ToList();
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
   var processes=selected.SelectMany(s=>mac?s.MacProcessNames:s.WinProcessNames).ToArray();
   if(processes.Length>0)rules.Add(new JsonObject{["process_name"]=Strings(processes),["outbound"]="proxy"});
  }
  var route=config["route"] as JsonObject;if(route==null){route=new();config["route"]=route;}route["rules"]=rules;route["final"]="direct";route["auto_detect_interface"]=true;
  route.Remove("rule_set"); // This policy does not use Russia rule sets or their format/version.
  config.Remove("experimental"); // A privileged core must not create root-owned user cache/UI files.
  if(config["dns"] is JsonObject dns){
   dns["reverse_mapping"]=true;
   dns["strategy"]="prefer_ipv4";
   var servers=dns["servers"] as JsonArray;
   if(networkDns!=null){
    var primary=networkDns.FirstOrDefault(address=>IPAddress.TryParse(address,out var ip)&&!ip.Equals(IPAddress.Any)&&!ip.Equals(IPAddress.IPv6Any));
    if(primary==null)throw new InvalidDataException("Не найден DNS текущего сетевого подключения.");
    // Ordinary sites must use the working resolver of the actual connection,
    // rather than a preset's hard-coded public DNS that this ISP may block.
    // Bootstrap lookups must also stay outside the new tunnel.
    if(servers!=null)for(var i=0;i<servers.Count;i++)if(servers[i] is JsonObject server&&server["tag"]?.GetValue<string>() is {} tag&&(tag.StartsWith("direct-dns-",StringComparison.Ordinal)||tag=="local-local"))
     servers[i]=new JsonObject{["type"]="udp",["tag"]=tag,["server"]=primary};
   }
   if(servers?.OfType<JsonObject>().Any(server=>server["tag"]?.GetValue<string>()=="direct-dns-1")==true&&servers.OfType<JsonObject>().Any(server=>server["tag"]?.GetValue<string>()=="remote-dns-1")){
    var dnsRules=new JsonArray();
    if(servers.OfType<JsonObject>().Any(server=>server["tag"]?.GetValue<string>()=="hosts-dns"))dnsRules.Add(new JsonObject{["preferred_by"]="hosts-dns",["server"]="hosts-dns"});
    var hosts=outbounds.OfType<JsonObject>().Select(o=>o["server"]?.GetValue<string>()).Where(host=>!string.IsNullOrEmpty(host)&&!IPAddress.TryParse(host,out _)).Select(host=>host!).ToArray();
    if(hosts.Length>0)dnsRules.Add(new JsonObject{["domain"]=Strings(hosts),["server"]="direct-dns-1"});
    var selectedDomains=new JsonArray();
    if(settings.Mode=="services")foreach(var rule in rules.OfType<JsonObject>().Where(rule=>rule["outbound"]?.GetValue<string>()=="proxy")){
     foreach(var field in new[]{"domain","domain_suffix"})if(rule[field] is JsonArray domains){
      dnsRules.Add(new JsonObject{[field]=domains.DeepClone(),["server"]="remote-dns-1"});
      selectedDomains.Add(new JsonObject{[field]=domains.DeepClone()});
     }
    }
    if(settings.Mode=="services"&&!ipv6Available){
     // TUN acknowledges the browser's IPv6 TCP connection before the direct
     // outbound learns that the physical network has no IPv6 route. A later
     // reset prevents Happy Eyeballs from falling back to IPv4. Do not offer
     // unusable AAAA answers for direct sites; proxy-selected names retain IPv6.
     var conditions=new JsonArray(new JsonObject{["query_type"]=Strings(["AAAA"])});
     if(selectedDomains.Count>0)conditions.Add(new JsonObject{["type"]="logical",["mode"]="or",["rules"]=selectedDomains,["invert"]=true});
     dnsRules.Insert(0,new JsonObject{["type"]="logical",["mode"]="and",["rules"]=conditions,["action"]="predefined",["rcode"]="NOERROR"});
    }
    // OS DNS brokers cannot reliably attribute a query to its originating desktop app.
    dns["final"]=settings.Mode=="apps"?"remote-dns-1":"direct-dns-1";dns["rules"]=dnsRules;
    route["default_domain_resolver"]="direct-dns-1";
   }
  }
  config["log"]=new JsonObject{["level"]="warn",["timestamp"]=true};
  return config;
 }
}
