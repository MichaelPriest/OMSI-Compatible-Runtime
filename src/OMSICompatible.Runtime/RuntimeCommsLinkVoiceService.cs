using System.Collections.Concurrent;
using NAudio.Wave;
using OMSICompatible.Multiplayer;

namespace OMSICompatible.Runtime;

internal sealed class RuntimeCommsLinkVoiceService :
    IDisposable
{
    private sealed class RemoteVoicePlayer :
        IDisposable
    {
        private readonly BufferedWaveProvider _buffer;
        private readonly WaveOutEvent _output;

        public RemoteVoicePlayer(
            WaveFormat format)
        {
            _buffer =
                new BufferedWaveProvider(
                    format)
                {
                    BufferDuration =
                        TimeSpan.FromMilliseconds(
                            500),
                    DiscardOnBufferOverflow =
                        true
                };

            _output =
                new WaveOutEvent
                {
                    DesiredLatency =
                        80,
                    NumberOfBuffers =
                        3
                };

            _output.Init(
                _buffer);
            _output.Play();
        }

        public void Push(
            byte[] pcm)
        {
            if (pcm.Length == 0)
            {
                return;
            }

            _buffer.AddSamples(
                pcm,
                0,
                pcm.Length);
        }

        public void Dispose()
        {
            try
            {
                _output.Stop();
            }
            catch
            {
            }

            _output.Dispose();
        }
    }

    private static readonly WaveFormat VoiceFormat =
        new(
            8000,
            16,
            1);

    private readonly ConcurrentQueue<byte[]>
        _outgoing =
            new();

    private readonly Dictionary<uint, RemoteVoicePlayer>
        _remotePlayers =
            [];

    private WaveInEvent? _capture;
    private bool _transmitting;
    private bool _disposed;

    public bool IsTransmitting =>
        _transmitting;

    public string? LastError
    {
        get;
        private set;
    }

    public bool StartTransmit()
    {
        if (_disposed)
        {
            return false;
        }

        if (_transmitting)
        {
            return true;
        }

        try
        {
            var capture =
                new WaveInEvent
                {
                    WaveFormat =
                        VoiceFormat,
                    BufferMilliseconds =
                        20,
                    NumberOfBuffers =
                        3
                };

            capture.DataAvailable +=
                OnDataAvailable;
            capture.RecordingStopped +=
                OnRecordingStopped;
            capture.StartRecording();

            _capture =
                capture;
            _transmitting =
                true;
            LastError =
                null;

            return true;
        }
        catch (Exception exception)
        {
            LastError =
                exception.Message;
            _transmitting =
                false;
            DisposeCapture();
            return false;
        }
    }

    public void StopTransmit()
    {
        if (!_transmitting &&
            _capture is null)
        {
            return;
        }

        _transmitting =
            false;

        var capture =
            _capture;

        _capture =
            null;

        if (capture is null)
        {
            return;
        }

        try
        {
            capture.StopRecording();
        }
        catch
        {
        }

        capture.DataAvailable -=
            OnDataAvailable;
        capture.RecordingStopped -=
            OnRecordingStopped;
        capture.Dispose();
    }

    public bool TryDequeueOutgoing(
        out byte[] pcm) =>
        _outgoing.TryDequeue(
            out pcm!);

    public void Play(
        OpenOmsiLanVoiceFrame frame)
    {
        if (_disposed ||
            frame.Pcm16Mono8Khz.Length == 0)
        {
            return;
        }

        try
        {
            if (!_remotePlayers.TryGetValue(
                    frame.SenderId,
                    out var player))
            {
                player =
                    new RemoteVoicePlayer(
                        VoiceFormat);

                _remotePlayers[
                    frame.SenderId] =
                    player;
            }

            player.Push(
                frame.Pcm16Mono8Khz);

            LastError =
                null;
        }
        catch (Exception exception)
        {
            LastError =
                exception.Message;
        }
    }

    private void OnDataAvailable(
        object? sender,
        WaveInEventArgs e)
    {
        if (!_transmitting ||
            e.BytesRecorded <= 0)
        {
            return;
        }

        var offset =
            0;

        while (offset <
               e.BytesRecorded)
        {
            var remaining =
                e.BytesRecorded -
                offset;

            var count =
                Math.Min(
                    remaining,
                    OpenOmsiLanVoiceCodec.MaximumPcmBytes);

            count &=
                ~1;

            if (count <= 0)
            {
                break;
            }

            var packet =
                new byte[count];

            Buffer.BlockCopy(
                e.Buffer,
                offset,
                packet,
                0,
                count);

            _outgoing.Enqueue(
                packet);

            offset +=
                count;
        }

        while (_outgoing.Count >
               20 &&
               _outgoing.TryDequeue(
                   out _))
        {
        }
    }

    private void OnRecordingStopped(
        object? sender,
        StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            LastError =
                e.Exception.Message;
        }
    }

    private void DisposeCapture()
    {
        var capture =
            _capture;

        _capture =
            null;

        if (capture is null)
        {
            return;
        }

        try
        {
            capture.DataAvailable -=
                OnDataAvailable;
            capture.RecordingStopped -=
                OnRecordingStopped;
            capture.Dispose();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopTransmit();
        _disposed =
            true;

        foreach (var player in
                 _remotePlayers.Values)
        {
            player.Dispose();
        }

        _remotePlayers.Clear();

        while (_outgoing.TryDequeue(
                   out _))
        {
        }
    }
}
