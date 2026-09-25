using System.Diagnostics;
using System.Globalization;
using System.Numerics;
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
    IReadOnlyList<RuntimeOmsiSoundCondition> Conditions,
    IReadOnlyList<RuntimeOmsiSoundCurve> VolumeCurves,
    string? PitchVariable = null,
    double PitchReferenceValue = 1.0,
    double? SourceX = null,
    double? SourceY = null,
    double? SourceZ = null,
    double? MaximumDistanceMeters = null);

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

        public List<RuntimeOmsiSoundCondition> Conditions { get; } =
            [];

        public string? PitchVariable { get; set; }

        public double PitchReferenceValue { get; set; } =
            1.0;

        public double? SourceX { get; set; }

        public double? SourceY { get; set; }

        public double? SourceZ { get; set; }

        public double? MaximumDistanceMeters { get; set; }

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
                Conditions.ToArray(),
                VolumeCurves.ToArray(),
                PitchVariable,
                PitchReferenceValue,
                SourceX,
                SourceY,
                SourceZ,
                MaximumDistanceMeters);
    }

    private sealed class LoopVoice :
        IDisposable
    {
        private readonly MixingSampleProvider _mixer;
        private readonly AudioFileReader _reader;

        public LoopVoice(
            MixingSampleProvider mixer,
            AudioFileReader reader,
            SmbPitchShiftingSampleProvider pitch,
            StereoPanSampleProvider spatial,
            VolumeSampleProvider volume)
        {
            _mixer =
                mixer;
            _reader =
                reader;
            Pitch =
                pitch;
            Spatial =
                spatial;
            Volume =
                volume;
        }

        public SmbPitchShiftingSampleProvider Pitch { get; }

        public StereoPanSampleProvider Spatial { get; }

        public VolumeSampleProvider Volume { get; }

        public float CurrentPitchFactor { get; set; } =
            1.0f;

        public float CurrentVolume { get; set; }

        public float CurrentBalance { get; set; }

        public float PlaybackSeconds { get; set; }

        public bool WasAudible { get; set; }

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

    private sealed class StereoPanSampleProvider :
        ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float _balance;

        public StereoPanSampleProvider(
            ISampleProvider source)
        {
            _source =
                source;
        }

        public WaveFormat WaveFormat =>
            _source.WaveFormat;

        public float Balance
        {
            get =>
                _balance;
            set =>
                _balance =
                    Math.Clamp(
                        value,
                        -1.0f,
                        1.0f);
        }

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

            if (WaveFormat.Channels != 2 ||
                Math.Abs(
                    _balance) <
                0.0001f)
            {
                return read;
            }

            var leftGain =
                _balance > 0.0f
                    ? 1.0f -
                      _balance
                    : 1.0f;

            var rightGain =
                _balance < 0.0f
                    ? 1.0f +
                      _balance
                    : 1.0f;

            for (var index = offset;
                 index + 1 <
                     offset + read;
                 index += 2)
            {
                buffer[index] *=
                    leftGain;
                buffer[index + 1] *=
                    rightGain;
            }

            return read;
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
    private readonly string _soundDirectory;
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
    private long _lastControlUpdateTimestamp;

    private RuntimeOmsiAudioHost(
        IReadOnlyList<RuntimeOmsiSoundDefinition> sounds,
        string soundDirectory,
        MixingSampleProvider mixer,
        WaveOutEvent output)
    {
        _sounds =
            sounds;
        _soundDirectory =
            soundDirectory;
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
            string? soundConfigPath,
            float masterVolume = 1.0f)
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
                    // Leave enough headroom for map streaming / D3D uploads
                    // without starving the audio callback. OMSI vehicle
                    // loops are long-running voices, so a little extra
                    // latency is preferable to audible buffer underruns.
                    DesiredLatency =
                        180,
                    NumberOfBuffers =
                        4,
                    Volume =
                        Math.Clamp(
                            masterVolume,
                            0.0f,
                            1.0f)
                };

            output.Init(
                mixer);
            output.Play();

            Console.WriteLine(
                $"[audio] OMSI sound.cfg loaded: {sounds.Count} sound entries.");

            var host =
                new RuntimeOmsiAudioHost(
                    sounds,
                    Path.GetDirectoryName(
                        Path.GetFullPath(
                            soundConfigPath)) ??
                        AppContext.BaseDirectory,
                    mixer,
                    output);

            host._lastControlUpdateTimestamp =
                Stopwatch.GetTimestamp();

            return host;
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
        bool interiorView,
        Vector3 listenerPosition,
        Vector3 vehiclePosition,
        float vehicleHeadingRadians)
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
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var viewpointGain =
                EvaluateViewpointGain(
                    sound.Viewpoint,
                    interiorView,
                    scriptRuntime);

            if (viewpointGain <=
                0.0001f)
            {
                continue;
            }

            var spatial =
                EvaluateSpatial(
                    sound,
                    listenerPosition,
                    vehiclePosition,
                    vehicleHeadingRadians);

            var volume =
                EvaluateVolume(
                    sound,
                    scriptRuntime) *
                spatial.Gain *
                viewpointGain;

            if (volume >
                0.0001f)
            {
                PlayOneShot(
                    sound,
                    volume,
                    spatial.Balance);
            }
        }
    }

    public void TriggerFile(
        string trigger,
        string declaredFile,
        bool interiorView)
    {
        if (string.IsNullOrWhiteSpace(
                declaredFile))
        {
            return;
        }

        try
        {
            var normalized =
                declaredFile
                    .Trim()
                    .Trim('"')
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar);

            var candidate =
                Path.IsPathRooted(
                    normalized)
                    ? Path.GetFullPath(
                        normalized)
                    : Path.GetFullPath(
                        Path.Combine(
                            _soundDirectory,
                            normalized));

            if (!File.Exists(
                    candidate) &&
                string.IsNullOrWhiteSpace(
                    Path.GetExtension(
                        candidate)))
            {
                candidate =
                    candidate +
                    ".wav";
            }

            if (!File.Exists(
                    candidate))
            {
                if (_reportedFailures.Add(
                        $"T.F:{trigger}:{candidate}"))
                {
                    Console.WriteLine(
                        $"[audio] dynamic OMSI sound missing: {candidate}");
                }

                return;
            }

            var dynamicSound =
                new RuntimeOmsiSoundDefinition(
                    -1,
                    candidate,
                    false,
                    1.0f,
                    interiorView
                        ? 1
                        : 0,
                    trigger,
                    Array.Empty<RuntimeOmsiSoundCondition>(),
                    Array.Empty<RuntimeOmsiSoundCurve>());

            PlayOneShot(
                dynamicSound,
                1.0f,
                0.0f);
        }
        catch (Exception exception)
        {
            if (_reportedFailures.Add(
                    $"T.F:{trigger}:{declaredFile}"))
            {
                Console.WriteLine(
                    $"[audio] dynamic OMSI sound failed ({declaredFile}): {exception.Message}");
            }
        }
    }

    public void Update(
        OmsiScriptRuntime? scriptRuntime,
        bool interiorView,
        bool engineRunning,
        Vector3 listenerPosition,
        Vector3 vehiclePosition,
        float vehicleHeadingRadians)
    {
        var controlDeltaSeconds =
            ResolveControlDeltaSeconds();

        foreach (var sound in
                 _sounds)
        {
            var viewpointGain =
                EvaluateViewpointGain(
                    sound.Viewpoint,
                    interiorView,
                    scriptRuntime);

            var spatial =
                EvaluateSpatial(
                    sound,
                    listenerPosition,
                    vehiclePosition,
                    vehicleHeadingRadians);

            var volume =
                EvaluateVolume(
                    sound,
                    scriptRuntime) *
                spatial.Gain *
                viewpointGain;

            // OMSI engine loops identify their playback-speed source in the
            // [loopsound] declaration (normally engine_n). Do not allow a
            // stale/init RPM variable to make the engine audibly idle while
            // the host/script engine state is still off. This deliberately
            // does not mute unrelated loops such as doors, electrics or
            // ambient equipment.
            if (!engineRunning &&
                sound.Loop &&
                IsEngineSpeedVariable(
                    sound.PitchVariable) &&
                !HasExplicitEngineStateCondition(
                    sound) &&
                !HasStarterCondition(
                    sound))
            {
                volume =
                    0.0f;
            }

            if (sound.Loop)
            {
                UpdateLoop(
                    sound,
                    volume,
                    EvaluatePitch(
                        sound,
                        scriptRuntime),
                    spatial.Balance,
                    controlDeltaSeconds);
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
                    volume,
                    spatial.Balance);
            }

            _oneShotConditionState[
                sound.Id] =
                active;
        }
    }

    private static bool HasExplicitEngineStateCondition(
        RuntimeOmsiSoundDefinition sound) =>
        sound.Conditions.Any(
            static condition =>
                condition.Variable.Equals(
                    "engine_on",
                    StringComparison.OrdinalIgnoreCase) ||
                condition.Variable.Equals(
                    "engine_injection_on",
                    StringComparison.OrdinalIgnoreCase));

    private static bool HasStarterCondition(
        RuntimeOmsiSoundDefinition sound) =>
        sound.Conditions.Any(
            static condition =>
                condition.Variable.Contains(
                    "starter",
                    StringComparison.OrdinalIgnoreCase) ||
                condition.Variable.Contains(
                    "engine_start",
                    StringComparison.OrdinalIgnoreCase));

    private static bool IsEngineSpeedVariable(
        string? variable)
    {
        if (string.IsNullOrWhiteSpace(
                variable))
        {
            return false;
        }

        var normalized =
            variable.Trim();

        return normalized.Equals(
                   "engine_n",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(
                   "engine_n_",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(
                   "engine_speed",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(
                   "engine_speed_",
                   StringComparison.OrdinalIgnoreCase);
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
        float volume,
        float pitchFactor,
        float balance,
        float deltaSeconds)
    {
        if (!_loopVoices.TryGetValue(
                sound.Id,
                out var voice))
        {
            if (volume <=
                0.0001f)
            {
                return;
            }

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

        var targetPitch =
            Math.Clamp(
                pitchFactor,
                0.25f,
                4.0f);

        var audible =
            volume >
            0.0001f;

        if (audible &&
            !voice.WasAudible)
        {
            voice.PlaybackSeconds =
                0.0f;
        }

        var playbackGain =
            audible
                ? EvaluatePlaybackTimeGain(
                    sound,
                    voice.PlaybackSeconds)
                : 0.0f;

        var targetVolume =
            Math.Clamp(
                volume *
                    playbackGain,
                0.0f,
                1.0f);

        if (audible)
        {
            voice.PlaybackSeconds +=
                deltaSeconds;
        }
        else
        {
            voice.PlaybackSeconds =
                0.0f;
        }

        voice.WasAudible =
            audible;

        var targetBalance =
            Math.Clamp(
                balance,
                -1.0f,
                1.0f);

        voice.CurrentPitchFactor =
            SmoothAudioControl(
                voice.CurrentPitchFactor,
                targetPitch,
                deltaSeconds,
                0.085f);

        voice.CurrentVolume =
            SmoothAudioControl(
                voice.CurrentVolume,
                targetVolume,
                deltaSeconds,
                targetVolume >
                    voice.CurrentVolume
                    ? 0.035f
                    : 0.055f);

        voice.CurrentBalance =
            SmoothAudioControl(
                voice.CurrentBalance,
                targetBalance,
                deltaSeconds,
                0.080f);

        // SmbPitchShiftingSampleProvider is relatively expensive to retune
        // and abrupt per-frame changes can create grainy/chopped output.
        // Update it only after the smoothed value moved materially.
        if (Math.Abs(
                voice.Pitch.PitchFactor -
                voice.CurrentPitchFactor) >
            0.0015f)
        {
            voice.Pitch.PitchFactor =
                voice.CurrentPitchFactor;
        }

        voice.Spatial.Balance =
            voice.CurrentBalance;

        voice.Volume.Volume =
            voice.CurrentVolume;
    }

    private static float EvaluatePlaybackTimeGain(
        RuntimeOmsiSoundDefinition sound,
        float playbackSeconds)
    {
        var playbackCurve =
            sound.VolumeCurves
                .FirstOrDefault(
                    static curve =>
                        curve.Variable ==
                        "-1");

        if (playbackCurve is null)
        {
            return 1.0f;
        }

        return (float)Math.Max(
            EvaluateCurve(
                playbackCurve,
                playbackSeconds),
            0.0);
    }

    private float ResolveControlDeltaSeconds()
    {
        var now =
            Stopwatch.GetTimestamp();

        if (_lastControlUpdateTimestamp <=
            0)
        {
            _lastControlUpdateTimestamp =
                now;
            return 1.0f / 60.0f;
        }

        var elapsed =
            (float)(
                (now -
                 _lastControlUpdateTimestamp) /
                (double)Stopwatch.Frequency);

        _lastControlUpdateTimestamp =
            now;

        return Math.Clamp(
            elapsed,
            1.0f / 240.0f,
            0.1f);
    }

    private static float SmoothAudioControl(
        float current,
        float target,
        float deltaSeconds,
        float timeConstantSeconds)
    {
        var timeConstant =
            Math.Max(
                timeConstantSeconds,
                0.001f);

        var alpha =
            1.0f -
            MathF.Exp(
                -Math.Max(
                    deltaSeconds,
                    0.0f) /
                timeConstant);

        return current +
               (target - current) *
               alpha;
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

            var pitch =
                new SmbPitchShiftingSampleProvider(
                    normalized)
                {
                    PitchFactor =
                        1.0f
                };

            var spatial =
                new StereoPanSampleProvider(
                    pitch);

            var volume =
                new VolumeSampleProvider(
                    spatial)
                {
                    Volume =
                        0.0f
                };

            _mixer.AddMixerInput(
                volume);

            return new LoopVoice(
                _mixer,
                reader,
                pitch,
                spatial,
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
        float volume,
        float balance)
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

            var spatial =
                new StereoPanSampleProvider(
                    normalized)
                {
                    Balance =
                        balance
                };

            var volumeProvider =
                new VolumeSampleProvider(
                    spatial)
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

    private readonly record struct SpatialMix(
        float Gain,
        float Balance);

    private static SpatialMix EvaluateSpatial(
        RuntimeOmsiSoundDefinition sound,
        Vector3 listenerPosition,
        Vector3 vehiclePosition,
        float vehicleHeadingRadians)
    {
        if (!sound.SourceX.HasValue ||
            !sound.SourceY.HasValue ||
            !sound.SourceZ.HasValue)
        {
            return new SpatialMix(
                1.0f,
                0.0f);
        }

        // OMSI vehicle coordinates: X lateral, Y longitudinal, Z vertical.
        // Runtime vehicle space mirrors X and maps longitudinal Y to world Z.
        var localX =
            (float)-sound.SourceX.Value;
        var localZ =
            (float)sound.SourceY.Value;
        var localY =
            (float)sound.SourceZ.Value;

        var sine =
            MathF.Sin(
                vehicleHeadingRadians);
        var cosine =
            MathF.Cos(
                vehicleHeadingRadians);

        var emitter =
            vehiclePosition +
            new Vector3(
                localX *
                    cosine +
                localZ *
                    sine,
                localY,
                -localX *
                    sine +
                localZ *
                    cosine);

        var offset =
            emitter -
            listenerPosition;

        var distance =
            offset.Length();

        var fullVolumeDistance =
            (float)Math.Max(
                sound.MaximumDistanceMeters ??
                0.0,
                0.0);

        var gain =
            fullVolumeDistance <=
                0.001f ||
            distance <=
                fullVolumeDistance
                ? 1.0f
                : Math.Clamp(
                    fullVolumeDistance /
                    Math.Max(
                        distance,
                        0.001f),
                    0.0f,
                    1.0f);

        var horizontal =
            new Vector2(
                offset.X,
                offset.Z);

        var balance =
            0.0f;

        if (horizontal.LengthSquared() >
            0.000001f)
        {
            horizontal =
                Vector2.Normalize(
                    horizontal);

            var vehicleRight =
                new Vector2(
                    cosine,
                    -sine);

            balance =
                Math.Clamp(
                    Vector2.Dot(
                        horizontal,
                        vehicleRight),
                    -1.0f,
                    1.0f);
        }

        return new SpatialMix(
            gain,
            balance);
    }

    private static float EvaluatePitch(
        RuntimeOmsiSoundDefinition sound,
        OmsiScriptRuntime? scriptRuntime)
    {
        if (!sound.Loop ||
            string.IsNullOrWhiteSpace(
                sound.PitchVariable) ||
            sound.PitchVariable == "-1" ||
            !double.IsFinite(
                sound.PitchReferenceValue) ||
            Math.Abs(
                sound.PitchReferenceValue) <
            0.000001)
        {
            return 1.0f;
        }

        var value =
            ResolveVariable(
                scriptRuntime,
                sound.PitchVariable);

        if (!double.IsFinite(
                value))
        {
            return 1.0f;
        }

        return (float)Math.Clamp(
            Math.Abs(
                value /
                sound.PitchReferenceValue),
            0.25,
            4.0);
    }

    private float EvaluateVolume(
        RuntimeOmsiSoundDefinition sound,
        OmsiScriptRuntime? scriptRuntime)
    {
        if (sound.Conditions.Any(
                condition =>
                    !ConditionMatches(
                        condition,
                        scriptRuntime)))
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
            // OMSI SDK conditionSingle comparison codes:
            // 0 <> , 1 = , 2 < , 3 > , 4 <= , 5 >=.
            0 =>
                Math.Abs(
                    current -
                    condition.Value) >=
                0.000001,
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

        // OMSI sound.cfg curves are active only inside the declared
        // [pnt] domain. The stock MAN comments explicitly state that below
        // the lowest X and above the highest X the curve evaluates to zero.
        if (value <
            points[0].X)
        {
            return 0.0;
        }

        if (value >
            points[^1].X)
        {
            return 0.0;
        }

        if (Math.Abs(
                value -
                points[0].X) <
            0.000001)
        {
            return points[0].Y;
        }

        if (Math.Abs(
                value -
                points[^1].X) <
            0.000001)
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

    private static float EvaluateViewpointGain(
        int viewpoint,
        bool interiorView,
        OmsiScriptRuntime? scriptRuntime)
    {
        if (viewpoint is
            <= 0 or >= 7)
        {
            return 1.0f;
        }

        var audibleOutside =
            (viewpoint &
             1) !=
            0;

        var audibleInside =
            (viewpoint &
             2) !=
            0;

        if (!interiorView)
        {
            return audibleOutside
                ? 1.0f
                : 0.0f;
        }

        if (audibleInside)
        {
            return 1.0f;
        }

        if (!audibleOutside)
        {
            return 0.0f;
        }

        // OMSI exposes Snd_OutsideVol specifically so vehicle scripts can
        // control how much exterior sound leaks into the cabin (doors,
        // windows, partitions, etc.). Exterior-only sounds therefore remain
        // audible from an interior camera at the script-defined gain.
        var outsideVolume =
            ResolveVariable(
                scriptRuntime,
                "Snd_OutsideVol");

        return double.IsFinite(
                outsideVolume)
                ? Math.Clamp(
                    (float)outsideVolume,
                    0.0f,
                    1.0f)
                : 0.0f;
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

                var loop =
                    section.Equals(
                        "loopsound",
                        StringComparison.OrdinalIgnoreCase);

                var baseVolume =
                    1.0f;
                string? pitchVariable =
                    null;
                var pitchReference =
                    1.0;

                if (loop)
                {
                    // OMSI SDK [loopsound]:
                    // file, nominal sample rate, pitch variable,
                    // variable value for original pitch, base volume.
                    if (values.Count >= 3)
                    {
                        pitchVariable =
                            values[2]
                                .Trim()
                                .Trim('"');
                    }

                    if (values.Count >= 4 &&
                        TryDouble(
                            values[3],
                            out var parsedReference))
                    {
                        pitchReference =
                            parsedReference;
                    }

                    if (values.Count >= 5 &&
                        TryDouble(
                            values[4],
                            out var parsedVolume))
                    {
                        baseVolume =
                            (float)parsedVolume;
                    }
                }
                else if (values.Count >= 2 &&
                         TryDouble(
                             values[1],
                             out var parsedVolume))
                {
                    baseVolume =
                        (float)parsedVolume;
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
                            loop,
                        BaseVolume =
                            baseVolume,
                        PitchVariable =
                            pitchVariable,
                        PitchReferenceValue =
                            pitchReference
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
                    current.Conditions.Add(
                        new RuntimeOmsiSoundCondition(
                            values[0]
                                .Trim()
                                .Trim('"'),
                            conditionValue,
                            conditionOperator));
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

            if (section.Equals(
                    "3d",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    ReadData(
                        lines,
                        index + 1);

                if (values.Count >= 4 &&
                    TryDouble(
                        values[0],
                        out var sourceX) &&
                    TryDouble(
                        values[1],
                        out var sourceY) &&
                    TryDouble(
                        values[2],
                        out var sourceZ) &&
                    TryDouble(
                        values[3],
                        out var maximumDistance))
                {
                    current.SourceX =
                        sourceX;
                    current.SourceY =
                        sourceY;
                    current.SourceZ =
                        sourceZ;
                    current.MaximumDistanceMeters =
                        Math.Max(
                            maximumDistance,
                            0.0);
                }

                activeCurvePoints =
                    null;
                continue;
            }

            activeCurvePoints =
                null;
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
