using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
namespace ServiceLib.Services;
// Only session cookies enter the per-user macOS Keychain. The password is never saved.
public static class MoskomSessionVault
{
    private static string FilePath => Path.Combine(Utils.StartupPath(),"cabinet-session.dat");
    private static string Account => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Utils.StartupPath()))).ToLowerInvariant();
    private sealed record SavedCookie(string Name,string Value,string Path,DateTime Expires,bool Secure,bool HttpOnly);
    public static void Load(MoskomWebsiteClient client)
    {
        if (!File.Exists(FilePath)) return;
        var buffer=new byte[65536];
        try {
            var length=MoskomMacNative.mkr_keychain_load(Account,buffer,buffer.Length);
            if(length<0)throw new InvalidOperationException("Разрешите доступ к сохранённому входу в Связке ключей macOS.");
            if(length==0)return;
            foreach(var cookie in JsonSerializer.Deserialize<List<SavedCookie>>(buffer.AsSpan(0,length))??[])
                if(cookie.Expires==DateTime.MinValue||cookie.Expires.ToUniversalTime()>DateTime.UtcNow)
                    client.Cookies.Add(client.Root,new Cookie(cookie.Name,cookie.Value,cookie.Path){Expires=cookie.Expires,Secure=cookie.Secure,HttpOnly=cookie.HttpOnly});
        } catch(JsonException){Delete();}
        finally{CryptographicOperations.ZeroMemory(buffer);}
    }
    public static void Save(MoskomWebsiteClient client)
    {
        var cookies=client.Cookies.GetCookies(client.Root).Cast<Cookie>().Where(c=>c.Name is "internet_account" or "mkr_remember").Select(c=>new SavedCookie(c.Name,c.Value,c.Path,c.Expires,c.Secure,c.HttpOnly)).ToList();
        var data=JsonSerializer.SerializeToUtf8Bytes(cookies);
        try {
            MoskomMacNative.Check(MoskomMacNative.mkr_keychain_save(Account,data,data.Length));
            Directory.CreateDirectory(Utils.StartupPath());File.WriteAllText(FilePath,"{\"storage\":\"macOS Keychain\",\"version\":1}");File.SetUnixFileMode(FilePath,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        } finally{CryptographicOperations.ZeroMemory(data);}
    }
    public static void Delete(){MoskomMacNative.Check(MoskomMacNative.mkr_keychain_delete(Account));if(File.Exists(FilePath))File.Delete(FilePath);}
}
