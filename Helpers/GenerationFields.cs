namespace Palace.Helpers;

/// <summary>
/// Preview Generation / Prompt / Negative / Seed. Empty catalog or Extract
/// replaces prior Prompt/Model — the inverse of
/// <c>HashService.ApplyExtractedMetadata</c>, which keeps catalog fields when
/// the file is still online-only.
/// </summary>
public static class GenerationFields
{
    public readonly record struct Snapshot(string? Model, string? Prompt, string? Negative, string? Seed)
    {
        public bool HasAny => GenerationFields.HasAny(Model, Prompt, Negative, Seed);
    }

    public static bool HasAny(string? model, string? prompt, string? negative, string? seed) =>
        !string.IsNullOrWhiteSpace(model)
        || !string.IsNullOrWhiteSpace(prompt)
        || !string.IsNullOrWhiteSpace(negative)
        || !string.IsNullOrWhiteSpace(seed);

    /// <summary>
    /// Bind snapshot for the current asset. Blanks become null so a non-AI
    /// image never keeps the previous AI Prompt/Model.
    /// </summary>
    public static Snapshot ForPreview(string? model, string? prompt, string? negative, string? seed) =>
        new(BlankToNull(model), BlankToNull(prompt), BlankToNull(negative), BlankToNull(seed));

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
