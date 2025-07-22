using Microsoft.ML.Data;

namespace VoiceCode.RouterService.Models;

public class IntentInput
{
    [LoadColumn(0)]
    public string Text { get; set; } = string.Empty;

    [LoadColumn(1), ColumnName("Label")]
    public string Label { get; set; } = string.Empty;
}

public class IntentPrediction
{
    [ColumnName("PredictedLabel")]
    public string? PredictedLabel { get; set; }

    [ColumnName("Score")]
    public float[]? Score { get; set; }
}