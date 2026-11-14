global using System.Runtime.InteropServices;
global using System.Runtime.Versioning;
global using System.Diagnostics;
namespace ServiceLib.Services {
 public static class Utils { public static string GetBinPath(string file,string folder)=>Path.Combine(Environment.GetEnvironmentVariable("MOSKOM_TUN_BIN")!,folder,file); }
 public static class Logging { public static void SaveLog(string message)=>Console.Error.WriteLine(message); }
}
