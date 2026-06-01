namespace PoliPage;

/// <summary>
/// Base type for render inputs. Sealed against external extension via an
/// internal constructor — only <see cref="ProjectModeInput"/> and
/// <see cref="InlineModeInput"/> can derive.
/// </summary>
public abstract record RenderInput
{
    /// <summary>Free-form data passed to the template at render time.</summary>
    public object? Data { get; init; }

    /// <summary>Optional primitive-only metadata (string/number/bool/null values).</summary>
    public RenderMetadata? Metadata { get; init; }

    // Why: internal ctor intentionally blocks external derivation (Meziantou MA0017 suppressed
    // because this is a deliberate API constraint, not an oversight).
#pragma warning disable MA0017
    internal RenderInput() { }
#pragma warning restore MA0017
}
