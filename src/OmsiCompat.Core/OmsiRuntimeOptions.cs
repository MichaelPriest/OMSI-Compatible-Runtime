using System.Globalization;
using System.Text.Json;

namespace OmsiCompat.Core;

public sealed class OmsiRuntimeOptions
{
    public string Language { get; set; } = "PTBR";
    public int TicketSalesMode { get; set; } = 0;
    public string InternetRadioUrl { get; set; } = "";
    public bool SmoothDriverViewTransitions { get; set; } = true;
    public bool DriverHeadMovement { get; set; } = true;
    public bool VehicleLandscapeCollisions { get; set; } = true;
    public bool TerrainCollisions { get; set; } = true;
    public bool VehicleToVehicleCollisions { get; set; } = true;
    public bool UserVehiclePedestrianCollisions { get; set; } = true;
    public bool AutomaticSteeringCenter { get; set; }
    public bool SeeOwnDriver { get; set; } = true;
    public bool ShowTicketPassengerInfo { get; set; } = true;
    public bool UseCurrentTime { get; set; }
    public bool UseCurrentDate { get; set; }
    public bool UseCurrentYear { get; set; }
    public int WearLifespan { get; set; } = 4;
    public bool AutomaticClutch { get; set; } = true;
    public bool ScheduleAnalysisPopup { get; set; } = true;
    public bool AlternativeView { get; set; }
    public bool ReducedSteeringSpeed { get; set; }
    public bool ShowVehiclePreview { get; set; } = true;
    public string TypewriterFont { get; set; } = "Courier New";
    public bool GameControllerEnabled { get; set; }

    public bool ReducedMultithreading { get; set; }
    public bool LoadWholeMapAtStart { get; set; }
    public bool AutoSave { get; set; } = true;
    public bool ShowErrorMessages { get; set; }

    public int TargetFps { get; set; } = 60;
    public int NeighborTiles { get; set; } = 2;
    public double MaximumObjectVisibilityMeters { get; set; } = 1000;
    public double MinimumObjectSize { get; set; } = 0.01;
    public double MinimumObjectSizeReflections { get; set; } = 0.04;
    public string RealTimeReflections { get; set; } = "economy";
    public bool ParticleSystems { get; set; } = true;
    public int MaximumParticlesPerEmitter { get; set; } = 1000;
    public bool ParticleOnlyOwnVehicle { get; set; }
    public bool ParticleSystemsInReflections { get; set; } = true;
    public bool SunGlow { get; set; } = true;
    public int MaximumObjectComplexity { get; set; } = 3;
    public int MaximumMapComplexity { get; set; } = 3;
    public bool StencilBufferEffects { get; set; }
    public bool Shadows { get; set; }
    public bool RainReflections { get; set; }
    public bool HumansInRainReflections { get; set; }

    public double ReflectionEconomyBelowFps { get; set; } = 20;
    public double ReflectionFullAboveFps { get; set; } = 40;
    public double ReduceTilesBelowFps { get; set; } = 20;
    public double IncreaseTilesAboveFps { get; set; } = 25;
    public bool ManualAspectRatioEnabled { get; set; }
    public double ManualAspectRatio { get; set; } = 16.0 / 9.0;
    public bool OnlyLowResolutionTextures { get; set; }
    public bool LimitTexturesTo256 { get; set; }
    public bool LowResolutionTexturesAtDistance { get; set; } = true;
    public double HighResolutionTextureMemoryMb { get; set; } = 2048;
    public int RealTimeReflectionTextureSize { get; set; } = 512;
    public bool MaterialNightMap { get; set; } = true;
    public bool MaterialLightMap { get; set; } = true;
    public bool MaterialTerrainLightMap { get; set; } = true;
    public bool MaterialReflectionMap { get; set; } = true;
    public bool MaterialBumpMap { get; set; } = true;
    public int TextureFilterMode { get; set; } = 3;
    public int AnisotropicFiltering { get; set; } = 16;

    public int MasterVolumePercent { get; set; } = 60;
    public int StereoEffect { get; set; } = 17;
    public int MaximumSoundCount { get; set; } = 400;
    public bool DopplerEffect { get; set; }
    public bool AiVehicleSounds { get; set; } = true;
    public bool ScenerySounds { get; set; } = true;
    public bool ReverbEffects { get; set; }

