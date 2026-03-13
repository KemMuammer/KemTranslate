using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KemTranslate
{
    internal sealed class TranslationService
    {
        private static readonly HttpClient Http = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly Func<AppSettings> _settingsProvider;

        public TranslationService(Func<AppSettings> settingsProvider)
        {
            _settingsProvider = settingsProvider;
        }

        public async Task<List<LtLanguage>> GetLanguagesAsync(CancellationToken cancellationToken = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BuildApiUri("languages"));
            using var res = await SendWithRetryAsync(() => Http.SendAsync(req, cancellationToken), cancellationToken);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<List<LtLanguage>>(json, JsonOptions) ?? [];
        }

        public async Task<string?> DetectSourceLanguageAsync(string text, CancellationToken cancellationToken = default)
        {
            var payload = new { q = text, api_key = GetApiKey() };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var res = await SendWithRetryAsync(() => Http.PostAsync(BuildApiUri("detect"), content, cancellationToken), cancellationToken);
            res.EnsureSuccessStatusCode();

            var body = await res.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                var flat = JsonSerializer.Deserialize<List<DetectCandidate>>(body, JsonOptions);
                if (flat?.Count > 0)
                    return flat.OrderByDescending(c => c.confidence).First().language;
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "DetectSourceLanguageAsync flat parse failed");
            }

            try
            {
                var nested = JsonSerializer.Deserialize<List<List<DetectCandidate>>>(body, JsonOptions);
                if (nested != null && nested.Count > 0 && nested[0].Count > 0)
                    return nested[0].OrderByDescending(c => c.confidence).First().language;
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "DetectSourceLanguageAsync nested parse failed");
            }

            return null;
        }

        public async Task<string> TranslateTextAsync(string text, string source, string target, CancellationToken cancellationToken = default)
        {
            var request = new TranslateRequest
            {
                q = text,
                source = source,
                target = target,
                format = "text",
                api_key = GetApiKey()
            };

            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var res = await SendWithRetryAsync(() => Http.PostAsync(BuildApiUri("translate"), content, cancellationToken), cancellationToken);
            var body = await res.Content.ReadAsStringAsync(cancellationToken);

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Translation failed: {(int)res.StatusCode} {res.ReasonPhrase}");

            var parsed = JsonSerializer.Deserialize<TranslateResponse>(body, JsonOptions);
            return parsed?.translatedText ?? string.Empty;
        }

        public Uri BuildApiUri(string relativePath)
        {
            return new Uri(new Uri(GetApiBaseUrl(), UriKind.Absolute), relativePath);
        }

        private string GetApiBaseUrl()
        {
            var baseUrl = (_settingsProvider().ServerUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = "https://tl.kemmuammer.com/";

            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";

            return baseUrl;
        }

        private string GetApiKey() => _settingsProvider().ApiKey ?? string.Empty;

        private static async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> operation, CancellationToken cancellationToken)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppLogger.Log(ex, "Translation service request failed, retrying");
                await Task.Delay(250, cancellationToken);
                return await operation();
            }
        }
    }
}
