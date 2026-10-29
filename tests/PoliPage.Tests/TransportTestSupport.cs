using System.Text;
using PoliPage.Tests.Contract;

namespace PoliPage.Tests;

/// <summary>Answers and clients for the tests that drive the SDK against <see cref="MockServer"/>.</summary>
internal static class TransportTestSupport
{
    public const string ApiKey = "pp_sandbox_00000000000000000000000000000000";

    public static readonly RenderInput Input = new()
    {
        Project = "billing",
        Template = "invoice",
        Data = new Dictionary<string, object> { ["invoiceNumber"] = "INV-001" },
    };

    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% a small test PDF body\n%%EOF\n");

    /// <summary>A 200 with a DocumentDescriptor whose presigned URL points at the mock.</summary>
    public static MockStep Descriptor(string origin) => JsonAnswer(
        200,
        $$"""
        {"documentId":"doc_abc123","organizationId":"org_xyz","projectId":"p1","projectSlug":"billing",
        "templateId":"t1","templateSlug":"invoice","environment":"sandbox","apiKeyId":"k1","format":"A4",
        "orientation":"portrait","locale":"fr-FR","pageCount":2,"sizeBytes":38421,
        "createdAt":"2026-09-29T09:45:22.000Z","metadata":{},
        "presignedPdfUrl":"{{origin}}/storage/doc_abc123.pdf?X-Amz-Signature=5f2b9c",
        "expiresAt":"2026-09-29T10:00:22.000Z"}
        """);

    public static MockStep JsonAnswer(int status, string json, params KeyValuePair<string, string>[] headers)
        => new(status, [new("Content-Type", "application/json"), .. headers], Encoding.UTF8.GetBytes(json), null);

    public static MockStep Bytes(int status, byte[] body, string contentType)
        => new(status, [new("Content-Type", contentType)], body, null);

    public static MockStep NoContent() => new(204, [], [], null);

    public static PoliPageClientOptions Options(MockServer server, int maxRetries = 0, TimeSpan? timeout = null)
        => new()
        {
            ApiKey = ApiKey,
            BaseUrl = new Uri(server.Origin),
            MaxRetries = maxRetries,
            RetryDelay = TimeSpan.FromMilliseconds(10),
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(5),
        };
}
