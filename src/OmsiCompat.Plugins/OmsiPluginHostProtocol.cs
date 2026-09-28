namespace OmsiCompat.Plugins;

public enum OmsiPluginHostCommand : byte
{
    Start = 1,
    Frame = 2,
    Finalize = 3
}

public sealed record OmsiPluginFrame(
    IReadOnlyList<(ushort Index, float Value)> SystemVariables,
    IReadOnlyList<(ushort Index, float Value)> Variables,
    IReadOnlyList<(ushort Index, string Value)> StringVariables,
    IReadOnlyList<ushort> Triggers);

public sealed record OmsiPluginFrameReply(
    IReadOnlyList<float?> SystemVariables,
    IReadOnlyList<float?> Variables,
    IReadOnlyList<string?> StringVariables,
    IReadOnlyList<bool> TriggersActive);
