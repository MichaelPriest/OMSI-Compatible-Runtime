using System.Globalization;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OmsiCompat.Scripting;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeOmsiSoundPoint(
    double X,
    double Y);

internal sealed record RuntimeOmsiSoundCurve(
    string Variable,
    IReadOnlyList<RuntimeOmsiSoundPoint> Points);

internal sealed record RuntimeOmsiSoundCondition(
    string Variable,
    double Value,
    int Operator);

internal sealed record RuntimeOmsiSoundDefinition(
    int Id,
    string FilePath,
    bool Loop,
    float BaseVolume,
    int Viewpoint,
    string? Trigger,
    RuntimeOmsiSoundCondition? Condition,
    IReadOnlyList<RuntimeOmsiSoundCurve> VolumeCurves);

internal sealed class RuntimeOmsiAudioHost :
    IDisposable
{
    private sealed class SoundBuilder
    {
        public int Id { get; init; }

        public string FilePath { get; init; } =
            string.Empty;

        public bool Loop { get; init; }

        public float BaseVolume { get; init; } =
            1.0f;

        public int Viewpoint { get; set; }

        public string? Trigger { get; set; }

        public RuntimeOmsiSoundCondition? Condition { get; set; }

        public List<RuntimeOmsiSoundCurve> VolumeCurves { get; } =
            [];

        public RuntimeOmsiSoundDefinition Build() =>
            new(
                Id,
                FilePath,
                Loop,
                BaseVolume,
                Viewpoint,
                Trigger,
                Condition,
                VolumeCurves.ToArray());
    }

    private sealed class LoopVoice :
        IDisposable
    {
        private readonly MixingSampleProvider _mixer;
        private readonly AudioFileReader _reader;

        public LoopVoice(
            MixingSampleProvider mixer,
            AudioFileReader reader,
            VolumeSampleProvider volume)
        {
            _mixer =
                mixer;
            _reader =
                reader;
            Volume =
                volume;
        }

        public VolumeSampleProvider Volume { get; }

        public void Dispose()
        {
            try
            {
                _mixer.RemoveMixerInput(
                    Volume);
            }
            catch
            {
            }

            _reader.Dispose();
        }
    }

    private sealed class LoopingSampleProvider :
        ISampleProvider
    {
        private readonly AudioFileReader _reader;

        public LoopingSampleProvider(
            AudioFileReader reader)
        {
            _reader =
                reader;
        }

        public WaveFormat WaveFormat =>
            _reader.WaveFormat;

        public int Read(
            float[] buffer,
            int offset,
            int count)
        {
            var written =
                0;

            while (written < count)
            {
                var read =
                    _reader.Read(
                        buffer,
                        offset + written,
                        count - written);

                if (read > 0)
                {
                    written +=
                        read;
                    continue;
                }

                if (_reader.Length <= 0)
                {
                    break;
                }

                _reader.Position =
                    0;
            }

            return written;
        }
    }

    private sealed class OwnedSampleProvider :
        ISampleProvider
    {
        private readonly ISampleProvider _source;
        private IDisposable? _owner;

        public OwnedSampleProvider(
            ISampleProvider source,
            IDisposable owner)
        {
            _source =
                source;
            _owner =
                owner;
        }

        public WaveFormat WaveFormat =>
            _source.WaveFormat;

        public int Read(
            float[] buffer,
            int offset,
            int count)
        {
            var read =
                _source.Read(
                    buffer,
                    offset,
                    count);

            if (read == 0)
            {
                _owner?.Dispose();
                _owner =
                    null;
            }

            return read;
        }
    }

    private static readonly WaveFormat OutputFormat =
        WaveFormat.CreateIeeeFloatWaveFormat(
            44100,
            2);

    private readonly IReadOnlyList<RuntimeOmsiSoundDefinition>
        _sounds;
    private readonly MixingSampleProvider _mixer;
    private readonly WaveOutEvent _output;
    private readonly Dictionary<int, LoopVoice>
        _loopVoices =
            [];
    private readonly Dictionary<int, bool>
        _oneShotConditionState =
            [];
    private readonly HashSet<string>
        _reportedFailures =
            new(
                StringComparer.OrdinalIgnoreCase);

    private RuntimeOmsiAudioHost(
        IReadOnlyList<RuntimeOmsiSoundDefinition> sounds,
        MixingSampleProvider mixer,
        WaveOutEvent output)
    {
        _sounds =
            sounds;
        _mixer =
            mixer;
        _output =
            output;
    }

    public int SoundCount =>
        _sounds.Count;

    public int ExistingFileCount =>
        _sounds.Count(
            static sound =>
                File.Exists(
                    sound.FilePath));

    public static RuntimeOmsiAudioHost?
        TryCreate(
            string? soundConfigPath)
    {
        if (string.IsNullOrWhiteSpace(
                soundConfigPath) ||
            !File.Exists(
                soundConfigPath))
        {
            return null;
        }

        try
        {
            var sounds =
                Parse(
                    soundConfigPath);

            if (sounds.Count == 0)
            {
                return null;
            }

            var mixer =
                new MixingSampleProvider(
                    OutputFormat)
                {
                    ReadFully =
                        true
                };

            var output =
                new WaveOutEvent
                {
                    DesiredLatency =
                        100,
                    NumberOfBuffers =
                        3
                };

            output.Init(
                mixer);
            output.Play();

            Console.WriteLine(
                $"[audio] OMSI sound.cfg loaded: {sounds.Count} sound entries.");

            return new RuntimeOmsiAudioHost(
                sounds,
                mixer,
                output);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[audio] OMSI audio unavailable: {ex.Message}");

            return null;
        }
    }

    public void Trigger(
        string trigger,
        OmsiScriptRuntime? scriptRuntime,
        bool interiorView)
    {
        if (string.IsNullOrWhiteSpace(
                trigger))
        {
            return;
        }

        foreach (var sound in
                 _sounds)
        {
            if (sound.Loop ||
                !string.Equals(
                    sound.Trigger,
                    trigger,
                    StringComparison.OrdinalIgnoreCase) ||
                !ViewpointMatches(
                    sound.Viewpoint,
                    interiorView))
            {
                continue;
            }

            var volume =
                EvaluateVolume(
                    sound,
                    scriptRuntime);

            if (volume >
                0.0001f)
            {
                PlayOneShot(
                    sound,
                    volume);
            }
        }
    }

    public void Update(
        OmsiScriptRuntime? scriptRuntime,
        bool interiorView)
    {
        foreach (var sound in
                 _sounds)
        {
            var viewVisible =
                ViewpointMatches(
                    sound.Viewpoint,
                    interiorView);

            var volume =
                viewVisible
                    ? EvaluateVolume(
                        sound,
                        scriptRuntime)
                    : 0.0f;

            if (sound.Loop)
            {
                UpdateLoop(
                    sound,
                    volume);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(
                    sound.Trigger))
            {
                continue;
            }

            var active =
                volume >
                0.0001f;

            var wasActive =
                _oneShotConditionState
                    .TryGetValue(
                        sound.Id,
                        out var previous) &&
                previous;

            if (active &&
                !wasActive)
            {
                PlayOneShot(
                    sound,
                    volume);
            }

            _oneShotConditionState[
                sound.Id] =
                active;
        }
    }

    public void Dispose()
    {
        foreach (var voice in
                 _loopVoices.Values)
        {
            voice.Dispose();
        }

        _loopVoices.Clear();

        try
        {
            _output.Stop();
        }
        catch
        {
        }

        _output.Dispose();
    }

    private void UpdateLoop(
        RuntimeOmsiSoundDefinition sound,
        float volume)
    {
        if (volume <=
            0.0001f)
        {
            if (_loopVoices.TryGetValue(
                    sound.Id,
                    out var muted))
            {
                muted.Volume.Volume =
                    0.0f;
            }

            return;
        }

        if (!_loopVoices.TryGetValue(
                sound.Id,
                out var voice))
        {
            voice =
                TryCreateLoopVoice(
                    sound);

            if (voice is null)
            {
                return;
            }

            _loopVoices[
                sound.Id] =
                voice;
        }

        voice.Volume.Volume =
            volume;
    }

    private LoopVoice? TryCreateLoopVoice(
        RuntimeOmsiSoundDefinition sound)
    {
        if (!File.Exists(
                sound.FilePath))
        {
            ReportFailure(
                sound.FilePath,
                "file not found");
            return null;
        }

        try
        {
            var reader =
                new AudioFileReader(
                    sound.FilePath);

            var looping =
                new LoopingSampleProvider(
                    reader);

            var normalized =
                Normalize(
                    looping);

            if (normalized is null)
            {
                reader.Dispose();
                ReportFailure(
                    sound.FilePath,
                    "unsupported channel layout");
                return null;
            }

            var volume =
                new VolumeSampleProvider(
                    normalized)
                {
                    Volume =
                        0.0f
                };

            _mixer.AddMixerInput(
                volume);

            return new LoopVoice(
                _mixer,
                reader,
                volume);
        }
        catch (Exception ex)
        {
            ReportFailure(
                sound.FilePath,
                ex.Message);
            return null;
        }
    }

    private void PlayOneShot(
        RuntimeOmsiSoundDefinition sound,
        float volume)
    {
        if (!File.Exists(
                sound.FilePath))
        {
            ReportFailure(
                sound.FilePath,
                "file not found");
            return;
        }

        try
        {
            var reader =
                new AudioFileReader(
                    sound.FilePath);

            var normalized =
                Normalize(
                    reader);

            if (normalized is null)
            {
                reader.Dispose();
                ReportFailure(
                    sound.FilePath,
                    "unsupported channel layout");
                return;
            }

            var volumeProvider =
                new VolumeSampleProvider(
                    normalized)
                {
                    Volume =
                        volume
                };

            _mixer.AddMixerInput(
                new OwnedSampleProvider(
                    volumeProvider,
                    reader));
        }
        catch (Exception ex)
        {
            ReportFailure(
                sound.FilePath,
                ex.Message);
        }
    }

    private static ISampleProvider? Normalize(
        ISampleProvider source)
    {
        ISampleProvider provider =
            source;

        if (provider.WaveFormat.Channels ==
            1)
        {
            provider =
                new MonoToStereoSampleProvider(
                    provider);
        }
        else if (provider.WaveFormat.Channels !=
                 2)
        {
            return null;
        }

        if (provider.WaveFormat.SampleRate !=
            OutputFormat.SampleRate)
        {
            provider =
                new WdlResamplingSampleProvider(
                    provider,
                    OutputFormat.SampleRate);
        }

        return provider;
    }

    private float EvaluateVolume(
        RuntimeOmsiSoundDefinition sound,
        OmsiScriptRuntime? scriptRuntime)
    {
        if (!ConditionMatches(
                sound.Condition,
                scriptRuntime))
        {
            return 0.0f;
        }

        var volume =
            Math.Clamp(
                sound.BaseVolume,
                0.0f,
                2.0f);

        foreach (var curve in
                 sound.VolumeCurves)
        {
            if (curve.Variable ==
                "-1")
            {
                continue;
            }

            volume *=
                (float)Math.Max(
                    EvaluateCurve(
                        curve,
                        ResolveVariable(
                            scriptRuntime,
                            curve.Variable)),
                    0.0);
        }

        return Math.Clamp(
            volume,
            0.0f,
            2.0f);
    }

    private static bool ConditionMatches(
        RuntimeOmsiSoundCondition? condition,
        OmsiScriptRuntime? scriptRuntime)
    {
        if (condition is null)
        {
            return true;
        }

        var current =
            ResolveVariable(
                scriptRuntime,
                condition.Variable);

        return condition.Operator switch
        {
            0 =>
                true,
            1 =>
                Math.Abs(
                    current -
                    condition.Value) <
                0.000001,
            2 =>
                current <
                condition.Value,
            3 =>
                current >
                condition.Value,
            4 =>
                current <=
                condition.Value,
            5 =>
                current >=
                condition.Value,
            _ =>
                true
        };
    }

    private static double EvaluateCurve(
        RuntimeOmsiSoundCurve curve,
        double value)
    {
        if (curve.Points.Count == 0)
        {
            return 1.0;
        }

        var points =
            curve.Points
                .OrderBy(
                    static point =>
                        point.X)
                .ToArray();

        if (value <=
            points[0].X)
        {
            return points[0].Y;
        }

        if (value >=
            points[^1].X)
        {
            return points[^1].Y;
        }

        for (var index = 0;
             index + 1 <
                 points.Length;
             index++)
        {
            var left =
                points[index];
            var right =
                points[index + 1];

            if (value <
                    left.X ||
                value >
                    right.X)
            {
                continue;
            }

            var span =
                right.X -
                left.X;

            if (Math.Abs(
                    span) <
                0.000001)
            {
                return right.Y;
            }

            var amount =
                (value -
                 left.X) /
                span;

            return left.Y +
                   (right.Y -
                    left.Y) *
                   amount;
        }

        return 1.0;
    }

    private static double ResolveVariable(
        OmsiScriptRuntime? scriptRuntime,
        string variable)
    {
        if (scriptRuntime is null ||
            string.IsNullOrWhiteSpace(
                variable))
        {
            return 0.0;
        }

        if (variable.Equals(
                "Timegap",
                StringComparison.OrdinalIgnoreCase) ||
            variable.Equals(
                "GetTime",
                StringComparison.OrdinalIgnoreCase))
        {
            return scriptRuntime.GetSystem(
                variable);
        }

        return scriptRuntime.GetLocal(
            variable);
    }

    private static bool ViewpointMatches(
        int viewpoint,
        bool interiorView)
    {
        if (viewpoint is
            <= 0 or >= 7)
        {
            return true;
        }

        var mask =
            interiorView
                ? 2
                : 1;

        return (viewpoint &
                mask) !=
               0;
    }

    private void ReportFailure(
        string path,
        string message)
    {
        var key =
            path +
            "|" +
            message;

        if (!_reportedFailures.Add(
                key))
        {
            return;
        }

        Console.WriteLine(
            $"[audio] {Path.GetFileName(path)}: {message}");
    }

    private static IReadOnlyList<RuntimeOmsiSoundDefinition>
        Parse(
            string configPath)
    {
        var lines =
            File.ReadAllLines(
                configPath);

        var directory =
            Path.GetDirectoryName(
                configPath) ??
            string.Empty;

        var builders =
            new List<SoundBuilder>();

        SoundBuilder? current =
            null;

        List<RuntimeOmsiSoundPoint>?
            activeCurvePoints =
                null;

        for (var index = 0;
             index <
                 lines.Length;
             index++)
        {
            var marker =
                lines[index]
                    .Trim();

            if (!IsMarker(
                    marker))
            {
                continue;
            }

            var section =
                marker[1..^1]
                    .Trim();

            if (section.Equals(
                    "sound",
                    StringComparison.OrdinalIgnoreCase) ||
                section.Equals(
                    "loopsound",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    ReadData(
                        lines,
                        index + 1);

                if (values.Count == 0)
                {
                    current =
                        null;
                    activeCurvePoints =
                        null;
                    continue;
                }

                var declared =
                    values[0]
                        .Trim()
                        .Trim('"');

                if (declared.Length == 0)
                {
                    current =
                        null;
                    activeCurvePoints =
                        null;
                    continue;
                }

                var baseVolume =
                    1.0f;

                foreach (var value in
                         values.Skip(1))
                {
                    if (TryDouble(
                            value,
                            out var parsed))
                    {
                        baseVolume =
                            (float)parsed;
                    }
                }

                var resolved =
                    Path.GetFullPath(
                        Path.Combine(
                            directory,
                            declared
                                .Replace(
                                    '\\',
                                    Path.DirectorySeparatorChar)
                                .Replace(
                                    '/',
                                    Path.DirectorySeparatorChar)));

                current =
                    new SoundBuilder
                    {
                        Id =
                            builders.Count,
                        FilePath =
                            resolved,
                        Loop =
                            section.Equals(
                                "loopsound",
                                StringComparison.OrdinalIgnoreCase),
                        BaseVolume =
                            baseVolume
                    };

                builders.Add(
                    current);

                activeCurvePoints =
                    null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (section.Equals(
                    "viewpoint",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    ReadData(
                        lines,
                        index + 1);

                if (values.Count > 0 &&
                    int.TryParse(
                        values[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var viewpoint))
                {
                    current.Viewpoint =
                        viewpoint;
                }

                activeCurvePoints =
                    null;
                continue;
            }

            if (section.Equals(
                    "trigger",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    ReadData(
                        lines,
                        index + 1);

                current.Trigger =
                    values.Count > 0
                        ? values[0]
                            .Trim()
                            .Trim('"')
                        : null;

                activeCurvePoints =
                    null;
                continue;
            }

            if (section.Equals(
                    "conditionSingle",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    ReadData(
                        lines,
                        index + 1);

                if (values.Count >= 3 &&
                    TryDouble(
                        values[1],
                        out var conditionValue) &&
                    int.TryParse(
                        values[2],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var conditionOperator))
                {
                    current.Condition =
                        new RuntimeOmsiSoundCondition(
                            values[0]
                                .Trim()
                                .Trim('"'),
                            conditionValue,
                            conditionOperator);
                }

                activeCurvePoints =
                    null;
                continue;
            }

            if (section.Equals(
                    "volcurve",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    ReadData(
                        lines,
                        index + 1);

                if (values.Count == 0)
                {
                    activeCurvePoints =
                        null;
                    continue;
                }

                activeCurvePoints =
                    [];

                current.VolumeCurves.Add(
                    new RuntimeOmsiSoundCurve(
                        values[0]
                            .Trim()
                            .Trim('"'),
                        activeCurvePoints));

                continue;
            }

            if (section.Equals(
                    "pnt",
                    StringComparison.OrdinalIgnoreCase) &&
                activeCurvePoints is
                    not null)
            {
                var values =
                    ReadData(
                        lines,
                        index + 1);

                if (values.Count >= 2 &&
                    TryDouble(
                        values[0],
                        out var x) &&
                    TryDouble(
                        values[1],
                        out var y))
                {
                    activeCurvePoints.Add(
                        new RuntimeOmsiSoundPoint(
                            x,
                            y));
                }

                continue;
            }

            if (!section.Equals(
                    "3d",
                    StringComparison.OrdinalIgnoreCase))
            {
                activeCurvePoints =
                    null;
            }
        }

        return builders
            .Select(
                static builder =>
                    builder.Build())
            .Where(
                static sound =>
                    sound.FilePath.Length >
                    0)
            .ToArray();
    }

    private static List<string> ReadData(
        IReadOnlyList<string> lines,
        int start)
    {
        var result =
            new List<string>();

        for (var index =
                 start;
             index <
                 lines.Count;
             index++)
        {
            var value =
                lines[index]
                    .Trim();

            if (IsMarker(
                    value))
            {
                break;
            }

            if (value.Length == 0 ||
                value.StartsWith(
                    '#') ||
                value.StartsWith(
                    "//",
                    StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(
                value);
        }

        return result;
    }

    private static bool IsMarker(
        string value) =>
        value.Length >=
            3 &&
        value[0] ==
            '[' &&
        value[^1] ==
            ']';

    private static bool TryDouble(
        string value,
        out double result) =>
        double.TryParse(
            value
                .Trim()
                .Replace(
                    ',',
                    '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
        double.IsFinite(
            result);
}
