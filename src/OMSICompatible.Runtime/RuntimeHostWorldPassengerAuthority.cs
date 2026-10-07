using System.Globalization;
using System.Numerics;
using OmsiCompat.Map;
using OMSICompatible.Multiplayer;
using OMSICompatible.World;

namespace OMSICompatible.Runtime;

internal sealed class RuntimeHostWorldPassengerAuthority
{
    private const double TileSizeMeters = 300.0;
    private const uint FirstPersonId = 0x00400000u;
    private const uint LastPersonId = 0x00BFFFFFu;

    private readonly Dictionary<string, uint> _idsBySpot =
        new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _spotById =
        [];
    private readonly Dictionary<uint, HostPassenger> _people =
        [];
    private readonly Dictionary<string, DateTimeOffset> _handedOver =
        new(StringComparer.Ordinal);
    private readonly Queue<OpenOmsiLanWorldPersonDescription>
        _pendingDescriptions =
            [];
    private readonly HashSet<uint> _described =
        [];
    private readonly Dictionary<uint, double> _goneSeconds =
        [];
    private readonly Dictionary<uint, (
        string HumanPath,
        DateTimeOffset ExpiresAt)>
        _transferredDescriptions =
            [];

    private uint _nextPersonId =
        FirstPersonId;

    public IReadOnlyList<OpenOmsiLanWorldPersonState> People =>
        _people
            .Values
            .OrderBy(static person => person.State.Id)
            .Select(static person => person.State)
            .ToArray();

