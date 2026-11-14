using System.Runtime.InteropServices;
namespace ServiceLib.Services {
 public static class MoskomBuildInfo {public const string Repository="stisvladnext3-creator/moskompremont";}
 public static class MoskomMacBuildInfo {
#if LEGACY
  public const string Flavor="legacy";
#else
  public const string Flavor="modern";
#endif
  public static string Rid=>RuntimeInformation.ProcessArchitecture==Architecture.Arm64?"osx-arm64":"osx-x64";
 }
 public static class Utils {public static string StartupPath()=>Environment.GetEnvironmentVariable("MOSKOM_PLATFORM_HOME")??throw new Exception("Test profile missing");}
 public enum EInboundProtocol {socks}
 public static class AppManager {public static TestApp Instance{get;}=new();public sealed class TestApp {public int GetLocalPort(EInboundProtocol _)=>18989;}}
}
