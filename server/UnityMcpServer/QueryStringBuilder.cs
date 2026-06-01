internal sealed class QueryStringBuilder
{
    private readonly List<string> parts = [];

    public void Add(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }
    }

    public void Add(string key, int? value)
    {
        if (value != null)
        {
            Add(key, value.Value.ToString());
        }
    }

    public void Add(string key, bool? value)
    {
        if (value != null)
        {
            Add(key, value.Value ? "true" : "false");
        }
    }

    public override string ToString()
    {
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
