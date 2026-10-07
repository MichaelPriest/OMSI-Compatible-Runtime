using System.Numerics;
using OmsiCompat.Vehicles;
using OMSICompatible.Multiplayer;

namespace OMSICompatible.Runtime;

internal sealed class RuntimeWorldPassengerSimulation
{
    private const float WalkSpeed = 1.1f;
    private const float Reach = 0.16f;
    private const float SeatFront = 0.55f;

    private int? _entry;
    private int? _entryOrdinal;
    private OmsiPassengerCabinPosition? _place;
    private int[] _route = [];
    private int _routeIndex;
    private Vector3 _local;

    public RuntimeWorldPassengerSimulation(
        OpenOmsiLanWorldPersonState state,
        string? humanPath)
    {
        State = state with
        {
            WaitingStopObjectId = null,
            WaitingSpot = null
        };
        HumanPath = humanPath ?? string.Empty;
    }

    public OpenOmsiLanWorldPersonState State { get; private set; }
    public string HumanPath { get; }
    public int? ReservedPlaceIndex => _place?.FileIndex;

    public void Update(
        double deltaSeconds,
        OmsiVehicleAsset? vehicle,
        OpenOmsiLanPose bus,
        uint playerId,
        bool doorsOpen,
        ISet<int> reserved,
        IReadOnlySet<int>? openEntries = null)
    {
        var dt = (float)Math.Clamp(deltaSeconds, 0.0, 0.1);
        if (_place is not null)
        {
            reserved.Add(_place.FileIndex);
        }

        if (!State.Aboard)
        {
            if (_entry is null)
            {
                if (!doorsOpen ||
                    vehicle is null ||
                    !Configure(vehicle, bus, reserved, openEntries))
                {
                    State = State with
                    {
                        Activity = OpenOmsiLanWorldPersonActivity.Stand,
                        SpeedMetersPerSecond = 0
                    };
                    return;
                }
            }

            if (!doorsOpen ||
                (_entryOrdinal.HasValue &&
                 openEntries is not null &&
                 !openEntries.Contains(_entryOrdinal.Value)) ||
                vehicle?.PassengerPaths is not { } paths ||
                !_entry.HasValue ||
                _entry.Value < 0 ||
                _entry.Value >= paths.Points.Count)
            {
                State = State with
                {
                    Activity = OpenOmsiLanWorldPersonActivity.Stand,
                    SpeedMetersPerSecond = 0
                };
                return;
            }

            var localEntry = V(paths.Points[_entry.Value]);
            var target = LocalToWorld(bus, localEntry);
            var current = new Vector3((float)State.X, (float)State.Y, (float)State.Z);
            var moved = Move(current, target, WalkSpeed * dt, out var reached);
            State = State with
            {
                X = moved.X,
                Y = moved.Y,
                Z = moved.Z,
                HeadingDegrees = Heading(target - current, State.HeadingDegrees),
                Activity = OpenOmsiLanWorldPersonActivity.Walk,
                SpeedMetersPerSecond = reached ? 0 : WalkSpeed
            };
            if (!reached)
            {
                return;
            }

            _local = localEntry;
            _routeIndex = _route.Length > 1 && _route[0] == _entry.Value ? 1 : 0;
            State = State with
            {
                Aboard = true,
                PlayerBus = true,
                BusId = playerId,
                X = _local.X,
                Y = _local.Y,
                Z = _local.Z,
                Activity = OpenOmsiLanWorldPersonActivity.Walk,
                SpeedMetersPerSecond = WalkSpeed,
                SeatIndex = null
            };
        }

        if (_place is null ||
            vehicle?.PassengerPaths is not { } cabinPaths ||
            State.Activity == OpenOmsiLanWorldPersonActivity.Sit)
        {
            return;
        }

        if (_routeIndex < _route.Length)
        {
            var i = _route[_routeIndex];
            if (i < 0 || i >= cabinPaths.Points.Count)
            {
                _routeIndex++;
                return;
            }

            var target = V(cabinPaths.Points[i]);
            var before = _local;
            _local = Move(_local, target, WalkSpeed * dt, out var reached);
            State = State with
            {
                X = _local.X,
                Y = _local.Y,
                Z = _local.Z,
                HeadingDegrees = Heading(target - before, State.HeadingDegrees),
                Activity = OpenOmsiLanWorldPersonActivity.Walk,
                SpeedMetersPerSecond = reached ? 0 : WalkSpeed
            };
            if (reached)
            {
                _routeIndex++;
            }
            return;
        }

        var floor = PlaceFloor(_place);
        var old = _local;
        _local = Move(_local, floor, WalkSpeed * dt, out var atPlace);
        State = State with
        {
            X = _local.X,
            Y = _local.Y,
            Z = _local.Z,
            HeadingDegrees = Heading(floor - old, (float)_place.RotationDegrees),
            Activity = OpenOmsiLanWorldPersonActivity.Walk,
            SpeedMetersPerSecond = atPlace ? 0 : WalkSpeed
        };
        if (!atPlace)
        {
            return;
        }

        var seated = _place.Height > 0.01;
        if (seated)
        {
            _local = new Vector3((float)_place.X, (float)_place.Y, (float)_place.Z);
        }
        State = State with
        {
            X = _local.X,
            Y = _local.Y,
            Z = _local.Z,
            HeadingDegrees = (float)_place.RotationDegrees,
            Activity = seated
                ? OpenOmsiLanWorldPersonActivity.Sit
                : OpenOmsiLanWorldPersonActivity.Stand,
            SpeedMetersPerSecond = 0,
            SeatIndex = seated
                ? (byte)Math.Clamp(_place.FileIndex, 0, 254)
                : null
        };
    }

