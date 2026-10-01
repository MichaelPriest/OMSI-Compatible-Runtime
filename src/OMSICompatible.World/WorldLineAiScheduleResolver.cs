using OmsiCompat.Map;

namespace OMSICompatible.World;

public enum WorldLineAiScheduleStatus
{
    Ready,
    TripMissing,
    TripAmbiguous,
    TrackMissing,
    StationLinkMissing,
    RouteUnresolved,
    VehicleGroupMissing
}

public sealed record WorldLineAiScheduledTrip(
    string LineName,
    int LinePriority,
    string TourNumber,
    string AiGroup,
    string Extra,
    string TripName,
    int ProfileIndex,
    double DepartureMinutes,
    OmsiTimetableTrip? Trip,
    WorldLineAiRoute? Route,
    IReadOnlyList<OmsiAiVehicleDefinition> VehicleCandidates,
    WorldLineAiScheduleStatus Status)
{
    public bool Ready =>
        Status ==
        WorldLineAiScheduleStatus.Ready;

    public int DayMask =>
        OmsiTimetableDayMask.Parse(
            Extra);
}

public sealed record WorldLineAiSchedule(
    IReadOnlyList<WorldLineAiScheduledTrip> Trips)
{
    public static WorldLineAiSchedule Empty { get; } =
        new(
            Array.Empty<WorldLineAiScheduledTrip>());

    public int ReadyCount =>
        Trips.Count(
            static trip =>
                trip.Ready);

    public int UnresolvedCount =>
        Trips.Count -
        ReadyCount;
}

