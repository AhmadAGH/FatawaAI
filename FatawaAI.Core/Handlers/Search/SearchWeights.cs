namespace FatawaAI.Core;

/// <summary>
/// Weights for combining per-field embedding scores.
/// Only title and question are embedded; answer is not.
/// </summary>
public sealed record SearchWeights(
    double TitleWeight,
    double QuestionWeight)
{
    public static SearchWeights Default => new(0.6, 0.4);
}

