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
    internal sealed class WritingService
    {
        private static readonly HttpClient Http = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly Func<AppSettings> _settingsProvider;
        private readonly TranslationService _translationService;

        public WritingService(Func<AppSettings> settingsProvider, TranslationService translationService)
        {
            _settingsProvider = settingsProvider;
            _translationService = translationService;
        }

        public async Task<LanguageToolWriteResult> ImproveWritingAsync(string text, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["language"] = "auto"
            });

            using var res = await SendWithRetryAsync(() => Http.PostAsync(BuildLanguageToolUri("check"), content, cancellationToken), cancellationToken);
            var body = await res.Content.ReadAsStringAsync(cancellationToken);

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"LanguageTool failed: {(int)res.StatusCode} {res.ReasonPhrase}");

            var parsed = JsonSerializer.Deserialize<LanguageToolResponse>(body, JsonOptions);
            return BuildLanguageToolWriteResult(text, parsed?.matches ?? []);
        }

        public async Task<string> RewriteTextAsync(string text, IReadOnlyCollection<LtLanguage> languages, CancellationToken cancellationToken = default)
        {
            var trimmedText = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedText))
                return text ?? string.Empty;

            var detectedSource = await _translationService.DetectSourceLanguageAsync(trimmedText, cancellationToken);
            var sourceLanguage = string.IsNullOrWhiteSpace(detectedSource) || detectedSource.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : detectedSource;

            var pivotLanguage = GetRewritePivotLanguage(sourceLanguage, languages);
            var intermediateText = await _translationService.TranslateTextAsync(trimmedText, sourceLanguage, pivotLanguage, cancellationToken);
            return await _translationService.TranslateTextAsync(intermediateText, pivotLanguage, sourceLanguage, cancellationToken);
        }

        public Uri BuildLanguageToolUri(string relativePath)
        {
            var baseUrl = (_settingsProvider().LanguageToolServerUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = "https://lang.kemmuammer.com/v2/";

            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";

            return new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath);
        }

        public static LanguageToolWriteResult BuildLanguageToolWriteResult(string text, IEnumerable<LanguageToolMatch> matches)
        {
            var segments = new List<LanguageToolSegment>();
            var corrected = new StringBuilder();
            int cursor = 0;

            foreach (var match in matches.OrderBy(x => x.offset))
            {
                var replacement = match.replacements?.FirstOrDefault()?.value;
                if (string.IsNullOrWhiteSpace(replacement))
                    continue;

                if (match.offset < cursor || match.length < 0 || match.offset + match.length > text.Length)
                    continue;

                var unchanged = text.Substring(cursor, match.offset - cursor);
                if (unchanged.Length > 0)
                {
                    segments.Add(new LanguageToolSegment { Text = unchanged });
                    corrected.Append(unchanged);
                }

                segments.Add(new LanguageToolSegment { Text = replacement, IsChanged = true });
                corrected.Append(replacement);
                cursor = match.offset + match.length;
            }

            if (cursor < text.Length)
            {
                var unchanged = text[cursor..];
                segments.Add(new LanguageToolSegment { Text = unchanged });
                corrected.Append(unchanged);
            }

            if (segments.Count == 0)
                segments.Add(new LanguageToolSegment { Text = text });

            return new LanguageToolWriteResult
            {
                CorrectedText = corrected.Length == 0 ? text : corrected.ToString(),
                Segments = segments
            };
        }

        public static string GetRewritePivotLanguage(string sourceLanguage, IReadOnlyCollection<LtLanguage> languages)
        {
            string[] preferredLanguages = sourceLanguage.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? ["de", "fr", "es"]
                : ["en", "de", "fr", "es"];

            foreach (var language in preferredLanguages)
            {
                if (!language.Equals(sourceLanguage, StringComparison.OrdinalIgnoreCase)
                    && IsTranslationLanguageAvailable(languages, language))
                {
                    return language;
                }
            }

            foreach (var language in languages)
            {
                if (!language.code.Equals(sourceLanguage, StringComparison.OrdinalIgnoreCase))
                    return language.code;
            }

            throw new InvalidOperationException("No rewrite pivot language available.");
        }

        public static bool IsTranslationLanguageAvailable(IReadOnlyCollection<LtLanguage> languages, string code)
        {
            if (languages.Count == 0)
                return true;

            return languages.Any(x => x.code.Equals(code, StringComparison.OrdinalIgnoreCase));
        }

        private static async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> operation, CancellationToken cancellationToken)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppLogger.Log(ex, "Writing service request failed, retrying");
                await Task.Delay(250, cancellationToken);
                return await operation();
            }
        }
    }
}
