using ServiceLib.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

static class Program {
 [DllImport("libc")]private static extern uint geteuid();
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static Process Spawn(params string[] args){var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};foreach(var a in args)info.ArgumentList.Add(a);return Process.Start(info)!;}
 static async Task Wait(Func<bool> predicate){for(var i=0;i<100;i++){if(predicate())return;await Task.Delay(100);}throw new Exception("Timed out");}
 public static async Task Main(string[] args){
  if(args.Length==3&&args[0]=="--moskom-watch-mac-proxy"){await MoskomMacProxyLease.WatchAsync(int.Parse(args[1]),args[2]);return;}
  if(args.Length==1&&args[0]=="--lease-child"){await new MoskomMacProxyLease().AcquireAsync(CancellationToken.None);File.WriteAllText(Path.Combine(Utils.StartupPath(),"child-ready"),"ready");await Task.Delay(Timeout.Infinite);return;}
  Directory.CreateDirectory(Utils.StartupPath());
  if(args[0]=="--updates"){
   string Release(string flavor,string rid,string version,string prefix="moskom-mac-v"){
    var tag=prefix+version;var file=$"MoskomVPN-macOS-{flavor}-{rid}-{version}.dmg";var root=$"https://github.com/{MoskomBuildInfo.Repository}/releases/download/{tag}/";
    return JsonSerializer.Serialize(new{tag_name=tag,draft=false,assets=new[]{new{name=file,browser_download_url=root+file,size=1000000},new{name="SHA256SUMS.txt",browser_download_url=root+"SHA256SUMS.txt",size=400}}});
   }
   var flat="""{"outbounds":[{"protocol":"vless","settings":{"address":"example.test","port":443,"id":"test-id","encryption":"none","flow":"xtls-rprx-vision"},"streamSettings":{"security":"reality","realitySettings":{"password":"public-key","serverName":"example.test"}}}]}""";
   var compatible=JsonNode.Parse(MoskomLegacyCoreConfig.Xray(flat))!;var vnext=compatible["outbounds"]![0]!["settings"]!["vnext"]![0]!;
   Check(vnext["address"]!.GetValue<string>()=="example.test"&&vnext["users"]![0]!["id"]!.GetValue<string>()=="test-id"&&vnext["users"]![0]!["flow"]!.GetValue<string>()=="xtls-rprx-vision","Legacy VLESS schema changed credentials/transport");
   Check(compatible["outbounds"]![0]!["streamSettings"]!["realitySettings"]!["publicKey"]!.GetValue<string>()=="public-key","Legacy Reality field lost");
   var dns=JsonNode.Parse(MoskomLegacyCoreConfig.Singbox("""{"dns":{"rules":[{"action":"evaluate","server":"remote","tag":"a","domain":["example.test"]},{"action":"respond","match_response":"a"}]}}"""))!;
   Check(dns["dns"]!["rules"]!.AsArray().Count==1&&dns["dns"]!["rules"]![0]!["domain"]![0]!.GetValue<string>()=="example.test","Legacy DNS conversion lost selector");
   var flavor=MoskomMacBuildInfo.Flavor;var rid=MoskomMacBuildInfo.Rid;
   using var json=JsonDocument.Parse("["+string.Join(',',Release(flavor,rid,"0.2.9"),Release(flavor,rid,"0.2.10"),Release(flavor=="legacy"?"modern":"legacy",rid,"0.2.99"),Release(flavor,rid=="osx-x64"?"osx-arm64":"osx-x64","0.2.98"),Release(flavor,rid,"0.2.97","moskom-vpn-v"))+"]");
   var selected=MoskomAppUpdates.SelectRelease(json.RootElement,new Version(0,2,1));Check(selected?.Version==new Version(0,2,10),"Wrong platform/channel/version update");
   Check(MoskomAppUpdates.ParseChecksum(new string('a',64)+"  "+selected!.FileName,selected.FileName)==new string('a',64),"Checksum lookup");
   var name="smoke-"+Guid.NewGuid().ToString("N");var clear=Encoding.UTF8.GetBytes("opaque-test-session");MoskomMacNative.Check(MoskomMacNative.mkr_keychain_save(name,clear,clear.Length));var buffer=new byte[200];var length=MoskomMacNative.mkr_keychain_load(name,buffer,buffer.Length);Check(length==clear.Length&&clear.SequenceEqual(buffer[..length]),"Keychain roundtrip");MoskomMacNative.Check(MoskomMacNative.mkr_keychain_delete(name));Check(MoskomMacNative.mkr_keychain_load(name,buffer,buffer.Length)==0,"Keychain delete");CryptographicOperations.ZeroMemory(clear);CryptographicOperations.ZeroMemory(buffer);Console.WriteLine("PASS macOS update channel, CPU, numeric version and real Keychain storage");return;
  }
  Check(args[0]=="--proxy"&&geteuid()==0,"Real proxy fixture must run only as root on isolated CI runner");
  var root=Utils.StartupPath();var before=Path.Combine(root,"before.json");var after=Path.Combine(root,"after.json");MoskomMacNative.Check(MoskomMacNative.mkr_proxy_snapshot(before));
  try{
   var lease=new MoskomMacProxyLease();await lease.AcquireAsync(CancellationToken.None);Check(File.Exists(Path.Combine(root,"mac-proxy-lease.json")),"Missing proxy journal");await lease.RestoreAsync();
   MoskomMacNative.Check(MoskomMacNative.mkr_proxy_snapshot(after));Check(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(before)),JsonNode.Parse(File.ReadAllText(after))),"Proxy did not restore exactly");
   using var child=Spawn("--lease-child");await Wait(()=>File.Exists(Path.Combine(root,"child-ready")));child.Kill(false);await child.WaitForExitAsync();await Wait(()=>!File.Exists(Path.Combine(root,"mac-proxy-lease.json")));
   MoskomMacNative.Check(MoskomMacNative.mkr_proxy_snapshot(after));Check(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(before)),JsonNode.Parse(File.ReadAllText(after))),"Crash watchdog did not restore proxy");
   Console.WriteLine("PASS real macOS proxy apply/restore and parent crash watchdog");
  }finally{await MoskomMacProxyLease.RecoverAfterCrashAsync();}
 }
}
