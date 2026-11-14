using System.Runtime.InteropServices;
namespace ServiceLib.Services;
public static class MoskomMacNative
{
    private const string Library = "MoskomPlatform";
    [DllImport(Library)] private static extern IntPtr mkr_error();
    public static void Check(int result) { if (result != 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(mkr_error()) ?? "Не удалось изменить настройки macOS."); }
    [DllImport(Library)] public static extern int mkr_authorize(int interactive);
    [DllImport(Library)] public static extern int mkr_auth_export([Out] byte[] data);
    [DllImport(Library)] public static extern int mkr_auth_import(byte[] data);
    [DllImport(Library)] public static extern void mkr_auth_release();
    [DllImport(Library)] public static extern int mkr_proxy_prepare([MarshalAs(UnmanagedType.LPUTF8Str)] string path,int port,int pid,[MarshalAs(UnmanagedType.LPUTF8Str)] string started,[MarshalAs(UnmanagedType.LPUTF8Str)] string session);
    [DllImport(Library)] public static extern int mkr_proxy_apply([MarshalAs(UnmanagedType.LPUTF8Str)] string path,[MarshalAs(UnmanagedType.LPUTF8Str)] string session);
    [DllImport(Library)] public static extern int mkr_proxy_restore([MarshalAs(UnmanagedType.LPUTF8Str)] string path,[MarshalAs(UnmanagedType.LPUTF8Str)] string session);
    [DllImport(Library)] public static extern int mkr_proxy_snapshot([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library)] public static extern int mkr_keychain_save([MarshalAs(UnmanagedType.LPUTF8Str)] string account,byte[] data,int length);
    [DllImport(Library)] public static extern int mkr_keychain_load([MarshalAs(UnmanagedType.LPUTF8Str)] string account,[Out] byte[] buffer,int capacity);
    [DllImport(Library)] public static extern int mkr_keychain_delete([MarshalAs(UnmanagedType.LPUTF8Str)] string account);
}
