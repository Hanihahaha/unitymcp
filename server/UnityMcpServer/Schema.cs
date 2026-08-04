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

    public static object Array(object items, string description)
    {
        return new { type = "array", items, description };
    }

    public static object Any()
    {
        return new { };
    }

    public static object Any(string description)
    {
        return new { description };
    }
}
