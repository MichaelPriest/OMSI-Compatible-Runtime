namespace OmsiCompat.Plugins;

public enum OmsiPluginBinaryArchitecture
{
    Unknown,
    X86,
    X64,
    Arm64
}

public static class OmsiPluginBinaryInspector
{
    private const ushort DosMagic =
        0x5A4D;
    private const uint PeMagic =
        0x00004550;
    private const ushort MachineX86 =
        0x014C;
    private const ushort MachineX64 =
        0x8664;
    private const ushort MachineArm64 =
        0xAA64;

    public static OmsiPluginBinaryArchitecture Inspect(
        string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    path) ||
                !File.Exists(
                    path))
            {
                return OmsiPluginBinaryArchitecture.Unknown;
            }

            using var stream =
                File.Open(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite |
                    FileShare.Delete);

            using var reader =
                new BinaryReader(
                    stream);

            if (stream.Length <
                    0x40 ||
                reader.ReadUInt16() !=
                    DosMagic)
            {
                return OmsiPluginBinaryArchitecture.Unknown;
            }

            stream.Position =
                0x3C;

            var peOffset =
                reader.ReadInt32();

            if (peOffset <
                    0 ||
                peOffset +
                    6 >
                    stream.Length)
            {
                return OmsiPluginBinaryArchitecture.Unknown;
            }

            stream.Position =
                peOffset;

            if (reader.ReadUInt32() !=
                PeMagic)
            {
                return OmsiPluginBinaryArchitecture.Unknown;
            }

            return reader.ReadUInt16() switch
            {
                MachineX86 =>
                    OmsiPluginBinaryArchitecture.X86,
                MachineX64 =>
                    OmsiPluginBinaryArchitecture.X64,
                MachineArm64 =>
                    OmsiPluginBinaryArchitecture.Arm64,
                _ =>
                    OmsiPluginBinaryArchitecture.Unknown
            };
        }
        catch
        {
            return OmsiPluginBinaryArchitecture.Unknown;
        }
    }
}
