using System.Collections.Concurrent;
using System.Diagnostics;

namespace VoiceCode.DispatcherService.Services;

public interface IMetricsService
{
    void RecordConnection();
    void RecordDisconnection();
    void RecordMessage(string type, int size);
    void RecordActiveSessions(int count);
    void RecordProcessingTime(string operation, TimeSpan duration);
    void RecordError(string operation);
    Task<Dictionary<string, object>> GetMetricsAsync();
}

public class MetricsService : IMetricsService
{
    private readonly ILogger<MetricsService> _logger;
    private readonly ConcurrentDictionary<string, long> _counters;
    private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _timings;
    private int _activeConnections;
    private int _activeSessions;

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
        _counters = new ConcurrentDictionary<string, long>();
        _timings = new ConcurrentDictionary<string, ConcurrentBag<double>>();
        _activeConnections = 0;
        _activeSessions = 0;
    }

    public void RecordConnection()
    {
        Interlocked.Increment(ref _activeConnections);
        IncrementCounter("connections.total");
    }

    public void RecordDisconnection()
    {
        Interlocked.Decrement(ref _activeConnections);
        IncrementCounter("disconnections.total");
    }

    public void RecordMessage(string type, int size)
    {
        IncrementCounter($"messages.{type}.count");
        AddToCounter($"messages.{type}.bytes", size);
    }

    public void RecordActiveSessions(int count)
    {
        _activeSessions = count;
    }

    public void RecordProcessingTime(string operation, TimeSpan duration)
    {
        var timings = _timings.GetOrAdd(operation, _ => new ConcurrentBag<double>());
        timings.Add(duration.TotalMilliseconds);
    }

    public void RecordError(string operation)
    {
        IncrementCounter($"errors.{operation}");
    }

    public Task<Dictionary<string, object>> GetMetricsAsync()
    {
        var metrics = new Dictionary<string, object>
        {
            ["connections.active"] = _activeConnections,
            ["sessions.active"] = _activeSessions
        };

        // Add counters
        foreach (var counter in _counters)
        {
            metrics[counter.Key] = counter.Value;
        }

        // Add timing statistics
        foreach (var timing in _timings)
        {
            var values = timing.Value.ToList();
            if (values.Any())
            {
                metrics[$"{timing.Key}.count"] = values.Count;
                metrics[$"{timing.Key}.avg"] = values.Average();
                metrics[$"{timing.Key}.min"] = values.Min();
                metrics[$"{timing.Key}.max"] = values.Max();
                metrics[$"{timing.Key}.p95"] = GetPercentile(values, 0.95);
            }
        }

        return Task.FromResult(metrics);
    }

    private void IncrementCounter(string name)
    {
        _counters.AddOrUpdate(name, 1, (_, value) => value + 1);
    }

    private void AddToCounter(string name, long amount)
    {
        _counters.AddOrUpdate(name, amount, (_, value) => value + amount);
    }

    private double GetPercentile(List<double> values, double percentile)
    {
        values.Sort();
        var index = (int)Math.Ceiling(percentile * values.Count) - 1;
        return values[Math.Max(0, Math.Min(index, values.Count - 1))];
    }
}