    public void Refresh(
        WorldDefinition? world,
        OmsiTimetableCatalog timetable)
    {
        var now =
            DateTimeOffset.UtcNow;

        foreach (var staleDescription in
                 _transferredDescriptions
                     .Where(
                         pair =>
                             pair.Value.ExpiresAt <=
                             now)
                     .Select(
                         static pair =>
                             pair.Key)
                     .ToArray())
        {
            _transferredDescriptions.Remove(
                staleDescription);
        }

        foreach (var stale in
                 _handedOver
                     .Where(
                         pair =>
                             now -
                                 pair.Value >=
                             TimeSpan.FromMinutes(
                                 3))
                     .Select(
                         static pair =>
                             pair.Key)
                     .ToArray())
        {
            _handedOver.Remove(
                stale);
        }

        if (world is null)
        {
            RemoveAllVisible();
            return;
        }

        var humanPaths =
            world.AiCatalog.Humans
                .Where(
                    static human =>
                        human.Exists &&
                        !string.IsNullOrWhiteSpace(
                            human.DeclaredPath))
                .Select(
                    static human =>
                        human.DeclaredPath
                            .Replace(
                                '\\',
                                '/'))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (humanPaths.Length ==
            0)
        {
            RemoveAllVisible();
            return;
        }

        var stopIds =
            timetable.Trips
                .SelectMany(
                    static trip =>
                        trip.Stops)
                .Where(
                    static stop =>
                        stop.StopId.HasValue)
                .Select(
                    static stop =>
                        (long)stop.StopId!.Value)
                .ToHashSet();

        if (stopIds.Count ==
            0)
        {
            RemoveAllVisible();
            return;
        }

        var stopObjects =
            world.Objects
                .Where(
                    placement =>
                        stopIds.Contains(
                            placement.Id))
                .OrderBy(
                    static placement =>
                        placement.Id)
                .ThenBy(
                    static placement =>
                        placement.Tile.Y)
                .ThenBy(
                    static placement =>
                        placement.Tile.X)
                .ToArray();

        if (stopObjects.Length ==
            0)
        {
            RemoveAllVisible();
            return;
        }

        var waiting =
            BuildWaitingPlaces(
                world);

        var desired =
            new Dictionary<string, DesiredPassenger>(
                StringComparer.Ordinal);

        foreach (var stop in
                 stopObjects)
        {
            var stopX =
                stop.Tile.X *
                    TileSizeMeters +
                stop.Position.X;

            var stopY =
                stop.Tile.Y *
                    TileSizeMeters +
                stop.Position.Z;

            var stopZ =
                stop.Position.Y;

            var headingRadians =
                stop.HeadingDegrees *
                Math.PI /
                180.0;

            var sin =
                Math.Sin(
                    headingRadians);

            var cos =
                Math.Cos(
                    headingRadians);

            var length =
                Math.Max(
                    10.0,
                    ParseStopNumber(
                        stop.ExtraValues,
                        4,
                        30.0,
                        round:
                            false));

            var candidates =
                waiting
                    .Select(
                        spot =>
                        {
                            var dx =
                                spot.X -
                                stopX;

                            var dy =
                                spot.Y -
                                stopY;

                            var lateral =
                                cos *
                                dx -
                                sin *
                                dy;

                            var along =
                                sin *
                                dx +
                                cos *
                                dy;

                            return (
                                Spot:
                                    spot,
                                DistanceSquared:
                                    dx *
                                    dx +
                                    dy *
                                    dy,
                                Lateral:
                                    lateral,
                                Along:
                                    along
                            );
                        })
                    .Where(
                        item =>
                            item.Along >
                                -10.0 &&
                            item.Along <
                                length &&
                            Math.Abs(
                                item.Lateral) <
                                10.0 &&
                            item.DistanceSquared <
                                Math.Pow(
                                    Math.Max(
                                        40.0,
                                        length +
                                        15.0),
                                    2.0))
                    .OrderBy(
                        static item =>
                            item.Along)
                    .ThenBy(
                        static item =>
                            Math.Abs(
                                item.Lateral))
                    .ThenBy(
                        static item =>
                            item.Spot.Key,
                        StringComparer.Ordinal)
                    .Take(
                        255)
                    .ToArray();

            if (candidates.Length ==
                0)
            {
                continue;
            }

            var enterMax =
                Math.Max(
                    0.0,
                    ParseStopNumber(
                        stop.ExtraValues,
                        1,
                        1.0,
                        round:
                            true));

            var enterMin =
                Math.Max(
                    0.0,
                    ParseStopNumber(
                        stop.ExtraValues,
                        2,
                        0.0,
                        round:
                            true));

            if (enterMin >
                enterMax)
            {
                (
                    enterMin,
                    enterMax
                ) =
                (
                    enterMax,
                    enterMin
                );
            }

            var targetCount =
                Math.Clamp(
                    (int)Math.Ceiling(
                        (
                            enterMin +
                            enterMax
                        ) /
                        2.0),
                    0,
                    candidates.Length);

            for (var index = 0;
                 index <
                     targetCount;
                 index++)
            {
                var spot =
                    candidates[index]
                        .Spot;

                var key =
                    string.Join(
                        "|",
                        stop.Id.ToString(
                            CultureInfo.InvariantCulture),
                        stop.Tile.X.ToString(
                            CultureInfo.InvariantCulture),
                        stop.Tile.Y.ToString(
                            CultureInfo.InvariantCulture),
                        spot.Key);

                if (_handedOver.ContainsKey(
                        key))
                {
                    continue;
                }

                desired[
                    key] =
                    new DesiredPassenger(
                        stop.Id,
                        (byte)index,
                        spot,
                        humanPaths[
                            StableIndex(
                                key,
                                humanPaths.Length)]);
            }
        }

        foreach (var existing in
                 _people
                     .Values
                     .Where(
                         person =>
                             !desired.ContainsKey(
                                 person.Key))
                     .Select(
                         static person =>
                             person.State.Id)
                     .ToArray())
        {
            RemoveVisible(
                existing);
        }

        foreach (var pair in
                 desired)
        {
            var id =
                GetOrCreatePersonId(
                    pair.Key);

            var selected =
                pair.Value;

            var state =
                new OpenOmsiLanWorldPersonState(
                    id,
                    selected.Spot.HeightMeters >
                            0.01
                        ? OpenOmsiLanWorldPersonActivity.Sit
                        : OpenOmsiLanWorldPersonActivity.Stand,
                    false,
                    false,
                    0,
                    selected.Spot.X,
                    selected.Spot.Y,
                    selected.Spot.Z,
                    (float)selected.Spot.HeadingDegrees,
                    0.0f,
                    selected.StopObjectId,
                    selected.WaitingSpot,
                    null);

            _people[
                id] =
                new HostPassenger(
                    pair.Key,
                    state,
                    selected.HumanPath);

            if (_described.Add(
                    id))
            {
                _pendingDescriptions.Enqueue(
                    new OpenOmsiLanWorldPersonDescription(
                        id,
                        selected.HumanPath));
            }

            _goneSeconds.Remove(
                id);
        }
    }

