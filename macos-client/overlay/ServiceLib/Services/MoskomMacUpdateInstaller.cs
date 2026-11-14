using System.Diagnostics;
using System.Security.Cryptography;
namespace ServiceLib.Services;
public static class MoskomMacUpdateInstaller
{
    private static (string Path,string Sha)? _verified;
    public static void RegisterVerified(string path,string sha)=>_verified=(System.IO.Path.GetFullPath(path),sha);
    public static async Task StartAsync(string path)
    {
        path=System.IO.Path.GetFullPath(path);
        if(_verified is not {} verified||verified.Path!=path)throw new InvalidOperationException("Обновление должно быть загружено и проверено в приложении.");
        await using(var file=File.OpenRead(path)) {
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
            if(hash!=verified.Sha)throw new InvalidOperationException("Загруженное обновление изменилось. Скачайте его заново.");
        }
        var open=new ProcessStartInfo("/usr/bin/open"){UseShellExecute=false};open.ArgumentList.Add(path);
        using var process=Process.Start(open)??throw new InvalidOperationException("Не удалось открыть установочный образ macOS.");
        await process.WaitForExitAsync();if(process.ExitCode!=0)throw new InvalidOperationException("macOS не открыла установочный образ.");
    }
}
