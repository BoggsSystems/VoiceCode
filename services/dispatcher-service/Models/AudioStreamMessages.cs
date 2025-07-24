using System;
using System.Collections.Generic;

namespace VoiceCode.DispatcherService.Models
{
    public enum AudioStreamMessageType
    {
        AudioData,
        Control,
        Metadata,
        Heartbeat,
        Error,
        Acknowledgment
    }

    public enum ControlCommand
    {
        StartStream,
        StopStream,
        PauseStream,
        ResumeStream,
        ResetStream
    }

    public class AudioStreamMessage
    {
        public AudioStreamMessageType Type { get; set; }
        public string SessionId { get; set; }
        public DateTime Timestamp { get; set; }
        public object Payload { get; set; }
    }

    public class AudioDataPayload
    {
        public byte[] AudioData { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitDepth { get; set; }
        public long SequenceNumber { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class AudioDataPayloadWithVAD : AudioDataPayload
    {
        public Dictionary<string, object> Metadata { get; set; }
    }

    public class ControlPayload
    {
        public ControlCommand Command { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }

    public class MetadataPayload
    {
        public string Key { get; set; }
        public object Value { get; set; }
        public Dictionary<string, object> Attributes { get; set; }
    }

    public class ErrorPayload
    {
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public bool IsRecoverable { get; set; }
    }

    public class HeartbeatPayload
    {
        public long ClientTimestamp { get; set; }
        public long ServerTimestamp { get; set; }
        public Dictionary<string, object> Stats { get; set; }
    }

    public class AcknowledgmentPayload
    {
        public long AcknowledgedSequence { get; set; }
        public TimeSpan ProcessingLatency { get; set; }
    }

    public class AudioStreamConfig
    {
        public int SampleRate { get; set; } = 16000;
        public int Channels { get; set; } = 1;
        public int BitDepth { get; set; } = 16;
        public int ChunkDurationMs { get; set; } = 100;
        public int BufferSizeMs { get; set; } = 2000;
        public int HeartbeatIntervalMs { get; set; } = 5000;
        public int ReconnectDelayMs { get; set; } = 1000;
        public int MaxReconnectAttempts { get; set; } = 5;
    }
}