using System.Text.Json.Nodes;

internal static class JsonArgs
{
    public static string? TryGetString(JsonObject args, string key)
    {
        return args.TryGetPropertyValue(key, out var node) && node != null
            ? node.GetValue<string>()
            : null;
    }

    public static int? TryGetInt(JsonObject args, string key)
    {
        return args.TryGetPropertyValue(key, out var node) && node != null
            ? node.GetValue<int>()
            : null;
    }

    public static bool? TryGetBool(JsonObject args, string key)
    {
        return args.TryGetPropertyValue(key, out var node) && node != null
            ? node.GetValue<bool>()
            : null;
    }
}
