using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace OmsiCompat.Map;

/// <summary>
/// Optional Rust implementation of the global.cfg tile declaration reader.
/// Non-UTF-8 legacy files and unavailable native binaries use the established
/// managed parser; Rust is not treated as authoritative until real-content
/// parity has been validated.
/// </summary>
internal static class OmsiRustGlobalConfigParser
{
    private const int InitialCapacity = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Lazy<bool> NativeAvailable = new(() =>
    {
        try { return CoreAbiVersion() == 1; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    });

    [DllImport("omsi_compat_core", EntryPoint = "omsi_core_abi_version",
        ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint CoreAbiVersion();

    [DllImport("omsi_compat_core", EntryPoint = "omsi_globalcfg_parse_tiles_utf8",
        ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ParseTiles(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string content,
        [Out] byte[] output,
        nuint outputCapacity);

    internal static bool TryReadTileDeclarations(
        string path,
        out IReadOnlyList<OmsiGlobalTileDeclaration> declarations)
    {
        declarations = Array.Empty<OmsiGlobalTileDeclaration>();
        if (!NativeAvailable.Value)
        {
            return false;
        }

        byte[] source;
        try
        {
            source = File.ReadAllBytes(path);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }

        string content;
        try
        {
            var offset = source.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
            content = StrictUtf8.GetString(source, offset, source.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            // Preserve support for legacy ANSI/code-page global.cfg files.
            return false;
        }

        var capacity = Math.Max(InitialCapacity, checked(source.Length * 4 + 1024));
        var buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            var length = ParseTiles(content, buffer, (nuint)buffer.Length);
            if (length < 0 || length >= buffer.Length)
            {
                return false;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, length);
            var parsed = new List<OmsiGlobalTileDeclaration>();
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.TrimEnd('\r').Split('\t');
                if (fields.Length != 4 ||
                    !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                    !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ||
                    !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceLine) ||
                    string.IsNullOrWhiteSpace(fields[2]))
                {
                    return false;
                }

                parsed.Add(new OmsiGlobalTileDeclaration(
                    new OmsiTileCoordinate(x, y),
                    fields[2],
                    sourceLine));
            }

            declarations = parsed;
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
        catch (OverflowException) { return false; }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
