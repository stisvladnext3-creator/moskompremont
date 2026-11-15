using System.Text.Json.Nodes;
using ServiceLib.Services;
using Xunit;
public sealed class SelectiveTests {
 private static MoskomServiceCatalog Catalog=>MoskomServiceCatalog.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"service-catalog.json")));
 private static JsonObject Core()=>JsonNode.Parse("""{"outbounds":[{"type":"socks","tag":"proxy","server":"127.0.0.1","server_port":23456},{"type":"direct","tag":"direct"}],"route":{"final":"proxy","rule_set":[{"tag":"fixture","type":"inline","rules":[{"domain_suffix":["example.test"]}]}]},"dns":{"servers":[]}}""")!.AsObject();
 [Fact]public void CatalogContainsExactlyRequested27IncludingCalls(){var c=Catalog;Assert.Equal(27,c.Services.Count);foreach(var id in new[]{"youtube","chatgpt","telegram","whatsapp","flibusta","gemini","kimi","deepseek","perplexity","claude","cursor","instagram","speedtest","discord","viber","snapchat","netflix","spotify","x","facebook","tiktok","playstation","signal","notion","miro","facetime","google-meet"})Assert.Contains(c.Services,s=>s.Id==id);}
 [Fact]public void ServiceModeHasExplicitSelectedDomainsAndDirectFallback(){var original=Core();var config=MoskomSelectivePolicy.Configure(original,new(){Mode="services",Services=["youtube","google-meet"]},Catalog,10808,"MoskomVPN",false);Assert.Equal("direct",config["route"]!["final"]!.GetValue<string>());var rules=config["route"]!["rules"]!.AsArray().ToJsonString();Assert.Contains("youtube.com",rules);Assert.Contains("meet.google.com",rules);Assert.DoesNotContain("chatgpt.com",rules);Assert.DoesNotContain("telegram.org",rules);Assert.Equal("proxy",original["route"]!["final"]!.GetValue<string>());Assert.Null(config["route"]!["rule_set"]);Assert.Contains(config["inbounds"]!.AsArray(),n=>n!["tag"]!.GetValue<string>()=="moskom-probe");}
 [Fact]public void AppModeNeverFallsBackToGlobalProxy(){var config=MoskomSelectivePolicy.Configure(Core(),new(){Mode="apps",Apps=[new("browser","Browser",@"C:\Program Files\Browser\browser.exe")]},Catalog,10808,"MoskomVPN",false);Assert.Equal("direct",config["route"]!["final"]!.GetValue<string>());Assert.Contains(@"browser.exe",config["route"]!["rules"]!.ToJsonString());Assert.True(config["inbounds"]!.AsArray().Any(n=>n!["type"]!.GetValue<string>()=="tun"));}
 [Fact]public void EmptyAndMalformedSelectionsFailBeforeChangingNetwork(){Assert.Throws<InvalidOperationException>(()=>MoskomSelectivePolicy.Configure(Core(),new(){Mode="apps"},Catalog,10808,"MoskomVPN",false));Assert.Throws<InvalidOperationException>(()=>MoskomSelectivePolicy.Configure(Core(),new(){Mode="services",Services=["not-a-service"]},Catalog,10808,"MoskomVPN",false));Assert.Throws<InvalidDataException>(()=>MoskomSelectivePolicy.Configure(Core(),new(){Mode="apps",Apps=[new("x","x","relative.exe")]},Catalog,10808,"MoskomVPN",false));}
 [Fact]public void MacBundlesIncludeHelpersAndFacetimeServicesIncludeCallProcesses(){var app=MoskomSelectivePolicy.Configure(Core(),new(){Mode="apps",Apps=[new("c","Chrome","/Applications/Chrome.app/Contents/MacOS/Chrome","/Applications/Chrome.app")]},Catalog,10808,"utun177",true);Assert.Contains("process_path_regex",app["route"]!["rules"]!.ToJsonString());var calls=MoskomSelectivePolicy.Configure(Core(),new(){Mode="services",Services=["facetime"]},Catalog,10808,"utun178",true);Assert.Contains("avconferenced",calls["route"]!["rules"]!.ToJsonString());}
 [Fact]public void SelectiveDnsDoesNotKeepOldRussiaRoutesOrPrivilegedUserCaches(){
  var source=Core();source["experimental"]=new JsonObject{["cache_file"]=new JsonObject{["path"]="user-cache.db"}};
  source["dns"]=JsonNode.Parse("""{"servers":[{"tag":"direct-dns-1","type":"udp","server":"77.88.8.8"},{"tag":"remote-dns-1","type":"https","server":"1.1.1.1","detour":"proxy"}],"rules":[{"rule_set":["old-russia"],"server":"remote-dns-1"}],"final":"remote-dns-1"}""");
  var services=MoskomSelectivePolicy.Configure(source,new(){Mode="services",Services=["youtube"]},Catalog,10808,"utun177",true);
  Assert.Equal("direct-dns-1",services["dns"]!["final"]!.GetValue<string>());Assert.Contains("youtube.com",services["dns"]!["rules"]!.ToJsonString());Assert.DoesNotContain("old-russia",services.ToJsonString());Assert.Null(services["experimental"]);
  var apps=MoskomSelectivePolicy.Configure(source,new(){Mode="apps",Apps=[new("a","a","/Applications/A.app/Contents/MacOS/A")]},Catalog,10808,"utun177",true);Assert.Equal("remote-dns-1",apps["dns"]!["final"]!.GetValue<string>());
 }

 [Fact]public void DirectSitesUseNetworkDnsWhileSelectedServicesKeepVpnDns(){
  var original=Core();original["dns"]=JsonNode.Parse("""{"servers":[{"tag":"direct-dns-1","type":"udp","server":"77.88.8.8"},{"tag":"local-local","type":"udp","server":"77.88.8.8"},{"tag":"remote-dns-1","type":"https","server":"1.1.1.1","detour":"proxy"}]}""");
  var result=MoskomSelectivePolicy.Configure(original,new(){Mode="services",Services=["youtube"]},Catalog,10808,"MoskomVPN",false,["192.168.88.1"]);
  var dns=result["dns"]!;var servers=dns["servers"]!.AsArray().OfType<JsonObject>().ToList();
  foreach(var tag in new[]{"direct-dns-1","local-local"}){var server=servers.Single(s=>s["tag"]!.GetValue<string>()==tag);Assert.Equal("192.168.88.1",server["server"]!.GetValue<string>());Assert.Null(server["detour"]);}
  var remote=servers.Single(s=>s["tag"]!.GetValue<string>()=="remote-dns-1");Assert.Equal("1.1.1.1",remote["server"]!.GetValue<string>());Assert.Equal("proxy",remote["detour"]!.GetValue<string>());
  Assert.Equal("direct-dns-1",dns["final"]!.GetValue<string>());Assert.Contains("youtube.com",dns["rules"]!.ToJsonString());Assert.DoesNotContain("yandex.ru",dns["rules"]!.ToJsonString());
 }

}