/// <summary>
/// Connects real OMSI TTL tours to their TTP trip, TTR road route and AI-list
/// vehicle group. Resolution deliberately fails closed: a missing/ambiguous
/// trip, unresolved track or empty AI group never gets a guessed route or
/// fallback vehicle.
/// </summary>
public static class WorldLineAiScheduleResolver
{
    public static WorldLineAiSchedule Resolve(
        OmsiTimetableCatalog catalog,
        WorldTrafficPathNetwork network,
        OmsiMapAiCatalog aiCatalog)
    {
        ArgumentNullException.ThrowIfNull(
            catalog);
        ArgumentNullException.ThrowIfNull(
            network);
        ArgumentNullException.ThrowIfNull(
            aiCatalog);

        if (catalog.Lines.Count ==
            0)
        {
            return WorldLineAiSchedule.Empty;
        }

        var tripsByName =
            catalog.Trips
                .GroupBy(
                    static trip =>
                        trip.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var vehiclesByGroup =
            aiCatalog.MovingVehicles
                .Where(
                    static vehicle =>
                        vehicle.Exists &&
                        vehicle.Weight >
                            0.0 &&
                        IsLineVehicle(
                            vehicle))
                .GroupBy(
                    static vehicle =>
                        vehicle.GroupName,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        (IReadOnlyList<OmsiAiVehicleDefinition>)
                        group
                            .OrderBy(
                                static vehicle =>
                                    vehicle.DeclaredPath,
                                StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var schedule =
            new List<WorldLineAiScheduledTrip>();

        foreach (var line in
                 catalog.Lines)
        {
            foreach (var tour in
                     line.Tours)
            {
                foreach (var tourTrip in
                         tour.Trips)
                {
                    schedule.Add(
                        ResolveTrip(
                            line,
                            tour,
                            tourTrip,
                            tripsByName,
                            catalog.Tracks,
                            catalog.StationLinks,
                            vehiclesByGroup,
                            network));
                }
            }
        }

        return new WorldLineAiSchedule(
            schedule
                .OrderBy(
                    static trip =>
                        trip.DepartureMinutes)
                .ThenBy(
                    static trip =>
                        trip.LineName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static trip =>
                        trip.TourNumber,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static trip =>
                        trip.TripName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public static OmsiAiVehicleDefinition? SelectVehicle(
        WorldLineAiScheduledTrip trip,
        int serviceDaySeed = 0)
    {
        ArgumentNullException.ThrowIfNull(
            trip);

        if (!trip.Ready ||
            trip.VehicleCandidates.Count ==
                0)
        {
            return null;
        }

        var totalWeight =
            trip.VehicleCandidates.Sum(
                static vehicle =>
                    Math.Max(
                        0.0,
                        vehicle.Weight));

        if (!double.IsFinite(
                totalWeight) ||
            totalWeight <=
                0.0)
        {
            return null;
        }

        var hash =
            StableHash(
                string.Join(
                    "|",
                    trip.LineName,
                    trip.TourNumber,
                    trip.TripName,
                    trip.ProfileIndex,
                    trip.DepartureMinutes.ToString(
                        "R",
                        System.Globalization.CultureInfo.InvariantCulture),
                    serviceDaySeed));

        var selector =
            (hash /
             (double)uint.MaxValue) *
            totalWeight;

        var accumulated =
            0.0;

        foreach (var vehicle in
                 trip.VehicleCandidates)
        {
            accumulated +=
                Math.Max(
                    0.0,
                    vehicle.Weight);

            if (selector <=
                accumulated)
            {
                return vehicle;
            }
        }

        return trip.VehicleCandidates[^1];
    }

    private static WorldLineAiScheduledTrip ResolveTrip(
        OmsiTimetableLine line,
        OmsiTimetableTour tour,
        OmsiTimetableTourTrip tourTrip,
        IReadOnlyDictionary<string, OmsiTimetableTrip[]> tripsByName,
        IReadOnlyDictionary<string, OmsiTimetableTrack> tracks,
        IReadOnlyList<OmsiTimetableStationLink> stationLinks,
        IReadOnlyDictionary<string, IReadOnlyList<OmsiAiVehicleDefinition>>
            vehiclesByGroup,
        WorldTrafficPathNetwork network)
    {
        if (!tripsByName.TryGetValue(
                tourTrip.TripName,
                out var matchingTrips) ||
            matchingTrips.Length ==
                0)
        {
            return Create(
                WorldLineAiScheduleStatus.TripMissing);
        }

        if (matchingTrips.Length !=
            1)
        {
            return Create(
                WorldLineAiScheduleStatus.TripAmbiguous);
        }

        var trip =
            matchingTrips[0];

        WorldLineAiRoute route;

        if (!string.IsNullOrWhiteSpace(
                trip.TrackName))
        {
            if (!tracks.TryGetValue(
                    trip.TrackName,
                    out var track))
            {
                return Create(
                    WorldLineAiScheduleStatus.TrackMissing,
                    trip);
            }

            route =
                WorldLineAiRouteResolver.Resolve(
                    track,
                    network);
        }
        else
        {
            if (!WorldLineAiRouteResolver
                .HasCompleteStationLinkChain(
                    trip,
                    stationLinks))
            {
                return Create(
                    WorldLineAiScheduleStatus.StationLinkMissing,
                    trip);
            }

            route =
                WorldLineAiRouteResolver.ResolveStationLinks(
                    trip,
                    stationLinks,
                    network);
        }

        if (!route.FullyResolved)
        {
            return Create(
                WorldLineAiScheduleStatus.RouteUnresolved,
                trip,
                route);
        }

        var vehicles =
            vehiclesByGroup.TryGetValue(
                tour.AiGroup,
                out var groupVehicles)
                ? groupVehicles
                : Array.Empty<OmsiAiVehicleDefinition>();

        if (vehicles.Count ==
            0)
        {
            return Create(
                WorldLineAiScheduleStatus.VehicleGroupMissing,
                trip,
                route);
        }

        return Create(
            WorldLineAiScheduleStatus.Ready,
            trip,
            route,
            vehicles);

        WorldLineAiScheduledTrip Create(
            WorldLineAiScheduleStatus status,
            OmsiTimetableTrip? resolvedTrip = null,
            WorldLineAiRoute? resolvedRoute = null,
            IReadOnlyList<OmsiAiVehicleDefinition>? vehicleCandidates = null) =>
            new(
                line.Name,
                line.Priority,
                tour.Number,
                tour.AiGroup,
                tour.Extra,
                tourTrip.TripName,
                tourTrip.ProfileIndex,
                tourTrip.DepartureMinutes,
                resolvedTrip,
                resolvedRoute,
                vehicleCandidates ??
                    Array.Empty<OmsiAiVehicleDefinition>(),
                status);
    }

    private static bool IsLineVehicle(
        OmsiAiVehicleDefinition vehicle)
    {
        var extension =
            Path.GetExtension(
                vehicle.DeclaredPath);

        return extension.Equals(
                   ".bus",
                   StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(
                   ".ovh",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static uint StableHash(
        string value)
    {
        const uint offset =
            2166136261;
        const uint prime =
            16777619;

        var hash =
            offset;

        foreach (var character in
                 value)
        {
            hash ^=
                character;
            hash *=
                prime;
        }

        return hash;
    }
}