    public void ResetSession()
    {
        _people.Clear();
        _handedOver.Clear();
        _pendingDescriptions.Clear();
        _described.Clear();
        _goneSeconds.Clear();
        _transferredDescriptions.Clear();
        _idsBySpot.Clear();
        _spotById.Clear();
        _nextPersonId =
            FirstPersonId;
    }

    public IReadOnlyList<OpenOmsiLanWorldPersonDescription>
        TakePendingDescriptions()
    {
        if (_pendingDescriptions.Count ==
            0)
        {
            return Array.Empty<
                OpenOmsiLanWorldPersonDescription>();
        }

        var result =
            new List<OpenOmsiLanWorldPersonDescription>(
                _pendingDescriptions.Count);

        while (_pendingDescriptions.TryDequeue(
                   out var description))
        {
            result.Add(
                description);
        }

        return result;
    }

    public OpenOmsiLanWorldPeopleFrame CreateFrame(
        double elapsedSeconds)
    {
        var gone =
            _goneSeconds
                .Keys
                .OrderBy(
                    static id =>
                        id)
                .Take(
                    63)
                .Select(
                    static id =>
                        new OpenOmsiLanWorldGoneEntity(
                            true,
                            id))
                .ToArray();

        var frame =
            new OpenOmsiLanWorldPeopleFrame(
                0,
                0,
                People,
                gone);

        var elapsed =
            Math.Clamp(
                elapsedSeconds,
                0.0,
                0.25);

        foreach (var id in
                 gone.Select(
                     static item =>
                         item.Id))
        {
            if (!_goneSeconds.TryGetValue(
                    id,
                    out var remaining))
            {
                continue;
            }

            remaining -=
                elapsed;

            if (remaining <=
                0.000001)
            {
                _goneSeconds.Remove(
                    id);
            }
            else
            {
                _goneSeconds[
                    id] =
                    remaining;
            }
        }

        return frame;
    }

    public IReadOnlyList<uint> HandOver(
        IReadOnlyList<uint> requested)
    {
        if (requested.Count ==
            0)
        {
            return Array.Empty<uint>();
        }

        var granted =
            new List<uint>(
                requested.Count);

        foreach (var id in
                 requested.Distinct())
        {
            if (!_people.TryGetValue(
                    id,
                    out var passenger) ||
                passenger.State.Aboard ||
                !passenger.State.WaitingStopObjectId.HasValue)
            {
                continue;
            }

            var transferredAt =
                DateTimeOffset.UtcNow;

            _handedOver[
                passenger.Key] =
                transferredAt;

            // The transferred WORLD id remains owned by the client from
            // this point on. Detach the stop slot from that id so a later
            // repopulation allocates a fresh id instead of colliding with
            // a passenger that may still be riding remotely.
            _idsBySpot.Remove(
                passenger.Key);

            if (!string.IsNullOrWhiteSpace(
                    passenger.HumanPath))
            {
                _transferredDescriptions[
                    id] =
                    (
                        passenger.HumanPath,
                        transferredAt +
                        TimeSpan.FromSeconds(
                            30)
                    );
            }

            _people.Remove(
                id);

            _goneSeconds[
                id] =
                1.0;

            granted.Add(
                id);
        }

        return granted;
    }

    public string? HumanPath(
        uint id)
    {
        if (_people.TryGetValue(
                id,
                out var passenger))
        {
            return passenger.HumanPath;
        }

        return _transferredDescriptions.TryGetValue(
                   id,
                   out var transferred) &&
               transferred.ExpiresAt >
                   DateTimeOffset.UtcNow
            ? transferred.HumanPath
            : null;
    }

    private void RemoveAllVisible()
    {
        foreach (var id in
                 _people.Keys.ToArray())
        {
            RemoveVisible(
                id);
        }
    }

    private void RemoveVisible(
        uint id)
    {
        if (!_people.Remove(
                id))
        {
            return;
        }

        _goneSeconds[
            id] =
            1.0;
    }

