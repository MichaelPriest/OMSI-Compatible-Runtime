using OmsiCompat.Core;

namespace OmsiCompat.Map;

public sealed record OmsiSectionLine(int LineNumber, string Value);

public sealed record OmsiSection(
    string Name,
    int HeaderLineNumber,
    IReadOnlyList<OmsiSectionLine> Lines);

public sealed class OmsiSectionDocument
{
    private OmsiSectionDocument(IReadOnlyList<OmsiSection> sections)
    {
        Sections = sections;
    }

    public IReadOnlyList<OmsiSection> Sections { get; }

    public static OmsiSectionDocument ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var input = OmsiText.ReadAllLines(path);
        var sections = new List<OmsiSection>();

        string? currentName = null;
        var currentHeaderLine = 0;
        var currentLines = new List<OmsiSectionLine>();

        void Commit()
        {
            if (currentName is null)
            {
                return;
            }

            sections.Add(new OmsiSection(
                currentName,
                currentHeaderLine,
                currentLines.ToArray()));

            currentLines = new List<OmsiSectionLine>();
        }

        for (var index = 0; index < input.Count; index++)
        {
            var raw = input[index];
            var trimmed = raw.Trim();

            if (trimmed.Length >= 3 &&
                trimmed[0] == '[' &&
                trimmed[^1] == ']')
            {
                Commit();
                currentName = trimmed[1..^1].Trim();
                currentHeaderLine = index + 1;
                continue;
            }

            if (currentName is not null)
            {
                currentLines.Add(new OmsiSectionLine(index + 1, raw));
            }
        }

        Commit();
        return new OmsiSectionDocument(sections.ToArray());
    }
}
