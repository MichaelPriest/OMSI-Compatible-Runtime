namespace OmsiCompat.Core;

public sealed record OmsiContentRoot
{
    private OmsiContentRoot(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public string MapsPath => Path.Combine(RootPath, "maps");

    public string VehiclesPath => Path.Combine(RootPath, "Vehicles");

    public string SceneryObjectsPath => Path.Combine(RootPath, "Sceneryobjects");

    public string SplinesPath => Path.Combine(RootPath, "Splines");

    public string TexturesPath => Path.Combine(RootPath, "Texture");

    public static bool TryCreate(string? path, out OmsiContentRoot? contentRoot, out string error)
    {
        contentRoot = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Content path is empty.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid content path: {ex.Message}";
            return false;
        }

        if (!Directory.Exists(fullPath))
        {
            error = $"Content directory does not exist: {fullPath}";
            return false;
        }

        var mapsPath = Path.Combine(fullPath, "maps");
        if (!Directory.Exists(mapsPath))
        {
            error = $"The directory does not look like OMSI-compatible content because 'maps' was not found: {fullPath}";
            return false;
        }

        contentRoot = new OmsiContentRoot(fullPath);
        error = string.Empty;
        return true;
    }
}