    public (double X, double Y, double Z, float Heading) WorldPose(
        OpenOmsiLanPose bus)
    {
        if (!State.Aboard)
        {
            return (State.X, State.Y, State.Z, State.HeadingDegrees);
        }
        var p = LocalToWorld(
            bus,
            new Vector3((float)State.X, (float)State.Y, (float)State.Z));
        return (p.X, p.Y, p.Z, Normalize(bus.HeadingDegrees + State.HeadingDegrees));
    }

    private bool Configure(
        OmsiVehicleAsset vehicle,
        OpenOmsiLanPose bus,
        ISet<int> reserved,
        IReadOnlySet<int>? openEntries)
    {
        var cabin = vehicle.PassengerCabin;
        var paths = vehicle.PassengerPaths;
        if (cabin is null || paths is null ||
            cabin.Entries.Count == 0 ||
            cabin.PassengerPositions.Count == 0 ||
            paths.Points.Count == 0)
        {
            return false;
        }

        var here = new Vector2((float)State.X, (float)State.Y);
        var entry = cabin.Entries
            .Select((value, index) =>
                new KeyValuePair<int, OmsiPassengerCabinEntry>(
                    index,
                    value))
            .Where(item =>
                (openEntries is null || openEntries.Contains(item.Key)) &&
                item.Value.PathPoint >= 0 &&
                item.Value.PathPoint < paths.Points.Count)
            .OrderBy(item =>
            {
                var w = LocalToWorld(
                    bus,
                    V(paths.Points[item.Value.PathPoint]));
                return Vector2.DistanceSquared(
                    here,
                    new Vector2(w.X, w.Y));
            })
            .FirstOrDefault();
        if (entry.Value is null)
        {
            return false;
        }

        var place = cabin.PassengerPositions
            .Where(p => !reserved.Contains(p.FileIndex))
            .OrderByDescending(p => p.Height > 0.01)
            .ThenBy(p => Math.Abs(p.FileIndex - (int)(State.Id % 31)))
            .FirstOrDefault();
        if (place is null)
        {
            return false;
        }

        var floor = PlaceFloor(place);
        var target = NearestPoint(paths, floor);
        if (target < 0)
        {
            return false;
        }
        var route = Route(paths, entry.Value.PathPoint, target);
        if (route.Length == 0)
        {
            return false;
        }

        _entry = entry.Value.PathPoint;
        _entryOrdinal = entry.Key;
        _place = place;
        _route = route;
        reserved.Add(place.FileIndex);
        return true;
    }

