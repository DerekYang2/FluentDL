using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;

namespace FluentDL.Helpers
{
    // Reader for public, free API keys bundled with the app
    internal class KeyReader
    {
        private static readonly string PATH = Path.Combine(AppContext.BaseDirectory, "Assets\\keys.txt");
        private static JsonElement rootElement;
        private static bool isLoaded = false;

        public static async Task Initialize() {
            isLoaded = false;
            try {
                var text = AesHelper.Decrypt(await File.ReadAllTextAsync(PATH));
                rootElement = JsonDocument.Parse(text).RootElement;
                isLoaded = true;
            } catch (Exception e) {
                Debug.WriteLine("Error loading keys file: "  + e.Message);
            }
        }

        public static string? GetValue(string key) {
            if (!isLoaded) return null;
            return rootElement.TryGetProperty(key, out var value) ? value.GetString() : null;
        }

        public static ImmutableArray<string?> GetValues(string key) {
            if (!isLoaded) return ImmutableArray<string?>.Empty;
            return rootElement.GetProperty(key).EnumerateArray().Select(x=>x.GetString()).ToImmutableArray();
        }
    }
}
