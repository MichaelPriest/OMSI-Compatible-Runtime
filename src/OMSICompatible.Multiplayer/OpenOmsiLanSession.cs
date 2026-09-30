using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace OMSICompatible.Multiplayer;

public enum OpenOmsiLanRole
{
    Host,
    Client
}

public sealed record OpenOmsiLanDiscoveryResult(
    IPEndPoint Endpoint,
    string HostName,
    ulong Session,
    string Map,
    int Players);

public sealed record OpenOmsiLanPeerSnapshot(
    uint Id,
    string Name,
    IPEndPoint? Endpoint,
    OpenOmsiLanPose Pose,
    DateTimeOffset LastSeen,
    bool HasInfo,
    bool HasState,
    uint DroppedStates);

/// <summary>
/// Minimal interoperable openOMSI protocol-5 LAN session.
/// It deliberately implements the transport/vehicle layer first: host/join,
/// broadcast discovery, INFO, STATE, WELCOME/REJECT/BYE and timeouts.
/// Shared traffic/passengers/world frames and internet bridge are layered on
/// later without changing this transport API.
/// </summary>
public sealed class OpenOmsiLanSession :
    IDisposable
{
    private sealed class Peer
    {
        public required uint Id { get; init; }
        public required IPEndPoint Endpoint { get; set; }
        public required OpenOmsiLanPose Pose { get; set; }
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
        public bool HasInfo { get; set; }
        public bool HasState { get; set; }
        public ushort LastSequence { get; set; }
        public uint DroppedStates { get; set; }
    }

    private readonly Socket _socket;
    private readonly Dictionary<uint, Peer> _peers = [];
    private readonly byte[] _receiveBuffer =
        new byte[
            OpenOmsiLanProtocol.MaximumDatagramBytes + 1];
    private readonly ulong _nonce =
        CreateNonce();

    private IPEndPoint? _host;
    private uint _nextId = 2;
    private ushort _sequence;
    private double _sendAccumulator;
    private double _helloAccumulator = 1.0;
    private double _infoAccumulator =
        OpenOmsiLanProtocol.InfoEverySeconds;
    private double _unchangedSeconds;
    private byte[] _lastStateBody = [];
    private string _lastInfo = string.Empty;
    private bool _disposed;

    private OpenOmsiLanSession(
        Socket socket,
        OpenOmsiLanRole role,
        string name,
        OpenOmsiLanWorld world)
    {
        _socket = socket;
        Role = role;
        PlayerName =
            string.IsNullOrWhiteSpace(name)
                ? "Driver"
                : OpenOmsiLanProtocol.CleanText(
                    name,
                    OpenOmsiLanProtocol.MaximumNameCharacters);
        World = world;
        PlayerId =
            role == OpenOmsiLanRole.Host
                ? 1u
                : 0u;
        Connected =
            role == OpenOmsiLanRole.Host;
    }

    public OpenOmsiLanRole Role { get; }

    public string PlayerName { get; }

    public OpenOmsiLanWorld World { get; private set; }

    public uint PlayerId { get; private set; }

    public ulong SessionId { get; private set; }

    public bool Connected { get; private set; }

    public string? RejectionReason { get; private set; }

    public IPEndPoint? HostEndpoint => _host;

    public int LocalPort =>
        (_socket.LocalEndPoint as IPEndPoint)?.Port ??
        0;

    public string SessionHex =>
        OpenOmsiLanProtocol.SessionHex(
            SessionId);

    public event Action<OpenOmsiLanOperationalMessage>?
        OperationalMessageReceived;

    public static OpenOmsiLanSession Host(
        int port,
        string playerName,
        OpenOmsiLanWorld world,
        bool tryNextPorts = true)
    {
        Exception? lastError =
            null;

        var tries =
            tryNextPorts
                ? OpenOmsiLanProtocol.PortRange
                : 1;

        for (var offset = 0; offset < tries; offset++)
        {
            var candidate =
                port == 0 &&
                !tryNextPorts
                    ? 0
                    : Math.Clamp(
                        port + offset,
                        1,
                        ushort.MaxValue);

            Socket? socket =
                null;

            try
            {
                socket =
                    CreateSocket();

                socket.Bind(
                    new IPEndPoint(
                        IPAddress.Any,
                        candidate));

                var session =
                    new OpenOmsiLanSession(
                        socket,
                        OpenOmsiLanRole.Host,
                        playerName,
                        world)
                    {
                        SessionId =
                            CreateSessionId()
                    };

                return session;
            }
            catch (Exception exception)
            {
                lastError =
                    exception;

                socket?.Dispose();
            }
        }

        throw new IOException(
            "Unable to host an openOMSI LAN session.",
            lastError);
    }

    public static OpenOmsiLanSession Join(
        string target,
        string playerName,
        OpenOmsiLanWorld world)
    {
        var endpoint =
            ResolveTarget(target);

        var socket =
            CreateSocket();

        socket.Bind(
            new IPEndPoint(
                IPAddress.Any,
                0));

        return new OpenOmsiLanSession(
            socket,
            OpenOmsiLanRole.Client,
            playerName,
            world)
        {
            _host =
                endpoint
        };
    }

    public static OpenOmsiLanDiscoveryResult? Discover(
        TimeSpan timeout,
        int port = OpenOmsiLanProtocol.DefaultPort)
    {
        using var socket =
            new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);

        socket.EnableBroadcast =
            true;

        socket.Bind(
            new IPEndPoint(
                IPAddress.Any,
                0));

        socket.ReceiveTimeout =
            200;

        var request =
            Encoding.UTF8.GetBytes(
                $"DISCOVER|{OpenOmsiLanProtocol.ProtocolVersion}");

        var deadline =
            DateTimeOffset.UtcNow +
            timeout;

        var targets =
            new List<IPEndPoint>
            {
                new(
                    IPAddress.Broadcast,
                    port)
            };

        for (var offset = 0;
             offset <
                 OpenOmsiLanProtocol.PortRange;
             offset++)
        {
            targets.Add(
                new IPEndPoint(
                    IPAddress.Loopback,
                    port + offset));
        }

        var buffer =
            new byte[1024];

        while (DateTimeOffset.UtcNow <
               deadline)
        {
            foreach (var target in
                     targets)
            {
                try
                {
                    socket.SendTo(
                        request,
                        target);
                }
                catch
                {
                }
            }

            EndPoint remote =
                new IPEndPoint(
                    IPAddress.Any,
                    0);

            try
            {
                var count =
                    socket.ReceiveFrom(
                        buffer,
                        ref remote);

                var text =
                    Encoding.UTF8.GetString(
                        buffer,
                        0,
                        count);

                var fields =
                    text.Split('|');

                if (fields.Length < 6 ||
                    !fields[0].Equals(
                        "HERE",
                        StringComparison.Ordinal) ||
                    !byte.TryParse(
                        fields[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var protocol) ||
                    protocol !=
                        OpenOmsiLanProtocol.ProtocolVersion ||
                    !ulong.TryParse(
                        fields[3],
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var session) ||
                    !int.TryParse(
                        fields[5],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var players) ||
                    remote is not IPEndPoint endpoint)
                {
                    continue;
                }

                return new OpenOmsiLanDiscoveryResult(
                    endpoint,
                    OpenOmsiLanProtocol.CleanText(
                        fields[2],
                        OpenOmsiLanProtocol.MaximumNameCharacters),
                    session,
                    OpenOmsiLanProtocol.CleanText(
                        fields[4],
                        260),
                    Math.Clamp(
                        players,
                        1,
                        OpenOmsiLanProtocol.MaximumPeers + 1));
            }
            catch (SocketException exception)
                when (exception.SocketErrorCode is
                      SocketError.TimedOut or
                      SocketError.WouldBlock)
            {
            }
        }

        return null;
    }

    public IReadOnlyList<OpenOmsiLanPeerSnapshot> SnapshotPeers()
    {
        return
            _peers
                .Values
                .OrderBy(
                    static peer =>
                        peer.Id)
                .Select(
                    static peer =>
                        new OpenOmsiLanPeerSnapshot(
                            peer.Id,
                            peer.Pose.Name,
                            peer.Endpoint,
                            peer.Pose.Clone(),
                            peer.LastSeen,
                            peer.HasInfo,
                            peer.HasState,
                            peer.DroppedStates))
                .ToArray();
    }

    public void SetWorld(OpenOmsiLanWorld world)
    {
        World =
            world;
    }

    public bool SendOperationalMessage(
        OpenOmsiLanOperationalMessage message)
    {
        ThrowIfDisposed();

        if (!Connected)
        {
            return false;
        }

        var outbound =
            message with
            {
                SenderId =
                    PlayerId,
                SenderName =
                    PlayerName,
                TimestampUnixMilliseconds =
                    message.TimestampUnixMilliseconds >
                        0
                        ? message.TimestampUnixMilliseconds
                        : DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds()
            };

        var text =
            OpenOmsiLanOperationalCodec.Encode(
                outbound);

        if (Role == OpenOmsiLanRole.Host)
        {
            BroadcastText(
                text,
                except:
                    null);

            OperationalMessageReceived?.Invoke(
                outbound);

            return true;
        }

        if (_host is null)
        {
            return false;
        }

        SendText(
            text,
            _host);

        return true;
    }

    public void Tick(
        double deltaSeconds,
        OpenOmsiLanPose localPose)
    {
        ThrowIfDisposed();

        var dt =
            double.IsFinite(deltaSeconds)
                ? Math.Clamp(
                    deltaSeconds,
                    0.0,
                    0.5)
                : 0.0;

        _sendAccumulator += dt;
        _helloAccumulator += dt;
        _infoAccumulator += dt;

        if (Role == OpenOmsiLanRole.Client &&
            !Connected &&
            RejectionReason is null &&
            _helloAccumulator >= 1.0)
        {
            _helloAccumulator =
                0.0;

            SendHello(
                localPose);
        }

        if (Connected)
        {
            SendOwn(
                dt,
                localPose);
        }

        ReceiveAvailable();

        var now =
            DateTimeOffset.UtcNow;

        var stale =
            _peers
                .Where(
                    pair =>
                        (
                            now -
                            pair.Value.LastSeen
                        ).TotalSeconds >
                        OpenOmsiLanProtocol.PeerTimeoutSeconds)
                .Select(
                    static pair =>
                        pair.Key)
                .ToArray();

        foreach (var id in stale)
        {
            _peers.Remove(id);

            if (Role == OpenOmsiLanRole.Host)
            {
                BroadcastText(
                    $"BYE|{id}",
                    except:
                        null);
            }
        }
    }

    public void Leave()
    {
        if (_disposed)
        {
            return;
        }

        var message =
            $"BYE|{PlayerId}";

        if (Role == OpenOmsiLanRole.Host)
        {
            BroadcastText(
                message,
                except:
                    null);
        }
        else if (Connected &&
                 _host is not null)
        {
            SendText(
                message,
                _host);
        }

        Connected =
            false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Leave();

        _disposed =
            true;

        _socket.Dispose();
    }

    private static Socket CreateSocket()
    {
        var socket =
            new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp)
            {
                Blocking =
                    false,
                EnableBroadcast =
                    true
            };

        return socket;
    }

    private void SendOwn(
        double deltaSeconds,
        OpenOmsiLanPose localPose)
    {
        var pose =
            localPose.Clone();

        pose.Id =
            PlayerId;
        pose.Name =
            PlayerName;

        if (string.IsNullOrWhiteSpace(
                pose.VehiclePath))
        {
            pose.Flags &=
                ~OpenOmsiLanProtocol.FlagVehicle;
        }

        pose.SentMilliseconds =
            0;

        var candidate =
            OpenOmsiLanStateCodec.Encode(
                pose,
                _sequence);

        var body =
            candidate[
                OpenOmsiLanProtocol.StateHeaderBytes..];

        if (_lastStateBody.AsSpan()
            .SequenceEqual(body))
        {
            _unchangedSeconds +=
                deltaSeconds;
        }
        else
        {
            _unchangedSeconds =
                0.0;
        }

        var interval =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagVehicle) ==
                    0
                ? OpenOmsiLanProtocol.HeartbeatSeconds
                : _unchangedSeconds >
                    1.0
                    ? 1.0 /
                        OpenOmsiLanProtocol.IdleStateRateHz
                    : 1.0 /
                        OpenOmsiLanProtocol.StateRateHz;

        if (_sendAccumulator +
                0.0001 >=
            interval)
        {
            _sendAccumulator =
                Math.Clamp(
                    _sendAccumulator -
                    interval,
                    0.0,
                    interval);

            _lastStateBody =
                body.ToArray();

            _sequence++;

            pose.SentMilliseconds =
                unchecked(
                    (uint)Environment.TickCount64);

            var data =
                OpenOmsiLanStateCodec.Encode(
                    pose,
                    _sequence);

            if (Role == OpenOmsiLanRole.Host)
            {
                Broadcast(
                    data,
                    except:
                        null);
            }
            else if (_host is not null)
            {
                Send(
                    data,
                    _host);
            }
        }

        var info =
            OpenOmsiLanProtocol.EncodeInfo(
                pose);

        if (!info.Equals(
                _lastInfo,
                StringComparison.Ordinal) ||
            _infoAccumulator >=
                OpenOmsiLanProtocol.InfoEverySeconds)
        {
            _infoAccumulator =
                0.0;

            if (Role == OpenOmsiLanRole.Host)
            {
                BroadcastText(
                    info,
                    except:
                        null);
            }
            else if (_host is not null)
            {
                SendText(
                    info,
                    _host);
            }

            _lastInfo =
                info;
        }
    }

    private void SendHello(
        OpenOmsiLanPose pose)
    {
        if (_host is null)
        {
            return;
        }

        var path =
            OpenOmsiLanProtocol.NormalizeVehiclePath(
                pose.VehiclePath) ??
            string.Empty;

        SendText(
            string.Join(
                "|",
                "HELLO",
                OpenOmsiLanProtocol.ProtocolVersion.ToString(
                    CultureInfo.InvariantCulture),
                "-",
                PlayerName,
                path,
                OpenOmsiLanProtocol.EncodeWorld(
                    World),
                _nonce.ToString(
                    "X16",
                    CultureInfo.InvariantCulture)),
            _host);
    }

    private void ReceiveAvailable()
    {
        while (true)
        {
            EndPoint remote =
                new IPEndPoint(
                    IPAddress.Any,
                    0);

            int count;

            try
            {
                count =
                    _socket.ReceiveFrom(
                        _receiveBuffer,
                        ref remote);
            }
            catch (SocketException exception)
                when (exception.SocketErrorCode is
                      SocketError.WouldBlock or
                      SocketError.IOPending or
                      SocketError.NoBufferSpaceAvailable)
            {
                break;
            }

            if (count <= 0 ||
                count >
                    OpenOmsiLanProtocol.MaximumDatagramBytes ||
                remote is not IPEndPoint endpoint)
            {
                continue;
            }

            var data =
                _receiveBuffer.AsSpan(
                    0,
                    count);

            if (Role == OpenOmsiLanRole.Client &&
                Connected &&
                _host is not null &&
                !endpoint.Equals(_host))
            {
                continue;
            }

            if (data[0] ==
                OpenOmsiLanProtocol.StateMagic)
            {
                HandleState(
                    data,
                    endpoint);

                continue;
            }

            string text;

            try
            {
                text =
                    Encoding.UTF8.GetString(
                        data);
            }
            catch
            {
                continue;
            }

            HandleText(
                text,
                endpoint);
        }
    }

    private void HandleText(
        string text,
        IPEndPoint endpoint)
    {
        var fields =
            text.Split('|');

        if (fields.Length == 0)
        {
            return;
        }

        switch (fields[0])
        {
            case "DISCOVER"
                when Role ==
                     OpenOmsiLanRole.Host:
                if (fields.Length >
                        1 &&
                    byte.TryParse(
                        fields[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var discoverProtocol) &&
                    discoverProtocol ==
                        OpenOmsiLanProtocol.ProtocolVersion)
                {
                    SendText(
                        string.Join(
                            "|",
                            "HERE",
                            OpenOmsiLanProtocol.ProtocolVersion.ToString(
                                CultureInfo.InvariantCulture),
                            PlayerName,
                            SessionHex,
                            OpenOmsiLanProtocol.CleanText(
                                World.Map,
                                260),
                            (_peers.Count + 1).ToString(
                                CultureInfo.InvariantCulture)),
                        endpoint);
                }

                break;

            case "HELLO"
                when Role ==
                     OpenOmsiLanRole.Host:
                HandleHello(
                    fields,
                    endpoint);
                break;

            case "WELCOME"
                when Role ==
                     OpenOmsiLanRole.Client:
                HandleWelcome(
                    fields,
                    endpoint);
                break;

            case "REJECT"
                when Role ==
                     OpenOmsiLanRole.Client:
                RejectionReason =
                    fields.Length > 2
                        ? OpenOmsiLanProtocol.CleanText(
                            fields[2],
                            300)
                        : "Host rejected the connection.";
                Connected =
                    false;
                break;

            case "INFO":
                HandleInfo(
                    text,
                    endpoint);
                break;

            case OpenOmsiLanOperationalCodec.Prefix:
                HandleOperationalMessage(
                    text,
                    endpoint);
                break;

            case "BYE":
                HandleBye(
                    fields,
                    endpoint);
                break;
        }
    }

    private void HandleOperationalMessage(
        string text,
        IPEndPoint endpoint)
    {
        if (!OpenOmsiLanOperationalCodec.TryDecode(
                text,
                out var message) ||
            message.SenderId ==
                PlayerId)
        {
            return;
        }

        if (Role == OpenOmsiLanRole.Host)
        {
            if (!_peers.TryGetValue(
                    message.SenderId,
                    out var peer) ||
                !peer.Endpoint.Equals(
                    endpoint))
            {
                return;
            }

            peer.LastSeen =
                DateTimeOffset.UtcNow;

            BroadcastText(
                text,
                except:
                    endpoint);

            OperationalMessageReceived?.Invoke(
                message);

            return;
        }

        if (_host is null ||
            !endpoint.Equals(
                _host))
        {
            return;
        }

        OperationalMessageReceived?.Invoke(
            message);
    }

    private void HandleHello(
        IReadOnlyList<string> fields,
        IPEndPoint endpoint)
    {
        if (fields.Count < 11 ||
            !byte.TryParse(
                fields[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var protocol) ||
            protocol !=
                OpenOmsiLanProtocol.ProtocolVersion)
        {
            SendText(
                $"REJECT|{OpenOmsiLanProtocol.ProtocolVersion}|the host runs LAN protocol {OpenOmsiLanProtocol.ProtocolVersion}",
                endpoint);

            return;
        }

        var asked =
            fields[2];

        if (!string.IsNullOrWhiteSpace(asked) &&
            asked != "-" &&
            (!ulong.TryParse(
                 asked,
                 NumberStyles.HexNumber,
                 CultureInfo.InvariantCulture,
                 out var requestedSession) ||
             requestedSession !=
                 SessionId))
        {
            SendText(
                $"REJECT|{OpenOmsiLanProtocol.ProtocolVersion}|wrong session code",
                endpoint);

            return;
        }

        var existing =
            _peers
                .Values
                .FirstOrDefault(
                    peer =>
                        peer.Endpoint.Equals(
                            endpoint));

        if (existing is null &&
            _peers.Count >=
                OpenOmsiLanProtocol.MaximumPeers)
        {
            SendText(
                $"REJECT|{OpenOmsiLanProtocol.ProtocolVersion}|the session is full",
                endpoint);

            return;
        }

        var id =
            existing?.Id ??
            NextPlayerId();

        if (existing is null)
        {
            existing =
                new Peer
                {
                    Id =
                        id,
                    Endpoint =
                        endpoint,
                    Pose =
                        new OpenOmsiLanPose
                        {
                            Id =
                                id,
                            Name =
                                OpenOmsiLanProtocol.CleanText(
                                    fields[3],
                                    OpenOmsiLanProtocol.MaximumNameCharacters),
                            VehiclePath =
                                OpenOmsiLanProtocol.NormalizeVehiclePath(
                                    fields[4]) ??
                                string.Empty
                        }
                };

            _peers[id] =
                existing;
        }
        else
        {
            existing.Endpoint =
                endpoint;
        }

        existing.LastSeen =
            DateTimeOffset.UtcNow;

        SendText(
            string.Join(
                "|",
                "WELCOME",
                OpenOmsiLanProtocol.ProtocolVersion.ToString(
                    CultureInfo.InvariantCulture),
                id.ToString(
                    CultureInfo.InvariantCulture),
                SessionHex,
                PlayerName,
                OpenOmsiLanProtocol.EncodeWorld(
                    World),
                (_peers.Count + 1).ToString(
                    CultureInfo.InvariantCulture)),
            endpoint);
    }

    private void HandleWelcome(
        IReadOnlyList<string> fields,
        IPEndPoint endpoint)
    {
        if (fields.Count < 11 ||
            !byte.TryParse(
                fields[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var protocol) ||
            protocol !=
                OpenOmsiLanProtocol.ProtocolVersion ||
            !uint.TryParse(
                fields[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var id) ||
            id is < 2 or > ushort.MaxValue ||
            !ulong.TryParse(
                fields[3],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var session))
        {
            return;
        }

        PlayerId =
            id;

        SessionId =
            session;

        _host =
            endpoint;

        World =
            OpenOmsiLanProtocol.DecodeWorld(
                fields,
                5);

        Connected =
            true;

        RejectionReason =
            null;
    }

    private void HandleInfo(
        string text,
        IPEndPoint endpoint)
    {
        if (!OpenOmsiLanProtocol.TryDecodeInfo(
                text,
                out var info) ||
            info.Id == PlayerId)
        {
            return;
        }

        if (Role == OpenOmsiLanRole.Host)
        {
            if (!_peers.TryGetValue(
                    info.Id,
                    out var peer) ||
                !peer.Endpoint.Equals(
                    endpoint))
            {
                return;
            }

            MergeInfo(
                peer.Pose,
                info);

            peer.HasInfo =
                true;

            peer.LastSeen =
                DateTimeOffset.UtcNow;

            BroadcastText(
                text,
                except:
                    endpoint);

            return;
        }

        if (_host is null ||
            !endpoint.Equals(_host))
        {
            return;
        }

        var clientPeer =
            GetOrCreateRelayedPeer(
                info.Id);

        MergeInfo(
            clientPeer.Pose,
            info);

        clientPeer.HasInfo =
            true;

        clientPeer.LastSeen =
            DateTimeOffset.UtcNow;
    }

    private void HandleState(
        ReadOnlySpan<byte> data,
        IPEndPoint endpoint)
    {
        if (!OpenOmsiLanStateCodec.TryDecode(
                data,
                out var sequence,
                out var state) ||
            state.Id == PlayerId)
        {
            return;
        }

        if (Role == OpenOmsiLanRole.Host)
        {
            if (!_peers.TryGetValue(
                    state.Id,
                    out var peer) ||
                !peer.Endpoint.Equals(
                    endpoint))
            {
                return;
            }

            if (peer.HasState &&
                !OpenOmsiLanProtocol.SequenceIsNewer(
                    sequence,
                    peer.LastSequence))
            {
                peer.DroppedStates++;
                return;
            }

            MergeState(
                peer.Pose,
                state);

            peer.HasState =
                true;

            peer.LastSequence =
                sequence;

            peer.LastSeen =
                DateTimeOffset.UtcNow;

            Broadcast(
                data.ToArray(),
                except:
                    endpoint);

            return;
        }

        if (_host is null ||
            !endpoint.Equals(_host))
        {
            return;
        }

        var clientPeer =
            GetOrCreateRelayedPeer(
                state.Id);

        if (clientPeer.HasState &&
            !OpenOmsiLanProtocol.SequenceIsNewer(
                sequence,
                clientPeer.LastSequence))
        {
            clientPeer.DroppedStates++;
            return;
        }

        MergeState(
            clientPeer.Pose,
            state);

        clientPeer.HasState =
            true;

        clientPeer.LastSequence =
            sequence;

        clientPeer.LastSeen =
            DateTimeOffset.UtcNow;
    }

    private void HandleBye(
        IReadOnlyList<string> fields,
        IPEndPoint endpoint)
    {
        if (fields.Count < 2 ||
            !uint.TryParse(
                fields[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var id))
        {
            return;
        }

        if (Role == OpenOmsiLanRole.Host)
        {
            if (_peers.TryGetValue(
                    id,
                    out var peer) &&
                peer.Endpoint.Equals(
                    endpoint))
            {
                _peers.Remove(id);

                BroadcastText(
                    $"BYE|{id}",
                    except:
                        endpoint);
            }

            return;
        }

        if (_host is null ||
            !endpoint.Equals(_host))
        {
            return;
        }

        if (id == 1)
        {
            Connected =
                false;
        }

        _peers.Remove(id);
    }

    private Peer GetOrCreateRelayedPeer(uint id)
    {
        if (_peers.TryGetValue(
                id,
                out var peer))
        {
            return peer;
        }

        peer =
            new Peer
            {
                Id =
                    id,
                Endpoint =
                    _host ??
                    new IPEndPoint(
                        IPAddress.None,
                        0),
                Pose =
                    new OpenOmsiLanPose
                    {
                        Id =
                            id
                    }
            };

        _peers[id] =
            peer;

        return peer;
    }

    private uint NextPlayerId()
    {
        while (true)
        {
            var id =
                _nextId;

            _nextId =
                _nextId >= ushort.MaxValue
                    ? 2u
                    : _nextId + 1u;

            if (!_peers.ContainsKey(id))
            {
                return id;
            }
        }
    }

    private void SendText(
        string text,
        IPEndPoint endpoint) =>
        Send(
            Encoding.UTF8.GetBytes(text),
            endpoint);

    private void BroadcastText(
        string text,
        IPEndPoint? except) =>
        Broadcast(
            Encoding.UTF8.GetBytes(text),
            except);

    private void Broadcast(
        byte[] data,
        IPEndPoint? except)
    {
        foreach (var peer in
                 _peers.Values)
        {
            if (except is not null &&
                peer.Endpoint.Equals(except))
            {
                continue;
            }

            Send(
                data,
                peer.Endpoint);
        }
    }

    private void Send(
        byte[] data,
        IPEndPoint endpoint)
    {
        if (data.Length == 0 ||
            data.Length >
                OpenOmsiLanProtocol.MaximumDatagramBytes)
        {
            return;
        }

        try
        {
            _socket.SendTo(
                data,
                SocketFlags.None,
                endpoint);
        }
        catch (SocketException exception)
            when (exception.SocketErrorCode is
                  SocketError.WouldBlock or
                  SocketError.NoBufferSpaceAvailable or
                  SocketError.ConnectionReset or
                  SocketError.ConnectionRefused)
        {
        }
    }

    private static void MergeInfo(
        OpenOmsiLanPose target,
        OpenOmsiLanPose info)
    {
        target.Name =
            info.Name;
        target.VehiclePath =
            info.VehiclePath;
        target.Paint =
            info.Paint;
        target.Line =
            info.Line;
        target.Destination =
            info.Destination;
        target.Tour =
            info.Tour;
        target.DisplayTexts =
            [.. info.DisplayTexts];
        target.FigurePath =
            info.FigurePath;
        target.LengthMeters =
            info.LengthMeters;
        target.WidthMeters =
            info.WidthMeters;
        target.BoxOffsetMeters =
            info.BoxOffsetMeters;
        target.SyncTableHash =
            info.SyncTableHash;
    }

    private static void MergeState(
        OpenOmsiLanPose target,
        OpenOmsiLanPose state)
    {
        target.Id =
            state.Id;
        target.X =
            state.X;
        target.Y =
            state.Y;
        target.Z =
            state.Z;
        target.HeadingDegrees =
            state.HeadingDegrees;
        target.PitchDegrees =
            state.PitchDegrees;
        target.BankDegrees =
            state.BankDegrees;
        target.SpeedKph =
            state.SpeedKph;
        target.SteeringDegrees =
            state.SteeringDegrees;
        target.Flags =
            state.Flags;
        target.HeadLights =
            state.HeadLights;
        target.InteriorLights =
            state.InteriorLights;
        target.Blinker =
            state.Blinker;
        target.Rpm =
            state.Rpm;
        target.Throttle =
            state.Throttle;
        target.Brake =
            state.Brake;
        target.Passengers =
            state.Passengers;
        target.Doors =
            [.. state.Doors];
        target.Suspension =
            [.. state.Suspension];
        target.RearSections =
            [.. state.RearSections];
        target.Lamps =
            [.. state.Lamps];
        target.Switches =
            [.. state.Switches];
        target.Values =
            [.. state.Values];
        target.Walker =
            state.Walker;
        target.SentMilliseconds =
            state.SentMilliseconds;
    }

    private static IPEndPoint ResolveTarget(string target)
    {
        var value =
            target.Trim();

        if (int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var localPort) &&
            localPort is >= 1 and <= ushort.MaxValue)
        {
            return new IPEndPoint(
                IPAddress.Loopback,
                localPort);
        }

        var host =
            value;

        var port =
            OpenOmsiLanProtocol.DefaultPort;

        var separator =
            value.LastIndexOf(':');

        if (separator > 0 &&
            separator <
                value.Length - 1 &&
            int.TryParse(
                value[(separator + 1)..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedPort) &&
            parsedPort is >= 1 and <= ushort.MaxValue)
        {
            host =
                value[..separator];

            port =
                parsedPort;
        }

        var address =
            IPAddress.TryParse(
                host,
                out var parsed)
                ? parsed
                : Dns
                    .GetHostAddresses(host)
                    .FirstOrDefault(
                        static item =>
                            item.AddressFamily ==
                            AddressFamily.InterNetwork);

        if (address is null)
        {
            throw new ArgumentException(
                $"Host '{host}' has no IPv4 address.",
                nameof(target));
        }

        return new IPEndPoint(
            address,
            port);
    }

    private static ulong CreateSessionId()
    {
        Span<byte> bytes =
            stackalloc byte[8];

        RandomNumberGenerator.Fill(
            bytes);

        return BitConverter.ToUInt64(bytes) &
               0x0000_FFFF_FFFF_FFFFUL;
    }

    private static ulong CreateNonce()
    {
        Span<byte> bytes =
            stackalloc byte[8];

        RandomNumberGenerator.Fill(
            bytes);

        return BitConverter.ToUInt64(bytes);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}
