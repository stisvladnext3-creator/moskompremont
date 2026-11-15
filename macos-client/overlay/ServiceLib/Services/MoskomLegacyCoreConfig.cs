using System.Text.Json.Nodes;
namespace ServiceLib.Services;
// v2rayN's UI/config model is current; the macOS 11 cores require their original JSON schemas.
// Never change a server credential or a transport parameter to pretend compatibility.
public static class MoskomLegacyCoreConfig {
 public static string Xray(string text){
  var config=JsonNode.Parse(text)!.AsObject();
  foreach(var outbound in (config["outbounds"] as JsonArray??[]).OfType<JsonObject>()){
   if(outbound["settings"] is not JsonObject settings)continue;var protocol=outbound["protocol"]?.GetValue<string>();
   if(protocol is "vless" or "vmess"&&settings["address"]!=null&&settings["vnext"]==null){
    var server=new JsonObject{["address"]=settings["address"]!.DeepClone(),["port"]=settings["port"]!.DeepClone()};
    var user=(JsonObject)settings.DeepClone();user.Remove("address");user.Remove("port");server["users"]=new JsonArray(user);outbound["settings"]=new JsonObject{["vnext"]=new JsonArray(server)};
   }else if(protocol is "trojan" or "shadowsocks" or "socks" or "http"&&settings["address"]!=null&&settings["servers"]==null){
    var server=(JsonObject)settings.DeepClone();
    if(protocol is "socks" or "http"&&server["user"]!=null){var user=new JsonObject{["user"]=server["user"]!.DeepClone(),["pass"]=server["pass"]?.DeepClone()};server.Remove("user");server.Remove("pass");server["users"]=new JsonArray(user);}
    outbound["settings"]=new JsonObject{["servers"]=new JsonArray(server)};
   }
   if(outbound["streamSettings"]?["realitySettings"] is JsonObject reality&&reality["publicKey"]==null&&reality["password"]!=null){reality["publicKey"]=reality["password"]!.DeepClone();reality.Remove("password");}
  }
  return config.ToJsonString();
 }
 public static string Singbox(string text){
  var config=JsonNode.Parse(text)!.AsObject();
  if(config["dns"] is JsonObject dns){
   dns.Remove("optimistic"); // DNS serve-stale/evaluate/respond belong to the newer engine.
   if(dns["rules"] is JsonArray rules){var compatible=new JsonArray();foreach(var node in rules){if(node is not JsonObject rule)continue;var action=rule["action"]?.GetValue<string>();if(action=="respond"||rule.ContainsKey("match_response"))continue;var copy=(JsonObject)rule.DeepClone();
    if(copy["preferred_by"] is JsonValue preferred){
     var hostServer=(dns["servers"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(server=>server["tag"]?.GetValue<string>()==preferred.GetValue<string>());
     var hosts=(hostServer?["predefined"] as JsonObject)?.Select(pair=>pair.Key).ToArray()??[];
     if(hosts.Length==0)continue;copy.Remove("preferred_by");copy["domain"]=new JsonArray(hosts.Select(host=>(JsonNode?)JsonValue.Create(host)).ToArray());
    }
    if(action=="evaluate"){copy.Remove("action");copy.Remove("tag");copy.Remove("speculative");}compatible.Add(copy);}dns["rules"]=compatible;}
  }
  return config.ToJsonString();
 }
}
