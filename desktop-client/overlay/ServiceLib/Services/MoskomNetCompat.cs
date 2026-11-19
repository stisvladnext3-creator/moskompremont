using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Services
{

// APIs used by the launcher that were added after .NET 6. The Windows 8
// channel compiles this file into both ServiceLib and the standalone TUN test.
public static class MoskomNetCompat
{
    public static DirectoryInfo CreateDirectory(string path)
    {
#if NET7_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            return Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
#endif
        return Directory.CreateDirectory(path);
    }

    public static void SetPrivateUnixFileMode(string path)
    {
#if NET7_0_OR_GREATER
        if (!OperatingSystem.IsWindows() && File.Exists(path))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
#endif
    }

    public static FileStreamOptions PrivateFileStreamOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
#if NET7_0_OR_GREATER
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
#endif
        return options;
    }

    public static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        CreateDirectory(path);
        return path;
    }
}

}

namespace System.IO
{

public static class MoskomStreamCompat
{
    public static async ValueTask ReadExactlyAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}

}

namespace System.Text.Json.Nodes
{

internal static class MoskomJsonCompat
{
    public static JsonNode? DeepClone(this JsonNode node) => JsonNode.Parse(node.ToJsonString());
}
}
