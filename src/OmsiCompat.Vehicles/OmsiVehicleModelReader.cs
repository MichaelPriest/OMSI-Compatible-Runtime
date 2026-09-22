using System.Globalization;
using OmsiCompat.Map;

namespace OmsiCompat.Vehicles;

public static class OmsiVehicleModelReader
{
    public static OmsiVehicleModel ReadFile(string path)
    {
        var document = OmsiSectionDocument.ParseFile(path);
        var meshes = new List<OmsiVehicleMeshReference>();

        string? currentMeshPath = null;
        var currentOrdinal = -1;
        var overrides = new List<OmsiVehicleMaterialOverride>();
        MaterialBuilder? material = null;

        void CommitMaterial()
        {
            if (material is not null)
            {
                overrides.Add(material.Build());
                material = null;
            }
        }

        void CommitMesh()
        {
            CommitMaterial();

            if (!string.IsNullOrWhiteSpace(currentMeshPath))
            {
                meshes.Add(new OmsiVehicleMeshReference(
                    currentOrdinal,
                    currentMeshPath,
                    overrides.ToArray()));
            }

            overrides = new List<OmsiVehicleMaterialOverride>();
        }

        foreach (var section in document.Sections)
        {
            if (section.Name.Equals("mesh", StringComparison.OrdinalIgnoreCase))
            {
                CommitMesh();
                currentOrdinal++;

                currentMeshPath = Values(section).FirstOrDefault();
                if (currentMeshPath is not null)
                {
                    currentMeshPath = currentMeshPath.Trim().Trim('"');
                }

                continue;
            }

            if (currentMeshPath is null)
            {
                continue;
            }

            if (section.Name.Equals("matl", StringComparison.OrdinalIgnoreCase))
            {
                CommitMaterial();
                var values = Values(section).ToArray();

                if (values.Length >= 2 &&
                    int.TryParse(
                        values[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var materialIndex) &&
                    materialIndex >= 0)
                {
                    material = new MaterialBuilder(
                        values[0].Trim().Trim('"'),
                        materialIndex);
                }

                continue;
            }

            if (material is null)
            {
                continue;
            }

            if (section.Name.Equals("matl_alpha", StringComparison.OrdinalIgnoreCase))
            {
                var value = Values(section).FirstOrDefault();
                if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var alphaMode) &&
                    alphaMode is >= 0 and <= 2)
                {
                    material.AlphaMode = alphaMode;
                }

                continue;
            }

            if (section.Name.Equals("matl_transmap", StringComparison.OrdinalIgnoreCase))
            {
                material.TransMapSource = Values(section).FirstOrDefault()?.Trim().Trim('"');
            }
        }

        CommitMesh();

        return new OmsiVehicleModel(path, meshes.ToArray());
    }

    private static IEnumerable<string> Values(OmsiSection section) =>
        section.Lines
            .Select(static line => line.Value.Trim())
            .Where(static value =>
                value.Length > 0 &&
                !value.StartsWith('#') &&
                !value.StartsWith("//", StringComparison.Ordinal));

    private sealed class MaterialBuilder(
        string textureName,
        int materialIndex)
    {
        public string TextureName { get; } = textureName;
        public int MaterialIndex { get; } = materialIndex;
        public int? AlphaMode { get; set; }
        public string? TransMapSource { get; set; }

        public OmsiVehicleMaterialOverride Build() =>
            new(TextureName, MaterialIndex, AlphaMode, TransMapSource);
    }
}