    public int MaximumUnscheduledTraffic { get; set; } = 100;
    public int RoadTrafficFactorPercent { get; set; } = 50;
    public int ParkedCarsPercent { get; set; } = 40;
    public int MaximumPeople { get; set; } = 150;
    public int PassengerFactorPercent { get; set; } = 100;
    public int MaximumScheduledTraffic { get; set; } = 20;
    public int ScheduledTrafficPriority { get; set; } = 2;
    public bool UseReducedAiList { get; set; }

    public bool RuntimeVSync { get; set; } = true;
    public bool RuntimeBorderlessFullscreen { get; set; }
    public int RuntimeStreamingRadius { get; set; } = 2;
    public bool RuntimePreferHardwareGpu { get; set; } = true;
    public bool RuntimeShowFps { get; set; }
    public bool RuntimeDiagnostics { get; set; } = true;

    public static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "OMSI-Compatible-Runtime");

    public static string SettingsPath =>
        Path.Combine(
            SettingsDirectory,
            "runtime-options.json");

    public static OmsiRuntimeOptions Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new OmsiRuntimeOptions();
            }

            return JsonSerializer.Deserialize<OmsiRuntimeOptions>(
                       File.ReadAllText(SettingsPath))
                   ?? new OmsiRuntimeOptions();
        }
        catch
        {
            return new OmsiRuntimeOptions();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(
            SettingsDirectory);

        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(
                this,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }

    public static OmsiRuntimeOptions ImportFromOmsi(
        string contentRoot,
        OmsiRuntimeOptions? baseline = null) =>
        ImportFromFile(
            Path.Combine(
                contentRoot,
                "options.cfg"),
            baseline);

    public static OmsiRuntimeOptions ImportFromFile(
        string path,
        OmsiRuntimeOptions? baseline = null)
    {
        var options =
            baseline is null
                ? new OmsiRuntimeOptions()
                : Clone(baseline);

        if (!File.Exists(path))
        {
            return options;
        }

        var cfg =
            ParseConfig(
                File.ReadAllLines(path));

        options.Language =
            Text(cfg, "language") ??
            options.Language;
        options.TicketSalesMode =
            Integer(cfg, "ticketselling") ??
            options.TicketSalesMode;
        options.InternetRadioUrl =
            Text(cfg, "radio") ??
            options.InternetRadioUrl;
        options.SmoothDriverViewTransitions =
            Present(cfg, "driverview_smooth");
        options.DriverHeadMovement =
            Present(cfg, "driverview_moving");
        options.VehicleLandscapeCollisions =
            !Present(cfg, "no_collision");
        options.TerrainCollisions =
            !Present(cfg, "no_collision_terrain");
        options.VehicleToVehicleCollisions =
            !Present(cfg, "no_collision_vehToVeh");
        options.UserVehiclePedestrianCollisions =
            !Present(cfg, "no_collision_pedastrians");
        options.AutomaticSteeringCenter =
            Present(cfg, "autoCenter");
        options.SeeOwnDriver =
            Present(cfg, "see_own_driver");
        options.ShowTicketPassengerInfo =
            !Present(cfg, "no_ticketinfo_visible");
        options.UseCurrentTime =
            Present(cfg, "useActTime");
        options.UseCurrentDate =
            Present(cfg, "useActDate");
        options.UseCurrentYear =
            Present(cfg, "useActYear");
        options.WearLifespan =
            Integer(cfg, "wear_lifespan") ??
            options.WearLifespan;
        options.AutomaticClutch =
            !Present(cfg, "no_automaticClutch");
        options.ScheduleAnalysisPopup =
            !Present(cfg, "no_schedAnaPopUp");
        options.AlternativeView =
            Present(cfg, "altView");
        options.ReducedSteeringSpeed =
            Present(cfg, "redSteerSpd");
        options.ShowVehiclePreview =
            !Present(cfg, "nopreview");
        options.TypewriterFont =
            Text(cfg, "font_typewriter") ??
            options.TypewriterFont;
        options.GameControllerEnabled =
            Present(cfg, "gamectrleron");

        options.ReducedMultithreading =
            Present(cfg, "no_multithreading_calculate") ||
            Present(cfg, "no_multithreading_texload") ||
            Present(cfg, "reducedMultithreading") ||
            Present(cfg, "reduceMultithreading");
        options.LoadWholeMapAtStart =
            Present(cfg, "loadAllTiles");
        options.AutoSave =
            !Present(cfg, "noAutoSave");
        options.ShowErrorMessages =
            Present(cfg, "showerrormessages");

        options.RealTimeReflections =
            Text(cfg, "performance_realreflexions") ??
            options.RealTimeReflections;
        options.NeighborTiles =
            Integer(cfg, "performance_tiledistmax") ??
            options.NeighborTiles;
        options.MaximumObjectVisibilityMeters =
            Number(cfg, "performance_maxObjDist") ??
            options.MaximumObjectVisibilityMeters;
        options.MinimumObjectSize =
            Number(cfg, "performance_minObjSize") ??
            options.MinimumObjectSize;
        options.MinimumObjectSizeReflections =
            Number(cfg, "performance_minObjSizeRefl") ??
            options.MinimumObjectSizeReflections;
        options.MaximumObjectComplexity =
            Integer(cfg, "maxcomplexity") ??
            options.MaximumObjectComplexity;
        options.MaximumMapComplexity =
            Integer(cfg, "maxcomplexity_map") ??
            options.MaximumMapComplexity;
        options.TargetFps =
            Integer(cfg, "maxFPS") ??
            options.TargetFps;
        options.StencilBufferEffects =
            !Present(cfg, "no_stencilbuffer");
        options.RainReflections =
            !Present(cfg, "no_rain_refl");
        options.HumansInRainReflections =
            !Present(cfg, "no_humans_on_rain_refl");
        options.SunGlow =
            Present(cfg, "sunglow");
        options.Shadows =
            !string.Equals(
                Text(cfg, "shadow_stencil"),
                "off",
                StringComparison.OrdinalIgnoreCase);

        var smoke =
            Values(cfg, "smokesystems");

        if (smoke.Count > 0)
        {
            options.ParticleSystems =
                ParseInt(smoke[0]) != 0;
        }

        if (smoke.Count > 1)
        {
            options.MaximumParticlesPerEmitter =
                ParseInt(smoke[1]);
        }

        if (smoke.Count > 2)
        {
            options.ParticleOnlyOwnVehicle =
                ParseInt(smoke[2]) != 0;
        }

        if (smoke.Count > 3)
        {
            options.ParticleSystemsInReflections =
                ParseInt(smoke[3]) == 0;
        }

        AssignPair(
            cfg,
            "performance_dyn_redrefl",
            (a, b) =>
            {
                options.ReflectionEconomyBelowFps = a;
                options.ReflectionFullAboveFps = b;
            });

        AssignPair(
            cfg,
            "performance_dyn_tile_red",
            (a, b) =>
            {
                options.ReduceTilesBelowFps = a;
                options.IncreaseTilesAboveFps = b;
            });

        var screenRatio =
            Number(cfg, "screenratio");

        if (screenRatio.HasValue)
        {
            options.ManualAspectRatioEnabled = true;
            options.ManualAspectRatio =
                screenRatio.Value;
        }

        options.HighResolutionTextureMemoryMb =
            Number(cfg, "texmemlimit") ??
            options.HighResolutionTextureMemoryMb;

        var reflExponent =
            Integer(cfg, "performance_reflTexSize");

        if (reflExponent is >= 4 and <= 14)
        {
            options.RealTimeReflectionTextureSize =
                1 << reflExponent.Value;
        }

        var texFilter =
            Values(cfg, "texFilter");

        if (texFilter.Count > 0)
        {
            options.TextureFilterMode =
                ParseInt(texFilter[0]);
        }

        if (texFilter.Count > 1)
        {
            options.AnisotropicFiltering =
                Math.Max(
                    1,
                    ParseInt(texFilter[1]));
        }

        options.OnlyLowResolutionTextures =
            Present(cfg, "texture_uselow");
        options.LimitTexturesTo256 =
            Present(cfg, "texmax256");
        options.LowResolutionTexturesAtDistance =
            !Present(cfg, "no_tex_low_high_switch");

        options.MaterialNightMap =
            !Present(cfg, "no_nightmap");
        options.MaterialLightMap =
            !Present(cfg, "no_lightmap");
        options.MaterialTerrainLightMap =
            !Present(cfg, "no_lightmap_terr");
        options.MaterialReflectionMap =
            !Present(cfg, "no_reflmap");
        options.MaterialBumpMap =
            !Present(cfg, "no_bumpmap");

        options.MaximumSoundCount =
            Integer(cfg, "sound_maxcount") ??
            options.MaximumSoundCount;
        options.MasterVolumePercent =
            (int)Math.Round(
                100 *
                (Number(cfg, "sound_vol_master") ??
                 options.MasterVolumePercent / 100.0));
        options.StereoEffect =
            Integer(cfg, "sound_stereo") ??
            options.StereoEffect;
        options.DopplerEffect =
            string.Equals(
                Text(cfg, "sound_doppler"),
                "on",
                StringComparison.OrdinalIgnoreCase);
        options.AiVehicleSounds =
            Present(cfg, "sound_ai");
        options.ScenerySounds =
            Present(cfg, "sound_scenery");
        options.ReverbEffects =
            !Present(cfg, "sound_noreverb");

        var random =
            Values(cfg, "AIMaxCountRandom");

        if (random.Count > 0)
        {
            options.MaximumUnscheduledTraffic =
                ParseInt(random[0]);
        }

        if (random.Count > 1)
        {
            options.MaximumPeople =
                ParseInt(random[1]);
        }

        options.RoadTrafficFactorPercent =
            Integer(cfg, "AIUnschedFactor") ??
            options.RoadTrafficFactorPercent;
        options.ParkedCarsPercent =
            Integer(cfg, "AIMaxCountParked") ??
            options.ParkedCarsPercent;
        options.PassengerFactorPercent =
            Integer(cfg, "AIPassFactor") ??
            options.PassengerFactorPercent;
        options.MaximumScheduledTraffic =
            Integer(cfg, "AIMaxCountScheduled") ??
            options.MaximumScheduledTraffic;
        options.ScheduledTrafficPriority =
            Integer(cfg, "AIPriorityScheduled") ??
            options.ScheduledTrafficPriority;
        options.UseReducedAiList =
            Present(cfg, "useLowAIList");

        return options;
    }

    private static OmsiRuntimeOptions Clone(
        OmsiRuntimeOptions value) =>
        JsonSerializer.Deserialize<OmsiRuntimeOptions>(
            JsonSerializer.Serialize(value))
        ?? new OmsiRuntimeOptions();

    private static Dictionary<string, List<string>> ParseConfig(
        IReadOnlyList<string> lines)
    {
        var result =
            new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);

        string? key = null;

        foreach (var raw in lines)
        {
            var line =
                raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) &&
                line.EndsWith("]", StringComparison.Ordinal) &&
                line.Length > 2)
            {
                key =
                    line[1..^1];

                if (!result.ContainsKey(key))
                {
                    result[key] = [];
                }

                continue;
            }

            if (key is null ||
                line.Contains("------------------------",
                    StringComparison.Ordinal))
            {
                continue;
            }

            result[key].Add(line);
        }

        return result;
    }

    private static bool Present(
        IReadOnlyDictionary<string, List<string>> cfg,
        string key) =>
        cfg.ContainsKey(key);

    private static IReadOnlyList<string> Values(
        IReadOnlyDictionary<string, List<string>> cfg,
        string key) =>
        cfg.TryGetValue(
            key,
            out var values)
                ? values
                : Array.Empty<string>();

    private static string? Text(
        IReadOnlyDictionary<string, List<string>> cfg,
        string key)
    {
        var values =
            Values(cfg, key);

        return values.Count == 0
            ? null
            : values[0];
    }

    private static int? Integer(
        IReadOnlyDictionary<string, List<string>> cfg,
        string key)
    {
        var value =
            Text(cfg, key);

        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : null;
    }

    private static double? Number(
        IReadOnlyDictionary<string, List<string>> cfg,
        string key)
    {
        var value =
            Text(cfg, key);

        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : null;
    }

    private static int ParseInt(
        string value) =>
        int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : 0;

    private static void AssignPair(
        IReadOnlyDictionary<string, List<string>> cfg,
        string key,
        Action<double, double> assign)
    {
        var values =
            Values(cfg, key);

        if (values.Count < 2)
        {
            return;
        }

        if (double.TryParse(
                values[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var first) &&
            double.TryParse(
                values[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var second))
        {
            assign(
                first,
                second);
        }
    }
}
