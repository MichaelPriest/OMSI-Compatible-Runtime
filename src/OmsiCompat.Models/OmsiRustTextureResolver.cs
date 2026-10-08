using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmsiCompat.Models;

/// <summary>
/// Versioned native Rust compatibility core. The managed path resolver is
/// retained as an explicit fallback during the staged migration.
/// </summary>
internal static class OmsiRustTextureResolver
{
    private const int BufferCapacity = 32768;
    private static readonly Lazy<bool> NativeAvailable = new(
        static () =>
        {
            try
            {
                return CoreAbiVersion() == 1;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (BadImageFormatException) { return false; }
        });

    private static long _nativeResolvedCount;

    internal static bool IsAvailable => NativeAvailable.Value;
    internal static long NativeResolvedCount =>
        Interlocked.Read(ref _nativeResolvedCount);

    [DllImport(
        "omsi_compat_core",
        EntryPoint = "omsi_core_abi_version",
        ExactSpelling = true,
        CallingConvention = CallingConvention.Cdecl)]
    private static extern uint CoreAbiVersion();

    [DllImport(
        "omsi_compat_core",
        EntryPoint = "omsi_texture_resolve_utf8",
        ExactSpelling = true,
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int ResolveTexture(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string allowedRoot,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string textureName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string baseDirectories,
        [Out] byte[] output,
        nuint outputCapacity);

    internal static bool TryResolve(
        string allowedRoot,
        string textureName,
        IReadOnlyList<string> baseDirectories,
        out string path)
    {
        path = string.Empty;

        if (!IsAvailable)
        {
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferCapacity);
        try
        {
            // Unit separator cannot appear in Windows path components.
            var directories = string.Join('\u001f', baseDirectories);
            var length = ResolveTexture(
                allowedRoot,
                textureName,
                directories,
                buffer,
                (nuint)buffer.Length);
            if (length <= 0 || length >= buffer.Length)
            {
                return false;
            }

            var candidate = Encoding.UTF8.GetString(buffer, 0, length);
            if (!Path.IsPathFullyQualified(candidate) ||
                !File.Exists(candidate))
            {
                return false;
            }

            path = candidate;
            Interlocked.Increment(ref _nativeResolvedCount);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
