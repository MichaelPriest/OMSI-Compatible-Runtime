using System.Globalization;
using System.Text;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeOmsiFontGlyph(
    char Character,
    int XStart,
    int XEnd,
    int YTop)
{
    public int Width =>
        Math.Max(
            XEnd - XStart,
            0);
}

internal sealed record RuntimeOmsiFontDefinition(
    string Name,
    string? ColorBitmapPath,
    string AlphaBitmapPath,
    int CharacterHeight,
    int CharacterSpacing,
    IReadOnlyDictionary<char, RuntimeOmsiFontGlyph> Glyphs,
    RuntimeOmsiFontGlyph? FallbackGlyph);

internal sealed class RuntimeOmsiTextTextureRenderer :
    IDisposable
{
    private sealed record FontPixels(
        RuntimeOmsiFontDefinition Definition,
        byte[] AlphaPixels,
        int AlphaWidth,
        int AlphaHeight,
        byte[]? ColorPixels,
        int ColorWidth,
        int ColorHeight);

    private sealed class TextTextureState :
        IDisposable
    {
        public string Value { get; set; } =
            string.Empty;

        public RuntimeGpuTexture? Texture
        {
            get;
            set;
        }

        public void Dispose()
        {
            Texture?.Dispose();
            Texture = null;
        }
    }

    private readonly string _fontsDirectory;
    private readonly RuntimeGpuTextureLoader _textureLoader;

    private readonly Dictionary<string, RuntimeOmsiFontDefinition?>
        _fontDefinitions =
            new(
                StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, FontPixels?>
        _fontPixels =
            new(
                StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<int, TextTextureState>
        _states =
            [];

    public RuntimeOmsiTextTextureRenderer(
        string contentRoot,
        RuntimeGpuTextureLoader textureLoader)
    {
        _fontsDirectory =
            Path.Combine(
                contentRoot,
                "Fonts");

        _textureLoader =
            textureLoader;
    }

    public RuntimeGpuTexture?
        GetOrCreate(
            RuntimeVehicleTextTextureInfo definition,
            string value)
    {
        value ??=
            string.Empty;

        if (_states.TryGetValue(
                definition.Index,
                out var state) &&
            string.Equals(
                state.Value,
                value,
                StringComparison.Ordinal) &&
            state.Texture is not null)
        {
            return state.Texture;
        }

        var font =
            GetFontPixels(
                definition.FontName);

        if (font is null)
        {
            return null;
        }

        if (!TryRender(
                definition,
                value,
                font,
                out var rgba))
        {
            return null;
        }

        var texture =
            _textureLoader
                .CreateFromRgba(
                    rgba,
                    definition.Width,
                    definition.Height);

        if (!_states.TryGetValue(
                definition.Index,
                out state))
        {
            state =
                new TextTextureState();

            _states[
                definition.Index] =
                state;
        }

        state.Dispose();
        state.Value =
            value;
        state.Texture =
            texture;

        return texture;
    }

    public void Dispose()
    {
        foreach (var state in
                 _states.Values)
        {
            state.Dispose();
        }

        _states.Clear();
    }

    private FontPixels?
        GetFontPixels(
            string fontName)
    {
        if (_fontPixels.TryGetValue(
                fontName,
                out var cached))
        {
            return cached;
        }

        var definition =
            FindFont(
                fontName);

        if (definition is null ||
            !_textureLoader.TryReadRgba(
                definition.AlphaBitmapPath,
                out var alphaPixels,
                out var alphaWidth,
                out var alphaHeight))
        {
            _fontPixels[
                fontName] =
                null;
            return null;
        }

        byte[]? colorPixels =
            null;

        var colorWidth =
            0;

        var colorHeight =
            0;

        if (!string.IsNullOrWhiteSpace(
                definition.ColorBitmapPath) &&
            File.Exists(
                definition.ColorBitmapPath))
        {
            _textureLoader.TryReadRgba(
                definition.ColorBitmapPath,
                out colorPixels,
                out colorWidth,
                out colorHeight);
        }

        cached =
            new FontPixels(
                definition,
                alphaPixels,
                alphaWidth,
                alphaHeight,
                colorPixels,
                colorWidth,
                colorHeight);

        _fontPixels[
            fontName] =
            cached;

        return cached;
    }

    private RuntimeOmsiFontDefinition?
        FindFont(
            string fontName)
    {
        if (_fontDefinitions.TryGetValue(
                fontName,
                out var cached))
        {
            return cached;
        }

        if (!Directory.Exists(
                _fontsDirectory))
        {
            _fontDefinitions[
                fontName] =
                null;
            return null;
        }

        foreach (var path in
                 Directory.EnumerateFiles(
                     _fontsDirectory,
                     "*.oft",
                     SearchOption.TopDirectoryOnly))
        {
            var definition =
                TryReadFont(
                    path);

            if (definition is null)
            {
                continue;
            }

            _fontDefinitions[
                definition.Name] =
                definition;

            if (string.Equals(
                    definition.Name,
                    fontName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        _fontDefinitions[
            fontName] =
            null;

        return null;
    }

    private static RuntimeOmsiFontDefinition?
        TryReadFont(
            string path)
    {
        try
        {
            var lines =
                File.ReadAllLines(
                    path,
                    Encoding.Latin1);

            string? name =
                null;

            string? colorBitmap =
                null;

            string? alphaBitmap =
                null;

            var characterHeight =
                0;

            var characterSpacing =
                0;

            var glyphs =
                new Dictionary<char, RuntimeOmsiFontGlyph>();

            RuntimeOmsiFontGlyph?
                fallback =
                    null;

            for (var index = 0;
                 index < lines.Length;
                 index++)
            {
                var command =
                    lines[index]
                        .Trim();

                if (command.Equals(
                        "[newfont]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadValue(
                            lines,
                            ref index,
                            out name) ||
                        !TryReadValue(
                            lines,
                            ref index,
                            out var colorFile) ||
                        !TryReadValue(
                            lines,
                            ref index,
                            out var alphaFile) ||
                        !TryReadInt(
                            lines,
                            ref index,
                            out characterHeight) ||
                        !TryReadInt(
                            lines,
                            ref index,
                            out characterSpacing))
                    {
                        return null;
                    }

                    var directory =
                        Path.GetDirectoryName(
                            path) ??
                        string.Empty;

                    colorBitmap =
                        ResolveFontFile(
                            directory,
                            colorFile);

                    alphaBitmap =
                        ResolveFontFile(
                            directory,
                            alphaFile);

                    continue;
                }

                if (!command.Equals(
                        "[char]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryReadValue(
                        lines,
                        ref index,
                        out var characterText) ||
                    characterText.Length == 0 ||
                    !TryReadInt(
                        lines,
                        ref index,
                        out var xStart) ||
                    !TryReadInt(
                        lines,
                        ref index,
                        out var xEnd) ||
                    !TryReadInt(
                        lines,
                        ref index,
                        out var yTop))
                {
                    continue;
                }

                var character =
                    characterText[0];

                var glyph =
                    new RuntimeOmsiFontGlyph(
                        character,
                        xStart,
                        xEnd,
                        yTop);

                glyphs[
                    character] =
                    glyph;

                fallback ??=
                    glyph;
            }

            if (string.IsNullOrWhiteSpace(
                    name) ||
                string.IsNullOrWhiteSpace(
                    alphaBitmap) ||
                characterHeight <= 0 ||
                glyphs.Count == 0)
            {
                return null;
            }

            return new RuntimeOmsiFontDefinition(
                name,
                colorBitmap,
                alphaBitmap,
                characterHeight,
                Math.Max(
                    characterSpacing,
                    0),
                glyphs,
                fallback);
        }
        catch
        {
            return null;
        }
    }

    private static string?
        ResolveFontFile(
            string directory,
            string fileName)
    {
        fileName =
            fileName
                .Trim()
                .Trim('"')
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar)
                .Replace(
                    '\\',
                    Path.DirectorySeparatorChar);

        if (fileName.Length == 0)
        {
            return null;
        }

        var path =
            Path.Combine(
                directory,
                fileName);

        return File.Exists(
                path)
            ? path
            : null;
    }

    private static bool TryReadValue(
        IReadOnlyList<string> lines,
        ref int index,
        out string value)
    {
        while (++index <
               lines.Count)
        {
            var raw =
                lines[index];

            var trimmed =
                raw.Trim();

            if (trimmed.Length == 0 ||
                trimmed.StartsWith(
                    "'",
                    StringComparison.Ordinal) ||
                trimmed.StartsWith(
                    "//",
                    StringComparison.Ordinal) ||
                trimmed.StartsWith(
                    "#",
                    StringComparison.Ordinal))
            {
                continue;
            }

            value =
                trimmed;
            return true;
        }

        value =
            string.Empty;
        return false;
    }

    private static bool TryReadInt(
        IReadOnlyList<string> lines,
        ref int index,
        out int value)
    {
        value =
            0;

        return TryReadValue(
                   lines,
                   ref index,
                   out var text) &&
               int.TryParse(
                   text,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private static bool TryRender(
        RuntimeVehicleTextTextureInfo definition,
        string text,
        FontPixels font,
        out byte[] rgba)
    {
        rgba =
            Array.Empty<byte>();

        if (definition.Width <= 0 ||
            definition.Height <= 0 ||
            definition.Width > 4096 ||
            definition.Height > 4096)
        {
            return false;
        }

        rgba =
            new byte[
                checked(
                    definition.Width *
                    definition.Height *
                    4)];

        var lines =
            text
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n');

        var blockHeight =
            lines.Length *
            font.Definition.CharacterHeight;

        var startY =
            Math.Max(
                (definition.Height -
                 blockHeight) /
                2,
                0);

        for (var lineIndex = 0;
             lineIndex < lines.Length;
             lineIndex++)
        {
            RenderLine(
                rgba,
                definition,
                font,
                lines[lineIndex],
                startY +
                lineIndex *
                font.Definition.CharacterHeight);
        }

        return true;
    }

    private static void RenderLine(
        byte[] destination,
        RuntimeVehicleTextTextureInfo definition,
        FontPixels font,
        string text,
        int targetY)
    {
        var glyphs =
            text
                .Select(
                    character =>
                        ResolveGlyph(
                            font.Definition,
                            character))
                .Where(
                    static glyph =>
                        glyph is not null)
                .Select(
                    static glyph =>
                        glyph!)
                .ToArray();

        if (glyphs.Length == 0)
        {
            return;
        }

        var baseSpacing =
            font.Definition.CharacterSpacing;

        var glyphWidth =
            glyphs.Sum(
                static glyph =>
                    glyph.Width);

        var spacing =
            baseSpacing;

        var alignment =
            definition.Alignment ??
            0;

        if (alignment is >= 3 and <= 5 &&
            glyphs.Length > 1)
        {
            spacing =
                Math.Max(
                    baseSpacing,
                    (definition.Width -
                     glyphWidth) /
                    (glyphs.Length - 1));
        }

        var lineWidth =
            glyphWidth +
            Math.Max(
                glyphs.Length - 1,
                0) *
            spacing;

        var targetX =
            alignment switch
            {
                1 =>
                    0,
                2 =>
                    definition.Width -
                    lineWidth,
                4 =>
                    0,
                5 =>
                    definition.Width -
                    lineWidth,
                _ =>
                    (definition.Width -
                     lineWidth) /
                    2
            };

        targetX =
            Math.Max(
                targetX,
                0);

        foreach (var glyph in
                 glyphs)
        {
            BlitGlyph(
                destination,
                definition,
                font,
                glyph,
                targetX,
                targetY);

            targetX +=
                glyph.Width +
                spacing;

            if (targetX >=
                definition.Width)
            {
                break;
            }
        }
    }

    private static RuntimeOmsiFontGlyph?
        ResolveGlyph(
            RuntimeOmsiFontDefinition font,
            char character)
    {
        if (font.Glyphs.TryGetValue(
                character,
                out var glyph))
        {
            return glyph;
        }

        if (char.IsLower(
                character) &&
            font.Glyphs.TryGetValue(
                char.ToUpperInvariant(
                    character),
                out glyph))
        {
            return glyph;
        }

        if (character == ' ')
        {
            return new RuntimeOmsiFontGlyph(
                ' ',
                0,
                Math.Max(
                    font.CharacterHeight /
                    2,
                    1),
                0);
        }

        return font.FallbackGlyph;
    }

    private static void BlitGlyph(
        byte[] destination,
        RuntimeVehicleTextTextureInfo target,
        FontPixels font,
        RuntimeOmsiFontGlyph glyph,
        int targetX,
        int targetY)
    {
        if (glyph.Character == ' ')
        {
            return;
        }

        var glyphWidth =
            glyph.Width;

        for (var y = 0;
             y < font.Definition.CharacterHeight;
             y++)
        {
            var sourceY =
                glyph.YTop +
                y;

            var destinationY =
                targetY +
                y;

            if (sourceY < 0 ||
                sourceY >=
                    font.AlphaHeight ||
                destinationY < 0 ||
                destinationY >=
                    target.Height)
            {
                continue;
            }

            for (var x = 0;
                 x < glyphWidth;
                 x++)
            {
                var sourceX =
                    glyph.XStart +
                    x;

                var destinationX =
                    targetX +
                    x;

                if (sourceX < 0 ||
                    sourceX >=
                        font.AlphaWidth ||
                    destinationX < 0 ||
                    destinationX >=
                        target.Width)
                {
                    continue;
                }

                var alphaOffset =
                    (sourceY *
                        font.AlphaWidth +
                     sourceX) *
                    4;

                var alpha =
                    (
                        font.AlphaPixels[
                            alphaOffset] +
                        font.AlphaPixels[
                            alphaOffset + 1] +
                        font.AlphaPixels[
                            alphaOffset + 2]
                    ) /
                    3;

                if (alpha <= 0)
                {
                    continue;
                }

                byte red =
                    target.Red;

                byte green =
                    target.Green;

                byte blue =
                    target.Blue;

                if (target.FullColor &&
                    font.ColorPixels is not null &&
                    sourceX <
                        font.ColorWidth &&
                    sourceY <
                        font.ColorHeight)
                {
                    var colorOffset =
                        (sourceY *
                            font.ColorWidth +
                         sourceX) *
                        4;

                    red =
                        font.ColorPixels[
                            colorOffset];

                    green =
                        font.ColorPixels[
                            colorOffset + 1];

                    blue =
                        font.ColorPixels[
                            colorOffset + 2];
                }

                var destinationOffset =
                    (destinationY *
                        target.Width +
                     destinationX) *
                    4;

                destination[
                    destinationOffset] =
                    red;

                destination[
                    destinationOffset + 1] =
                    green;

                destination[
                    destinationOffset + 2] =
                    blue;

                destination[
                    destinationOffset + 3] =
                    (byte)Math.Max(
                        destination[
                            destinationOffset + 3],
                        alpha);
            }
        }
    }
}
