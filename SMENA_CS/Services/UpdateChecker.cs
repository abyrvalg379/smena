using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace SMENA.Services
{
    /// <summary>Release check against GitHub Releases (same scheme as FLOMASTER's updater).</summary>
    public static class UpdateChecker
    {
        public const string ReleasesUrl = "https://github.com/abyrvalg379/smena/releases/latest";
        private const string ApiLatest = "https://api.github.com/repos/abyrvalg379/smena/releases/latest";

        public static Version CurrentVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

        /// <summary>"v0.23" / "0.23.1" vs the running assembly version.</summary>
        public static bool IsNewer(string latestTag, Version current)
        {
            var s = (latestTag ?? "").Trim().TrimStart('v', 'V');
            return Version.TryParse(s, out var v) && v > current;
        }

        /// <summary>Latest release tag from the GitHub API; null on any failure (offline, rate limit).</summary>
        public static async Task<string?> FetchLatestTagAsync()
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SMENA-time-tracker");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync(ApiLatest);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("tag_name", out var el) ? el.GetString() : null;
        }
    }
}
