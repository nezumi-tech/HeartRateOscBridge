using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace HeartRateAntPlus;

public sealed record AvailableUpdate(string Version, Version ParsedVersion);

public static class UpdateChecker
{
    private static readonly HttpClient Client = CreateClient();
    private const string LatestReleaseApi = "https://api.github.com/repos/nezumi-tech/HeartRateOscBridge/releases/latest";

    public static async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("tag_name", out var tagElement)) return null;

        var tag = tagElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var latest = ParseVersion(tag);
        var current = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));
        return latest > current ? new AvailableUpdate(tag, latest) : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HeartRateOscBridge-UpdateChecker/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version ParseVersion(string tag)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var suffixIndex = value.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0) value = value[..suffixIndex];
        if (!Version.TryParse(value, out var parsed)) throw new FormatException($"GitHub Releaseのタグ '{tag}' は数値バージョンとして解釈できません。");
        return Normalize(parsed);
    }

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));
}