    private static int NearestPoint(OmsiVehiclePathNetwork paths, Vector3 target)
    {
        var best = -1;
        var bestD = float.PositiveInfinity;
        for (var i = 0; i < paths.Points.Count; i++)
        {
            var p = V(paths.Points[i]);
            var d = Vector2.Distance(new(p.X, p.Y), new(target.X, target.Y))
                    + MathF.Abs(p.Z - target.Z) * 3;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    private static int[] Route(OmsiVehiclePathNetwork paths, int start, int target)
    {
        if (start < 0 || target < 0 ||
            start >= paths.Points.Count || target >= paths.Points.Count)
        {
            return [];
        }
        var previous = Enumerable.Repeat(-1, paths.Points.Count).ToArray();
        var seen = new bool[paths.Points.Count];
        var queue = new Queue<int>();
        queue.Enqueue(start);
        seen[start] = true;
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at == target)
            {
                break;
            }
            foreach (var link in paths.Links)
            {
                if (link.From == at)
                {
                    Visit(link.To);
                }
                if (!link.OneWay && link.To == at)
                {
                    Visit(link.From);
                }
            }
            void Visit(int next)
            {
                if (next < 0 || next >= seen.Length || seen[next])
                {
                    return;
                }
                seen[next] = true;
                previous[next] = at;
                queue.Enqueue(next);
            }
        }
        if (!seen[target])
        {
            return [];
        }
        var route = new List<int>();
        for (var p = target; p >= 0; p = previous[p])
        {
            route.Add(p);
            if (p == start) break;
        }
        route.Reverse();
        return route.ToArray();
    }

    private static Vector3 PlaceFloor(OmsiPassengerCabinPosition p)
    {
        var pos = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        if (p.Height <= 0.01)
        {
            return pos;
        }
        var r = (float)(p.RotationDegrees * Math.PI / 180.0);
        return new(
            pos.X + MathF.Sin(r) * SeatFront,
            pos.Y + MathF.Cos(r) * SeatFront,
            pos.Z - (float)p.Height);
    }

    private static Vector3 Move(Vector3 from, Vector3 to, float amount, out bool reached)
    {
        var d = to - from;
        var len = d.Length();
        if (len <= Reach || len <= amount || len <= 0.0001f)
        {
            reached = true;
            return to;
        }
        reached = false;
        return from + d / len * Math.Max(amount, 0);
    }

    private static Vector3 LocalToWorld(OpenOmsiLanPose bus, Vector3 local)
    {
        var r = bus.HeadingDegrees * MathF.PI / 180.0f;
        var s = MathF.Sin(r);
        var c = MathF.Cos(r);
        return new(
            (float)bus.X + local.X * c + local.Y * s,
            (float)bus.Y - local.X * s + local.Y * c,
            (float)bus.Z + local.Z);
    }

    private static Vector3 V(OmsiVehiclePathPoint p) =>
        new((float)p.X, (float)p.Y, (float)p.Z);

    private static float Heading(Vector3 delta, float fallback)
    {
        if (delta.X * delta.X + delta.Y * delta.Y < 0.000001f)
        {
            return fallback;
        }
        return Normalize(MathF.Atan2(delta.X, delta.Y) * 180.0f / MathF.PI);
    }

    private static float Normalize(float v)
    {
        v %= 360;
        if (v < -180) v += 360;
        if (v > 180) v -= 360;
        return v;
    }
}
