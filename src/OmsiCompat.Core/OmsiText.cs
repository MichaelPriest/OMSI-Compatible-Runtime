using System.Text;

namespace OmsiCompat.Core;

public static class OmsiText
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public static string ReadAllText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes =
            File.ReadAllBytes(path);

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var (encoding, preambleLength) =
            DetectEncoding(bytes);

        return encoding.GetString(
            bytes,
            preambleLength,
            bytes.Length - preambleLength);
    }

    public static IReadOnlyList<string> ReadAllLines(
        string path)
    {
        return ReadAllText(path)
            .Replace(
                "
",
                "
",
                StringComparison.Ordinal)
            .Replace(
                '',
                '
')
            .Split('
');
    }

    private static (
        Encoding Encoding,
        int PreambleLength)
        DetectEncoding(
            ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(
                new byte[]
                {
                    0xEF,
                    0xBB,
                    0xBF
                }))
        {
            return (
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: true,
                    throwOnInvalidBytes: true),
                3);
        }

        if (bytes.StartsWith(
                new byte[]
                {
                    0xFF,
                    0xFE
                }))
        {
            return (
                new UnicodeEncoding(
                    bigEndian: false,
                    byteOrderMark: true,
                    throwOnInvalidBytes: true),
                2);
        }

        if (bytes.StartsWith(
                new byte[]
                {
                    0xFE,
                    0xFF
                }))
        {
            return (
                new UnicodeEncoding(
                    bigEndian: true,
                    byteOrderMark: true,
                    throwOnInvalidBytes: true),
                2);
        }

        var utf16Guess =
            GuessUtf16WithoutBom(bytes);

        if (utf16Guess is not null)
        {
            return (
                utf16Guess,
                0);
        }

        try
        {
            _ =
                StrictUtf8.GetString(
                    bytes);

            return (
                StrictUtf8,
                0);
        }
        catch (DecoderFallbackException)
        {
            // OMSI legacy files are commonly distributed as
            // Windows-1252/ANSI. Latin-1 preserves the original
            // single-byte values deterministically even without
            // requiring an external code-page provider.
            return (
                Encoding.Latin1,
                0);
        }
    }

    private static Encoding? GuessUtf16WithoutBom(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8)
        {
            return null;
        }

        var sampleLength =
            Math.Min(
                bytes.Length,
                4096);

        var evenZeroes = 0;
        var oddZeroes = 0;
        var pairs = 0;

        for (var index = 0;
             index + 1 < sampleLength;
             index += 2)
        {
            if (bytes[index] == 0)
            {
                evenZeroes++;
            }

            if (bytes[index + 1] == 0)
            {
                oddZeroes++;
            }

            pairs++;
        }

        if (pairs == 0)
        {
            return null;
        }

        var evenRatio =
            evenZeroes /
            (double)pairs;

        var oddRatio =
            oddZeroes /
            (double)pairs;

        if (oddRatio > 0.30 &&
            evenRatio < 0.10)
        {
            return new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: false,
                throwOnInvalidBytes: true);
        }

        if (evenRatio > 0.30 &&
            oddRatio < 0.10)
        {
            return new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: false,
                throwOnInvalidBytes: true);
        }

        return null;
    }
}
