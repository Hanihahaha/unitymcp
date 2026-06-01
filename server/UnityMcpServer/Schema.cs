internal static class Schema
{
    public static object Object(Dictionary<string, object>? properties = null, string[]? required = null)
    {
        return new
        {
            type = "object",
            properties = properties ?? new Dictionary<string, object>(),
            required = required ?? []
        };
    }

    public static object String(string description)
    {
        return new { type = "string", description };
    }

    public static object Boolean(string description)
    {
        return new { type = "boolean", description };
    }

    public static object Integer(string description)
    {
        return new { type = "integer", description };
    }
}
