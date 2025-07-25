using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VoiceCode.OrchestratorService.Services
{
    public interface IRepoAwareRoutingService
    {
        Task<string> SelectWorkerByKeywordsAsync(string voicePrompt);
        Task<RepositoryConfig> GetRepoConfigForWorkerAsync(string workerId);
        Task<List<RepositoryConfig>> GetAllRepoConfigsAsync();
        void ReloadConfiguration();
    }

    public class RepoAwareRoutingService : IRepoAwareRoutingService
    {
        private readonly ILogger<RepoAwareRoutingService> _logger;
        private readonly IConfiguration _configuration;
        private RepositoryRegistry _registry;
        private readonly object _registryLock = new object();

        public RepoAwareRoutingService(
            ILogger<RepoAwareRoutingService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            LoadRegistryConfiguration();
        }

        private void LoadRegistryConfiguration()
        {
            try
            {
                // Try to load from configuration first
                var registrySection = _configuration.GetSection("RepositoryRegistry");
                if (registrySection.Exists())
                {
                    _registry = new RepositoryRegistry();
                    registrySection.Bind(_registry);
                    _logger.LogInformation("Loaded repository registry from configuration");
                    return;
                }

                // Fallback to YAML file
                var yamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "repo-registry.yaml");
                if (File.Exists(yamlPath))
                {
                    var yaml = File.ReadAllText(yamlPath);
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    _registry = deserializer.Deserialize<RepositoryRegistry>(yaml);
                    _logger.LogInformation("Loaded repository registry from YAML file");
                    return;
                }

                // Use default configuration
                _registry = GetDefaultRegistry();
                _logger.LogWarning("Using default repository registry configuration");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load repository registry, using defaults");
                _registry = GetDefaultRegistry();
            }
        }

        private RepositoryRegistry GetDefaultRegistry()
        {
            return new RepositoryRegistry
            {
                Repositories = new List<RepositoryConfig>
                {
                    new RepositoryConfig
                    {
                        WorkerId = "worker-1",
                        Name = "Default Repository",
                        Repo = "https://github.com/default/repo",
                        Keywords = new List<string> { "default" },
                        Description = "Default repository for unmatched requests"
                    }
                },
                Fallback = new FallbackConfig
                {
                    Strategy = "round-robin",
                    GenericWorkers = new List<string>()
                }
            };
        }

        public async Task<string> SelectWorkerByKeywordsAsync(string voicePrompt)
        {
            if (string.IsNullOrWhiteSpace(voicePrompt))
            {
                _logger.LogWarning("Empty voice prompt provided");
                return null;
            }

            var lowerPrompt = voicePrompt.ToLower();
            var scores = new Dictionary<string, int>();

            lock (_registryLock)
            {
                foreach (var repo in _registry.Repositories)
                {
                    var score = CalculateMatchScore(lowerPrompt, repo);
                    if (score > 0)
                    {
                        scores[repo.WorkerId] = score;
                    }
                }
            }

            if (scores.Any())
            {
                // Return the worker with the highest score
                var bestMatch = scores.OrderByDescending(s => s.Value).First();
                _logger.LogInformation(
                    "Selected worker {WorkerId} for prompt with score {Score}", 
                    bestMatch.Key, 
                    bestMatch.Value);
                return bestMatch.Key;
            }

            // No matches found - use fallback strategy
            _logger.LogWarning("No keyword matches found for prompt: {Prompt}", voicePrompt);
            return await ApplyFallbackStrategyAsync();
        }

        private int CalculateMatchScore(string lowerPrompt, RepositoryConfig repo)
        {
            int score = 0;

            // Check exact repo name match (highest priority)
            if (!string.IsNullOrEmpty(repo.Name) && lowerPrompt.Contains(repo.Name.ToLower()))
            {
                score += 100;
            }

            // Check keyword matches
            foreach (var keyword in repo.Keywords)
            {
                if (lowerPrompt.Contains(keyword.ToLower()))
                {
                    // Longer keywords get higher scores (more specific)
                    score += keyword.Length * 2;
                    
                    // Bonus for word boundary matches
                    if (IsWordBoundaryMatch(lowerPrompt, keyword.ToLower()))
                    {
                        score += 10;
                    }
                }
            }

            return score;
        }

        private bool IsWordBoundaryMatch(string text, string keyword)
        {
            // Check if keyword appears as a complete word
            var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}\b";
            return System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
        }

        private async Task<string> ApplyFallbackStrategyAsync()
        {
            lock (_registryLock)
            {
                if (_registry.Fallback?.GenericWorkers?.Any() == true)
                {
                    switch (_registry.Fallback.Strategy)
                    {
                        case "round-robin":
                            // Simple round-robin for now
                            var index = DateTime.UtcNow.Second % _registry.Fallback.GenericWorkers.Count;
                            return _registry.Fallback.GenericWorkers[index];

                        case "random":
                            var random = new Random();
                            var randomIndex = random.Next(_registry.Fallback.GenericWorkers.Count);
                            return _registry.Fallback.GenericWorkers[randomIndex];

                        case "least-loaded":
                            // This would require integration with worker pool service
                            // For now, fallback to round-robin
                            goto case "round-robin";
                    }
                }
            }

            return null;
        }

        public async Task<RepositoryConfig> GetRepoConfigForWorkerAsync(string workerId)
        {
            lock (_registryLock)
            {
                return _registry.Repositories.FirstOrDefault(r => r.WorkerId == workerId);
            }
        }

        public async Task<List<RepositoryConfig>> GetAllRepoConfigsAsync()
        {
            lock (_registryLock)
            {
                return _registry.Repositories.ToList();
            }
        }

        public void ReloadConfiguration()
        {
            _logger.LogInformation("Reloading repository registry configuration");
            LoadRegistryConfiguration();
        }
    }

    // Models for configuration
    public class RepositoryRegistry
    {
        public List<RepositoryConfig> Repositories { get; set; } = new();
        public FallbackConfig Fallback { get; set; }
    }

    public class RepositoryConfig
    {
        public string WorkerId { get; set; }
        public string Name { get; set; }
        public string Repo { get; set; }
        public List<string> Keywords { get; set; } = new();
        public string Description { get; set; }
    }

    public class FallbackConfig
    {
        public string Strategy { get; set; }
        public List<string> GenericWorkers { get; set; } = new();
    }
}