namespace PoliPage;

/// <summary>
/// Groups all render operations: PDF, stream, preview, and document output.
/// Accessed via <see cref="PoliPageClient.Render"/>.
/// </summary>
public sealed class Render
{
    private readonly Internal.ITransport _transport;

    internal Render(Internal.ITransport transport) => _transport = transport;

    /// <summary>
    /// Renders a stored project template to a PDF and returns the raw bytes.
    /// </summary>
    /// <param name="input">The project template reference and optional data.</param>
    /// <param name="options">Optional per-call overrides (idempotency key, timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The raw PDF bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageAuthException">The API returned 401 or 403.</exception>
    /// <exception cref="PoliPageNotFoundException">The API returned 404 (project, template, or version not found).</exception>
    /// <exception cref="PoliPageValidationException">The API returned 400 or 422 (invalid input).</exception>
    /// <exception cref="PoliPageRateLimitException">The API returned 429; <see cref="PoliPageRateLimitException.RetryAfter"/> exposes the server's retry hint.</exception>
    /// <exception cref="PoliPagePaymentRequiredException">The API returned 402 (organisation has unpaid invoices).</exception>
    /// <exception cref="PoliPageGoneException">The API returned 410 (resource permanently removed).</exception>
    /// <exception cref="PoliPageNetworkException">DNS, TCP, or TLS failure before the response was received.</exception>
    /// <exception cref="PoliPageException">Any other API failure, or a per-request timeout (<see cref="PoliPageErrorCode.Timeout"/>).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<byte[]> PdfAsync(
        ProjectModeInput input,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var idempotencyKey = options?.IdempotencyKey ?? Guid.NewGuid().ToString();

        using var response = await _transport.PostAsync(
            "/render",
            input,
            idempotencyKey,
            options,
            cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
