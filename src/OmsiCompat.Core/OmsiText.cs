using System.Text;

namespace OmsiCompat.Core;

public static class OmsiText
{
    public static string ReadAllText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        // Classic OMSI content is commonly distributed as legacy single-byte text.
        // Latin-1 preserves byte values deterministically until dedicated code-page
        // handling is introduced for individual formats.
        return Encoding.Latin1.GetString(bytes);
    }

    public static IReadOnlyList<string> ReadAllLines(string path)
    {
        return ReadAllText(path)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }
}
