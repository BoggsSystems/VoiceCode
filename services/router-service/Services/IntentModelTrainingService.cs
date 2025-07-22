using Microsoft.Extensions.Options;
using Microsoft.ML;
using VoiceCode.RouterService.Configuration;
using VoiceCode.RouterService.Models;

namespace VoiceCode.RouterService.Services;

public class IntentModelTrainingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IntentClassificationOptions _options;
    private readonly ILogger<IntentModelTrainingService> _logger;
    private readonly TimeSpan _trainingInterval = TimeSpan.FromDays(1);

    public IntentModelTrainingService(
        IServiceProvider serviceProvider,
        IOptions<IntentClassificationOptions> options,
        ILogger<IntentModelTrainingService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.UseMLModel)
        {
            _logger.LogInformation("ML model training is disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting intent model training");
                await TrainModelAsync(stoppingToken);
                _logger.LogInformation("Intent model training completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during model training");
            }

            await Task.Delay(_trainingInterval, stoppingToken);
        }
    }

    private async Task TrainModelAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var mlContext = new MLContext(seed: 0);

            // Load training data
            var trainingData = GetTrainingData();
            var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

            // Split data for training and testing
            var splitData = mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);

            // Build training pipeline
            var pipeline = mlContext.Transforms.Text.FeaturizeText(
                    outputColumnName: "Features",
                    inputColumnName: nameof(IntentInput.Text))
                .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: "Label",
                    featureColumnName: "Features"))
                .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            // Train the model
            _logger.LogInformation("Training ML model with {Count} samples", trainingData.Count);
            var model = pipeline.Fit(splitData.TrainSet);

            // Evaluate the model
            var predictions = model.Transform(splitData.TestSet);
            var metrics = mlContext.MulticlassClassification.Evaluate(predictions, "Label", "Score");

            _logger.LogInformation("Model evaluation - Accuracy: {Accuracy:P2}, LogLoss: {LogLoss:F4}",
                metrics.MicroAccuracy, metrics.LogLoss);

            // Save the model if it performs well
            if (metrics.MicroAccuracy > 0.8)
            {
                var modelPath = _options.ModelPath;
                var directory = Path.GetDirectoryName(modelPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                mlContext.Model.Save(model, dataView.Schema, modelPath);
                _logger.LogInformation("Saved trained model to {Path}", modelPath);
            }
            else
            {
                _logger.LogWarning("Model accuracy {Accuracy:P2} is below threshold, not saving",
                    metrics.MicroAccuracy);
            }
        }, cancellationToken);
    }

    private List<IntentInput> GetTrainingData()
    {
        // In a real implementation, this would load from a database or file
        // For now, we'll use sample training data
        return new List<IntentInput>
        {
            // Code generation samples
            new IntentInput { Text = "create a new user service", Label = "generate_code" },
            new IntentInput { Text = "generate a REST API for products", Label = "generate_code" },
            new IntentInput { Text = "make a login function", Label = "generate_code" },
            new IntentInput { Text = "build a database connection class", Label = "generate_code" },
            new IntentInput { Text = "write a method to calculate taxes", Label = "generate_code" },
            new IntentInput { Text = "implement user authentication", Label = "generate_code" },

            // Code explanation samples
            new IntentInput { Text = "explain this function", Label = "explain_code" },
            new IntentInput { Text = "what does this code do", Label = "explain_code" },
            new IntentInput { Text = "how does this algorithm work", Label = "explain_code" },
            new IntentInput { Text = "tell me about this class", Label = "explain_code" },
            new IntentInput { Text = "help me understand this code", Label = "explain_code" },

            // Error fixing samples
            new IntentInput { Text = "fix this error", Label = "fix_error" },
            new IntentInput { Text = "there's a bug in my code", Label = "fix_error" },
            new IntentInput { Text = "debug this function", Label = "fix_error" },
            new IntentInput { Text = "solve this null reference exception", Label = "fix_error" },
            new IntentInput { Text = "the code is throwing an error", Label = "fix_error" },

            // Refactoring samples
            new IntentInput { Text = "refactor this method", Label = "refactor_code" },
            new IntentInput { Text = "improve this code", Label = "refactor_code" },
            new IntentInput { Text = "make this more efficient", Label = "refactor_code" },
            new IntentInput { Text = "clean up this function", Label = "refactor_code" },
            new IntentInput { Text = "optimize the performance", Label = "refactor_code" },

            // Testing samples
            new IntentInput { Text = "create unit tests", Label = "create_tests" },
            new IntentInput { Text = "write tests for this function", Label = "create_tests" },
            new IntentInput { Text = "generate test cases", Label = "create_tests" },
            new IntentInput { Text = "add integration tests", Label = "create_tests" },

            // Documentation samples
            new IntentInput { Text = "document this code", Label = "document_code" },
            new IntentInput { Text = "add comments to this function", Label = "document_code" },
            new IntentInput { Text = "write documentation", Label = "document_code" },
            new IntentInput { Text = "create API documentation", Label = "document_code" }
        };
    }
}