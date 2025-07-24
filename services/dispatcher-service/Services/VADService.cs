using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace VoiceCode.DispatcherService.Services
{
    public interface IVADService
    {
        VADResult ProcessAudioFrame(string sessionId, byte[] audioData, int sampleRate);
        void UpdateConfiguration(string sessionId, VADConfiguration config);
        VADMetrics GetMetrics(string sessionId);
        void ResetSession(string sessionId);
    }

    public class VADService : IVADService
    {
        private readonly ILogger<VADService> _logger;
        private readonly ConcurrentDictionary<string, VADSession> _sessions;

        public VADService(ILogger<VADService> logger)
        {
            _logger = logger;
            _sessions = new ConcurrentDictionary<string, VADSession>();
        }

        public VADResult ProcessAudioFrame(string sessionId, byte[] audioData, int sampleRate)
        {
            var session = _sessions.GetOrAdd(sessionId, id => new VADSession(id));
            
            // Calculate audio features
            var features = CalculateAudioFeatures(audioData, sampleRate);
            
            // Update session history
            session.AddFrame(features);
            
            // Apply noise gate
            if (features.Energy < session.Config.NoiseGateThreshold)
            {
                return new VADResult
                {
                    IsSpeech = false,
                    Confidence = 0,
                    Energy = features.Energy,
                    State = VADState.Silence
                };
            }
            
            // Calculate VAD probability based on multiple features
            var vadProbability = CalculateVADProbability(session, features);
            
            // Apply state machine logic
            var state = UpdateVADState(session, vadProbability);
            
            // Update metrics
            session.UpdateMetrics(state, features);
            
            return new VADResult
            {
                IsSpeech = state == VADState.Speech,
                Confidence = vadProbability,
                Energy = features.Energy,
                State = state,
                Features = features
            };
        }

        private AudioFeatures CalculateAudioFeatures(byte[] audioData, int sampleRate)
        {
            // Convert byte array to float samples
            var samples = new float[audioData.Length / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short sample = BitConverter.ToInt16(audioData, i * 2);
                samples[i] = sample / 32768f;
            }
            
            // Calculate RMS energy
            var energy = CalculateRMS(samples);
            
            // Calculate zero-crossing rate
            var zcr = CalculateZCR(samples);
            
            // Calculate spectral centroid (simplified)
            var centroid = CalculateSpectralCentroid(samples, sampleRate);
            
            // Calculate spectral rolloff
            var rolloff = CalculateSpectralRolloff(samples, sampleRate);
            
            return new AudioFeatures
            {
                Energy = energy,
                ZeroCrossingRate = zcr,
                SpectralCentroid = centroid,
                SpectralRolloff = rolloff,
                SampleCount = samples.Length
            };
        }

        private float CalculateRMS(float[] samples)
        {
            if (samples.Length == 0) return 0;
            
            float sum = 0;
            foreach (var sample in samples)
            {
                sum += sample * sample;
            }
            
            return (float)Math.Sqrt(sum / samples.Length);
        }

        private float CalculateZCR(float[] samples)
        {
            if (samples.Length < 2) return 0;
            
            int crossings = 0;
            for (int i = 1; i < samples.Length; i++)
            {
                if (Math.Sign(samples[i]) != Math.Sign(samples[i - 1]))
                {
                    crossings++;
                }
            }
            
            return (float)crossings / samples.Length;
        }

        private float CalculateSpectralCentroid(float[] samples, int sampleRate)
        {
            // Simplified spectral centroid calculation
            // In production, use proper FFT
            var fftSize = 512;
            var window = CreateHannWindow(Math.Min(samples.Length, fftSize));
            
            // Apply window
            var windowed = new float[window.Length];
            for (int i = 0; i < window.Length; i++)
            {
                windowed[i] = samples[i] * window[i];
            }
            
            // Simple energy distribution estimate
            float weightedSum = 0;
            float magnitudeSum = 0;
            
            for (int i = 0; i < windowed.Length / 2; i++)
            {
                var frequency = (float)i * sampleRate / windowed.Length;
                var magnitude = Math.Abs(windowed[i]);
                weightedSum += frequency * magnitude;
                magnitudeSum += magnitude;
            }
            
            return magnitudeSum > 0 ? weightedSum / magnitudeSum : 0;
        }

        private float CalculateSpectralRolloff(float[] samples, int sampleRate)
        {
            // Simplified spectral rolloff at 85%
            return 0.85f * sampleRate / 2; // Placeholder
        }

        private float[] CreateHannWindow(int size)
        {
            var window = new float[size];
            for (int i = 0; i < size; i++)
            {
                window[i] = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * i / (size - 1));
            }
            return window;
        }

        private float CalculateVADProbability(VADSession session, AudioFeatures features)
        {
            var config = session.Config;
            
            // Normalize features
            var energyScore = Math.Min(features.Energy / config.EnergyThreshold, 1.0f);
            var zcrScore = 1.0f - Math.Min(features.ZeroCrossingRate / 0.5f, 1.0f);
            var centroidScore = ScoreCentroid(features.SpectralCentroid);
            
            // Check against history
            var energyRatio = features.Energy / (session.GetAverageEnergy() + 0.001f);
            var historyScore = Math.Min(energyRatio / 3.0f, 1.0f);
            
            // Combine scores with weights
            var probability = config.EnergyWeight * energyScore +
                            config.ZCRWeight * zcrScore +
                            config.SpectralWeight * centroidScore +
                            config.HistoryWeight * historyScore;
            
            // Apply smoothing
            var smoothed = session.SmoothProbability(probability);
            
            return Math.Max(0, Math.Min(1, smoothed));
        }

        private float ScoreCentroid(float centroid)
        {
            // Speech typically has centroid between 300-3000 Hz
            if (centroid >= 300 && centroid <= 3000)
            {
                return 1.0f;
            }
            else if (centroid < 300)
            {
                return centroid / 300;
            }
            else
            {
                return Math.Max(0, 1.0f - (centroid - 3000) / 3000);
            }
        }

        private VADState UpdateVADState(VADSession session, float probability)
        {
            var config = session.Config;
            var currentState = session.CurrentState;
            var now = DateTime.UtcNow;
            
            switch (currentState)
            {
                case VADState.Idle:
                case VADState.Silence:
                    if (probability > config.SpeechThreshold)
                    {
                        session.CurrentState = VADState.MaybeSpeech;
                        session.StateStartTime = now;
                    }
                    break;
                    
                case VADState.MaybeSpeech:
                    if (probability > config.SpeechThreshold)
                    {
                        var duration = (now - session.StateStartTime).TotalMilliseconds;
                        if (duration >= config.MinSpeechDuration)
                        {
                            session.CurrentState = VADState.Speech;
                            session.SpeechStartTime = session.StateStartTime;
                        }
                    }
                    else if (probability < config.SilenceThreshold)
                    {
                        session.CurrentState = VADState.Idle;
                    }
                    break;
                    
                case VADState.Speech:
                    if (probability < config.SilenceThreshold)
                    {
                        session.CurrentState = VADState.MaybeSilence;
                        session.StateStartTime = now;
                    }
                    break;
                    
                case VADState.MaybeSilence:
                    if (probability > config.SpeechThreshold)
                    {
                        session.CurrentState = VADState.Speech;
                    }
                    else if (probability < config.SilenceThreshold)
                    {
                        var duration = (now - session.StateStartTime).TotalMilliseconds;
                        if (duration >= config.MaxSilenceDuration)
                        {
                            session.CurrentState = VADState.Silence;
                            session.SpeechEndTime = now;
                        }
                    }
                    break;
            }
            
            return session.CurrentState;
        }

        public void UpdateConfiguration(string sessionId, VADConfiguration config)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Config = config;
                _logger.LogInformation("Updated VAD configuration for session {SessionId}", sessionId);
            }
        }

        public VADMetrics GetMetrics(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                return session.GetMetrics();
            }
            
            return new VADMetrics();
        }

        public void ResetSession(string sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
            _logger.LogInformation("Reset VAD session {SessionId}", sessionId);
        }
    }

    public class VADSession
    {
        public string SessionId { get; }
        public VADConfiguration Config { get; set; }
        public VADState CurrentState { get; set; }
        public DateTime StateStartTime { get; set; }
        public DateTime? SpeechStartTime { get; set; }
        public DateTime? SpeechEndTime { get; set; }
        
        private readonly Queue<AudioFeatures> _featureHistory;
        private readonly Queue<float> _probabilityHistory;
        private readonly VADMetrics _metrics;
        private const int MaxHistorySize = 50;
        
        public VADSession(string sessionId)
        {
            SessionId = sessionId;
            Config = new VADConfiguration();
            CurrentState = VADState.Idle;
            StateStartTime = DateTime.UtcNow;
            _featureHistory = new Queue<AudioFeatures>();
            _probabilityHistory = new Queue<float>();
            _metrics = new VADMetrics();
        }
        
        public void AddFrame(AudioFeatures features)
        {
            _featureHistory.Enqueue(features);
            while (_featureHistory.Count > MaxHistorySize)
            {
                _featureHistory.Dequeue();
            }
        }
        
        public float GetAverageEnergy()
        {
            if (_featureHistory.Count == 0) return 0;
            return _featureHistory.Average(f => f.Energy);
        }
        
        public float SmoothProbability(float probability)
        {
            _probabilityHistory.Enqueue(probability);
            while (_probabilityHistory.Count > 5)
            {
                _probabilityHistory.Dequeue();
            }
            
            return _probabilityHistory.Average();
        }
        
        public void UpdateMetrics(VADState state, AudioFeatures features)
        {
            _metrics.TotalFrames++;
            _metrics.LastUpdateTime = DateTime.UtcNow;
            
            if (state == VADState.Speech)
            {
                _metrics.SpeechFrames++;
            }
            
            // Track state transitions
            if (CurrentState != state)
            {
                if (state == VADState.Speech && SpeechStartTime.HasValue)
                {
                    _metrics.SpeechSegments++;
                }
                else if (CurrentState == VADState.Speech && state != VADState.Speech && 
                         SpeechStartTime.HasValue && SpeechEndTime.HasValue)
                {
                    var duration = (SpeechEndTime.Value - SpeechStartTime.Value).TotalMilliseconds;
                    _metrics.TotalSpeechDuration += duration;
                    _metrics.AverageSegmentDuration = _metrics.TotalSpeechDuration / _metrics.SpeechSegments;
                }
            }
        }
        
        public VADMetrics GetMetrics()
        {
            return new VADMetrics
            {
                SessionId = SessionId,
                TotalFrames = _metrics.TotalFrames,
                SpeechFrames = _metrics.SpeechFrames,
                SpeechSegments = _metrics.SpeechSegments,
                TotalSpeechDuration = _metrics.TotalSpeechDuration,
                AverageSegmentDuration = _metrics.AverageSegmentDuration,
                LastUpdateTime = _metrics.LastUpdateTime,
                CurrentState = CurrentState
            };
        }
    }

    public class VADConfiguration
    {
        public float NoiseGateThreshold { get; set; } = 0.01f;
        public float EnergyThreshold { get; set; } = 0.02f;
        public float SpeechThreshold { get; set; } = 0.7f;
        public float SilenceThreshold { get; set; } = 0.3f;
        public int MinSpeechDuration { get; set; } = 300;
        public int MaxSilenceDuration { get; set; } = 1500;
        
        // Feature weights
        public float EnergyWeight { get; set; } = 0.3f;
        public float ZCRWeight { get; set; } = 0.2f;
        public float SpectralWeight { get; set; } = 0.3f;
        public float HistoryWeight { get; set; } = 0.2f;
    }

    public class VADResult
    {
        public bool IsSpeech { get; set; }
        public float Confidence { get; set; }
        public float Energy { get; set; }
        public VADState State { get; set; }
        public AudioFeatures Features { get; set; }
    }

    public class AudioFeatures
    {
        public float Energy { get; set; }
        public float ZeroCrossingRate { get; set; }
        public float SpectralCentroid { get; set; }
        public float SpectralRolloff { get; set; }
        public int SampleCount { get; set; }
    }

    public class VADMetrics
    {
        public string SessionId { get; set; }
        public long TotalFrames { get; set; }
        public long SpeechFrames { get; set; }
        public int SpeechSegments { get; set; }
        public double TotalSpeechDuration { get; set; }
        public double AverageSegmentDuration { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public VADState CurrentState { get; set; }
    }

    public enum VADState
    {
        Idle,
        MaybeSpeech,
        Speech,
        MaybeSilence,
        Silence
    }
}