using System.Text.Json;

namespace FluentDL.Helpers
{
    public static class JsonElementExtensions
    {
        public static string? SafeGetString(this JsonElement element, params string[] path)
        {
            JsonElement current = element;
            foreach (var prop in path)
            {
                if (!current.TryGetProperty(prop, out current))
                    return null;
            }
            return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
        }

        public static bool SafeGetBool(this JsonElement element, params string[] path)
        {
            JsonElement current = element;
            foreach (var prop in path)
            {
                if (!current.TryGetProperty(prop, out current))
                    return false;
            }
            if (current.ValueKind == JsonValueKind.True) return true;
            return false;
        }

        public static int? SafeGetInt32(this JsonElement element, params string[] path)
        {
            JsonElement current = element;
            foreach (var prop in path)
            {
                if (!current.TryGetProperty(prop, out current))
                    return null;
            }
            return current.TryGetInt32(out var value) ? value : null;
        }
    }
}
