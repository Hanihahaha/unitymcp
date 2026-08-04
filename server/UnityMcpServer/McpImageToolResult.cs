using System.Text.Json.Nodes;

internal sealed record McpImageToolResult(
    bool IsError,
    string? MimeType,
    string? Data,
    JsonObject Metadata);
