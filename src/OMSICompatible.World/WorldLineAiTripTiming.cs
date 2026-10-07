using OmsiCompat.Map;

namespace OMSICompatible.World;

public sealed record WorldLineAiStopTiming(
    int StopIndex,
    int? StopId,
    string StopName,
    double ArrivalSeconds,
    double DepartureSeconds,
    bool Stops,
    double RouteDistanceMeters = 0.0,
    int StoppingMode = 0);

public sealed record WorldLineAiTripTiming(
    IReadOnlyList<WorldLineAiStopTiming> Stops,
    double DurationSeconds)
{
    public static WorldLineAiTripTiming Empty { get; } =
        new(
            Array.Empty<WorldLineAiStopTiming>(),
            1.0);
}

/// <summary>
/// Resolves OMSI TTP profile timing. Manual profile times win; untimed
/// stations are interpolated by real StnLinks lengths. The selected profile
/// index is the one carried by TTL [addtrip], clamped to the trip's available
/// profile range as OMSI does.
/// </summary>
public static class WorldLineAiTripTimingResolver
{
    private const double DefaultTripDurationSeconds =
        600.0;
    private const double MissingStationLinkLengthMeters =
        500.0;

    public static WorldLineAiTripTiming Resolve(
        OmsiTimetableTrip trip,
        int profileIndex,
        IReadOnlyList<OmsiTimetableStationLink> stationLinks)
    {
        ArgumentNullException.ThrowIfNull(
            trip);
        ArgumentNullException.ThrowIfNull(
            stationLinks);

        OmsiTimetableTripProfile? profile =
            null;

        if (trip.Profiles.Count >
            0)
        {
            var safeIndex =
                Math.Clamp(
                    profileIndex,
                    0,
                    trip.Profiles.Count -
                    1);

            profile =
                trip.Profiles[
                    safeIndex];
        }

        var fallbackDuration =
            profile is
                { FactorMinutes: > 0.0 } &&
            double.IsFinite(
                profile.FactorMinutes)
                ? profile.FactorMinutes *
                  60.0
                : DefaultTripDurationSeconds;

        var count =
            trip.Stops.Count;

        if (count ==
            0)
        {
            return new WorldLineAiTripTiming(
                Array.Empty<WorldLineAiStopTiming>(),
                Math.Max(
                    fallbackDuration,
                    1.0));
        }

        var arrival =
            new double?[
                count];
        var departure =
            new double?[
                count];
        var stopping =
            Enumerable
                .Repeat(
                    true,
                    count)
                .ToArray();
        var stoppingMode =
            new int[
                count];

        if (profile is not
            null)
        {
            foreach (var value in
                     profile.ManualArrivalTimes)
            {
                if (value.StationIndex >=
                        0 &&
                    value.StationIndex <
                        count &&
                    double.IsFinite(
                        value.Minutes))
                {
                    arrival[
                        value.StationIndex] =
                        value.Minutes *
                        60.0;
                }
            }

            foreach (var value in
                     profile.ManualDepartureTimes)
            {
                if (value.StationIndex >=
                        0 &&
                    value.StationIndex <
                        count &&
                    double.IsFinite(
                        value.Minutes))
                {
                    departure[
                        value.StationIndex] =
                        value.Minutes *
                        60.0;
                }
            }

            foreach (var value in
                     profile.OtherStopping)
            {
                if (value.StationIndex >=
                        0 &&
                    value.StationIndex <
                        count)
                {
                    stoppingMode[
                        value.StationIndex] =
                        value.Mode;

                    if (value.Mode ==
                        2)
                    {
                        stopping[
                            value.StationIndex] =
                            false;
                    }
                }
            }
        }

        if (!arrival[0].HasValue &&
            !departure[0].HasValue)
        {
            departure[0] =
                0.0;
        }

        if (count >
                1 &&
            !arrival[
                count -
                1]
                .HasValue &&
            !departure[
                count -
                1]
                .HasValue)
        {
            arrival[
                count -
                1] =
                fallbackDuration;
        }

        var lengths =
            stationLinks
                .GroupBy(
                    static link =>
                        (
                            link.FromStopId,
                            link.ToStopId
                        ))
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.First()
                            .LengthMeters);

