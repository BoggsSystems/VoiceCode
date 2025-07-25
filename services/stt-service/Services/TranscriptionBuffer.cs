using System.Collections.Concurrent;
using System.Text;
using VoiceCode.Common.Models;

namespace VoiceCode.STTService.Services;

public class TranscriptionBuffer
{
    private readonly string _sessionId;
    private readonly ConcurrentQueue<TranscriptionSegment> _segments;
    private readonly ConcurrentDictionary<string, PartialTranscriptionResult> _partialResults;
    private readonly object _lockObject = new object();
    
    private TranscriptionSegment _currentSegment;
    private StringBuilder _aggregatedText;
    private List<TranscriptionWord> _aggregatedWords;
    private double _totalConfidence;
    private int _segmentCount;

    public TranscriptionBuffer(string sessionId)
    {
        _sessionId = sessionId;
        _segments = new ConcurrentQueue<TranscriptionSegment>();
        _partialResults = new ConcurrentDictionary<string, PartialTranscriptionResult>();
        _aggregatedText = new StringBuilder();
        _aggregatedWords = new List<TranscriptionWord>();
    }

    public void AddPartial(PartialTranscriptionResult partial)
    {
        lock (_lockObject)
        {
            // Update current segment with partial result
            if (_currentSegment == null)
            {
                _currentSegment = new TranscriptionSegment
                {
                    Id = Guid.NewGuid().ToString(),
                    Text = partial.Text,
                    IsFinal = false,
                    Timestamp = partial.Timestamp,
                    Confidence = 0.7 // Default confidence for partials
                };
            }
            else
            {
                _currentSegment.Text = partial.Text;
                _currentSegment.Timestamp = partial.Timestamp;
            }

            // Store partial for reference
            _partialResults.TryAdd(_currentSegment.Id, partial);
        }
    }

    public void AddFinal(TranscriptionResult final)
    {
        lock (_lockObject)
        {
            // Convert current segment to final
            if (_currentSegment != null)
            {
                _currentSegment.Text = final.Text;
                _currentSegment.IsFinal = true;
                _currentSegment.Confidence = final.Confidence;
                _currentSegment.Words = final.Words;
                
                // Add to segments queue
                _segments.Enqueue(_currentSegment);
                
                // Update aggregated data
                if (!string.IsNullOrWhiteSpace(final.Text))
                {
                    if (_aggregatedText.Length > 0)
                    {
                        _aggregatedText.Append(" ");
                    }
                    _aggregatedText.Append(final.Text);
                }
                
                if (final.Words != null)
                {
                    _aggregatedWords.AddRange(final.Words);
                }
                
                _totalConfidence += final.Confidence;
                _segmentCount++;
                
                // Clear partials for this segment
                _partialResults.TryRemove(_currentSegment.Id, out _);
                
                // Reset current segment
                _currentSegment = null;
            }
            else
            {
                // Create new segment for this final result
                var segment = new TranscriptionSegment
                {
                    Id = final.Id ?? Guid.NewGuid().ToString(),
                    Text = final.Text,
                    IsFinal = true,
                    Confidence = final.Confidence,
                    Timestamp = DateTime.UtcNow,
                    Words = final.Words
                };
                
                _segments.Enqueue(segment);
                
                // Update aggregated data
                if (!string.IsNullOrWhiteSpace(final.Text))
                {
                    if (_aggregatedText.Length > 0)
                    {
                        _aggregatedText.Append(" ");
                    }
                    _aggregatedText.Append(final.Text);
                }
                
                if (final.Words != null)
                {
                    _aggregatedWords.AddRange(final.Words);
                }
                
                _totalConfidence += final.Confidence;
                _segmentCount++;
            }
        }
    }

    public TranscriptionBufferState GetCurrentState()
    {
        lock (_lockObject)
        {
            var state = new TranscriptionBufferState
            {
                SessionId = _sessionId,
                CurrentPartial = _currentSegment?.IsFinal == false ? _currentSegment.Text : null,
                LastFinal = GetLastFinalText(),
                AccumulatedText = _aggregatedText.ToString(),
                AccumulatedWords = _aggregatedWords.ToList(),
                SegmentCount = _segmentCount,
                AverageConfidence = _segmentCount > 0 ? _totalConfidence / _segmentCount : 0,
                HasPendingPartial = _currentSegment?.IsFinal == false
            };
            
            return state;
        }
    }

    public TranscriptionResult GetAggregatedResult()
    {
        lock (_lockObject)
        {
            // Add any pending partial as final
            if (_currentSegment != null && !_currentSegment.IsFinal)
            {
                _currentSegment.IsFinal = true;
                _currentSegment.Confidence = 0.7; // Lower confidence for forced final
                _segments.Enqueue(_currentSegment);
                
                if (!string.IsNullOrWhiteSpace(_currentSegment.Text))
                {
                    if (_aggregatedText.Length > 0)
                    {
                        _aggregatedText.Append(" ");
                    }
                    _aggregatedText.Append(_currentSegment.Text);
                }
                
                _currentSegment = null;
            }
            
            return new TranscriptionResult
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = _sessionId,
                Text = _aggregatedText.ToString(),
                Transcript = _aggregatedText.ToString(),
                IsFinal = true,
                Confidence = _segmentCount > 0 ? _totalConfidence / _segmentCount : 0,
                Words = _aggregatedWords,
                Success = true
            };
        }
    }

    public List<TranscriptionSegment> GetAllSegments()
    {
        lock (_lockObject)
        {
            var segments = _segments.ToList();
            
            // Add current segment if exists
            if (_currentSegment != null)
            {
                segments.Add(_currentSegment);
            }
            
            return segments;
        }
    }

    public string GetLastFinalText()
    {
        lock (_lockObject)
        {
            var segments = _segments.ToArray();
            return segments.LastOrDefault(s => s.IsFinal)?.Text;
        }
    }

    public void Clear()
    {
        lock (_lockObject)
        {
            _segments.Clear();
            _partialResults.Clear();
            _currentSegment = null;
            _aggregatedText.Clear();
            _aggregatedWords.Clear();
            _totalConfidence = 0;
            _segmentCount = 0;
        }
    }
}

public class TranscriptionBufferState
{
    public string SessionId { get; set; }
    public string CurrentPartial { get; set; }
    public string LastFinal { get; set; }
    public string AccumulatedText { get; set; }
    public List<TranscriptionWord> AccumulatedWords { get; set; }
    public int SegmentCount { get; set; }
    public double AverageConfidence { get; set; }
    public bool HasPendingPartial { get; set; }
    
    public string GetFullTranscript()
    {
        if (!string.IsNullOrEmpty(CurrentPartial))
        {
            return string.IsNullOrEmpty(AccumulatedText) 
                ? CurrentPartial 
                : $"{AccumulatedText} {CurrentPartial}";
        }
        
        return AccumulatedText ?? string.Empty;
    }
}