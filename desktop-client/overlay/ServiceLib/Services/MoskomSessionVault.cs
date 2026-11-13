using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net;

namespace ServiceLib.Services;

// DPAPI binds the saved website session to the current Windows user. No password is saved.
public static class MoskomSessionVault
{
    private static string FilePath => Path.Combine(Utils.StartupPath(), "cabinet-session.dat");
    private sealed record SavedCookie(string Name, string Value, string Path, DateTime Expires, bool Secure, bool HttpOnly);
    public static void Load(MoskomWebsiteClient client)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(FilePath)) return;
        try
        {
            var clear = Transform(File.ReadAllBytes(FilePath), false);
            try
            {
                foreach (var c in JsonSerializer.Deserialize<List<SavedCookie>>(clear) ?? [])
                    if (c.Expires == DateTime.MinValue || c.Expires.ToUniversalTime() > DateTime.UtcNow)
                        client.Cookies.Add(client.Root, new Cookie(c.Name, c.Value, c.Path) { Expires = c.Expires, Secure = c.Secure, HttpOnly = c.HttpOnly });
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch { Delete(); }
    }
    public static void Save(MoskomWebsiteClient client)
    {
        if (!OperatingSystem.IsWindows()) return;
        var cookies = client.Cookies.GetCookies(client.Root).Cast<Cookie>().Where(c => c.Name is "internet_account" or "mkr_remember")
            .Select(c => new SavedCookie(c.Name, c.Value, c.Path, c.Expires, c.Secure, c.HttpOnly)).ToList();
        var clear = JsonSerializer.SerializeToUtf8Bytes(cookies);
        try { var bytes = Transform(clear, true); Directory.CreateDirectory(Utils.StartupPath()); File.WriteAllBytes(FilePath + ".tmp", bytes); File.Move(FilePath + ".tmp", FilePath, true); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public static void Delete() { if (File.Exists(FilePath)) File.Delete(FilePath); }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Не удалось сохранить вход для пользователя Windows.");
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr value);
}