        var along =
            new double[
                count];

        for (var index = 1;
             index <
                 count;
             index++)
        {
            var previous =
                trip.Stops[
                    index -
                    1]
                    .StopId;
            var current =
                trip.Stops[
                    index]
                    .StopId;

            var length =
                previous.HasValue &&
                current.HasValue &&
                lengths.TryGetValue(
                    (
                        previous.Value,
                        current.Value
                    ),
                    out var resolvedLength) &&
                double.IsFinite(
                    resolvedLength)
                    ? resolvedLength
                    : MissingStationLinkLengthMeters;

            along[index] =
                along[
                    index -
                    1] +
                Math.Max(
                    length,
                    1.0);
        }

        var timed =
            Enumerable
                .Range(
                    0,
                    count)
                .Where(
                    index =>
                        arrival[index]
                            .HasValue ||
                        departure[index]
                            .HasValue)
                .ToArray();

        var result =
            new List<WorldLineAiStopTiming>(
                count);

        var last =
            0.0;

        for (var index = 0;
             index <
                 count;
             index++)
        {
            double arrivalSeconds;
            double departureSeconds;

            if (arrival[index].HasValue &&
                departure[index].HasValue)
            {
                arrivalSeconds =
                    arrival[index]!.Value;
                departureSeconds =
                    Math.Max(
                        departure[index]!.Value,
                        arrivalSeconds);
            }
            else if (arrival[index].HasValue)
            {
                arrivalSeconds =
                    arrival[index]!.Value;
                departureSeconds =
                    arrivalSeconds;
            }
            else if (departure[index].HasValue)
            {
                arrivalSeconds =
                    departure[index]!.Value;
                departureSeconds =
                    arrivalSeconds;
            }
            else
            {
                var previousTimed =
                    timed
                        .Where(
                            candidate =>
                                candidate <
                                index)
                        .DefaultIfEmpty(
                            -1)
                        .Last();

                var nextTimed =
                    timed
                        .FirstOrDefault(
                            candidate =>
                                candidate >
                                index,
                            -1);

                double interpolated;

                if (previousTimed >=
                        0 &&
                    nextTimed >=
                        0)
                {
                    var fromTime =
                        departure[
                            previousTimed] ??
                        arrival[
                            previousTimed] ??
                        0.0;
                    var toTime =
                        arrival[
                            nextTimed] ??
                        departure[
                            nextTimed] ??
                        fromTime;
                    var span =
                        along[
                            nextTimed] -
                        along[
                            previousTimed];

                    interpolated =
                        span >
                                0.0
                            ? fromTime +
                              (toTime -
                               fromTime) *
                              (along[index] -
                               along[
                                   previousTimed]) /
                              span
                            : fromTime;
                }
                else if (previousTimed >=
                         0)
                {
                    interpolated =
                        departure[
                            previousTimed] ??
                        arrival[
                            previousTimed] ??
                        0.0;
                }
                else if (nextTimed >=
                         0)
                {
                    interpolated =
                        arrival[
                            nextTimed] ??
                        departure[
                            nextTimed] ??
                        0.0;
                }
                else
                {
                    interpolated =
                        0.0;
                }

                arrivalSeconds =
                    interpolated;
                departureSeconds =
                    interpolated;
            }

            arrivalSeconds =
                Math.Max(
                    arrivalSeconds,
                    last);
            departureSeconds =
                Math.Max(
                    departureSeconds,
                    arrivalSeconds);
            last =
                departureSeconds;

            var stop =
                trip.Stops[
                    index];

            result.Add(
                new WorldLineAiStopTiming(
                    index,
                    stop.StopId,
                    stop.Name,
                    arrivalSeconds,
                    departureSeconds,
                    stopping[index],
                    along[index],
                    stoppingMode[index]));
        }

        var duration =
            count >
                    1
                ? result[^1]
                    .ArrivalSeconds
                : fallbackDuration;

        return new WorldLineAiTripTiming(
            result.ToArray(),
            Math.Max(
                duration,
                1.0));
    }
}