    private uint GetOrCreatePersonId(
        string key)
    {
        if (_idsBySpot.TryGetValue(
                key,
                out var existing))
        {
            return existing;
        }

        var candidate =
            _nextPersonId;

        while (_spotById.ContainsKey(
                   candidate))
        {
            candidate =
                candidate >=
                        LastPersonId
                    ? FirstPersonId
                    : candidate +
                      1u;

            if (candidate ==
                _nextPersonId)
            {
                throw new InvalidOperationException(
                    "No host WORLD passenger ids are available.");
            }
        }

        _idsBySpot[
            key] =
            candidate;

        _spotById[
            candidate] =
            key;

        _nextPersonId =
            candidate >=
                    LastPersonId
                ? FirstPersonId
                : candidate +
                  1u;

        return candidate;
    }

    private static WaitingPlace[] BuildWaitingPlaces(
        WorldDefinition world)
    {
        var result =
            new List<WaitingPlace>();

        foreach (var placement in
                 world.Objects)
        {
            if (!world.SceneryAssets.TryGetValue(
                    placement.AssetPath,
                    out var asset) ||
                asset.PassengerWaitingPositions is not
                    { Count: > 0 } places)
            {
                continue;
            }

            var origin =
                new Vector3(
                    (float)(
                        placement.Tile.X *
                            TileSizeMeters +
                        placement.Position.X),
                    (float)placement.Position.Y,
                    (float)(
                        placement.Tile.Y *
                            TileSizeMeters +
                        placement.Position.Z));

            var rotation =
                Matrix4x4.CreateFromYawPitchRoll(
                    DegreesToRadians(
                        placement.HeadingDegrees),
                    DegreesToRadians(
                        placement.PitchDegrees),
                    DegreesToRadians(
                        placement.BankDegrees));

            foreach (var place in
                     places)
            {
                var local =
                    new Vector3(
                        (float)place.X,
                        (float)place.Z,
                        (float)place.Y);

                var worldPoint =
                    Vector3.Transform(
                        local,
                        rotation) +
                    origin;

                var key =
                    string.Join(
                        ":",
                        placement.Id.ToString(
                            CultureInfo.InvariantCulture),
                        placement.Tile.X.ToString(
                            CultureInfo.InvariantCulture),
                        placement.Tile.Y.ToString(
                            CultureInfo.InvariantCulture),
                        place.FileIndex.ToString(
                            CultureInfo.InvariantCulture));

                result.Add(
                    new WaitingPlace(
                        key,
                        placement.Id,
                        worldPoint.X,
                        worldPoint.Z,
                        worldPoint.Y,
                        NormalizeHeading(
                            placement.HeadingDegrees +
                            place.HeadingDegrees),
                        place.HeightMeters));
            }
        }

        return result.ToArray();
    }

    private static double ParseStopNumber(
        IReadOnlyList<string> values,
        int index,
        double fallback,
        bool round)
    {
        if (index < 0 ||
            index >=
                values.Count)
        {
            return fallback;
        }

        var text =
            values[index]
                .Trim()
                .Replace(
                    ',',
                    '.');

        if (!double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            !double.IsFinite(
                parsed))
        {
            return fallback;
        }

        return round
            ? Math.Round(
                parsed,
                MidpointRounding.AwayFromZero)
            : parsed;
    }

    private static int StableIndex(
        string value,
        int count)
    {
        if (count <=
            1)
        {
            return 0;
        }

        uint hash =
            2166136261;

        foreach (var character in
                 value)
        {
            hash ^=
                character;

            hash *=
                16777619;
        }

        return (int)(
            hash %
            (uint)count);
    }

    private static float DegreesToRadians(
        double degrees) =>
        (float)(
            degrees *
            Math.PI /
            180.0);

    private static double NormalizeHeading(
        double value)
    {
        value %=
            360.0;

        if (value <=
            -180.0)
        {
            value +=
                360.0;
        }
        else if (value >
                 180.0)
        {
            value -=
                360.0;
        }

        return value;
    }

    private sealed record HostPassenger(
        string Key,
        OpenOmsiLanWorldPersonState State,
        string HumanPath);

    private sealed record DesiredPassenger(
        long StopObjectId,
        byte WaitingSpot,
        WaitingPlace Spot,
        string HumanPath);

    private sealed record WaitingPlace(
        string Key,
        long ObjectId,
        double X,
        double Y,
        double Z,
        double HeadingDegrees,
        double HeightMeters);
}
