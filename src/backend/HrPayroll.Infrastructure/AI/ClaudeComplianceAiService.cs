using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HrPayroll.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HrPayroll.Infrastructure.AI;

public sealed class ClaudeComplianceAiService : IComplianceAiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeComplianceAiService> _logger;

    public ClaudeComplianceAiService(
        IHttpClientFactory httpClientFactory,
        IOptions<ClaudeOptions> options,
        ILogger<ClaudeComplianceAiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ComplianceAiResult> GenerateBriefAsync(ComplianceAiInput input, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return BuildFallback(input, "fallback-disabled");
        }

        try
        {
            var prompt = BuildPrompt(input);
            var httpClient = _httpClientFactory.CreateClient("ClaudeCompliance");

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl);
            request.Headers.Add("x-api-key", _options.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new
            {
                model = _options.Model,
                max_tokens = 450,
                temperature = 0.2,
                system = input.Language.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                    ? "اكتب موجزا تنفيذيا منضبطا عن التزام شركة سعودية بالموارد البشرية والرواتب. لا تخترع بيانات."
                    : "Write an executive compliance brief for a Saudi HR/payroll company. Do not hallucinate numbers.",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Claude API failed: {StatusCode} {Body}", (int)response.StatusCode, errorBody);
                return BuildFallback(input, "fallback-api-error");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);

            var text = ExtractText(doc.RootElement);
            if (string.IsNullOrWhiteSpace(text))
            {
                return BuildFallback(input, "fallback-empty");
            }

            return new ComplianceAiResult("claude", false, text.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Claude generation failed, using fallback.");
            return BuildFallback(input, "fallback-exception");
        }
    }

    private static string BuildPrompt(ComplianceAiInput input)
    {
        var recommendations = string.Join("; ", input.Recommendations);
        var userPrompt = string.IsNullOrWhiteSpace(input.UserPrompt) ? "None" : input.UserPrompt.Trim();

        return $"""
        Generate a concise compliance briefing using this data.
        Score: {input.Score}/100
        Grade: {input.Grade}
        SaudizationPercent: {input.SaudizationPercent:F1}
        WpsCompanyReady: {input.WpsCompanyReady}
        EmployeesMissingPaymentData: {input.EmployeesMissingPaymentData}
        CriticalAlerts: {input.CriticalAlerts}
        WarningAlerts: {input.WarningAlerts}
        NoticeAlerts: {input.NoticeAlerts}
        Recommendations: {recommendations}
        UserPrompt: {userPrompt}

        Requirements:
        - 4 to 7 short bullets
        - Include immediate actions and timeline (7/30/60 days)
        - Keep to plain business language
        """;
    }

    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "text", StringComparison.OrdinalIgnoreCase) &&
                item.TryGetProperty("text", out var text))
            {
                parts.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static ComplianceAiResult BuildFallback(ComplianceAiInput input, string provider)
    {
        if (input.Language.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
        {
            var text = string.Join("\n", new[]
            {
                $"درجة الالتزام الحالية: {input.Score}/100 ({input.Grade}).",
                $"- المخاطر الحرجة خلال 7 أيام: {input.CriticalAlerts}.",
                $"- المخاطر المتوسطة خلال 30 يوما: {input.WarningAlerts}.",
                $"- جاهزية WPS للشركة: {(input.WpsCompanyReady ? "مكتملة" : "غير مكتملة")}، موظفون ينقصهم بيانات دفع: {input.EmployeesMissingPaymentData}.",
                $"- نسبة السعودة الحالية: {input.SaudizationPercent:F1}%.",
                "- خطة عمل: إغلاق المخاطر الحرجة خلال 7 أيام، استكمال بيانات الدفع خلال 30 يوما، وخفض إشعارات الانتهاء خلال 60 يوما."
            });
            return new ComplianceAiResult(provider, true, text);
        }

        var english = string.Join("\n", new[]
        {
            $"Current compliance score: {input.Score}/100 ({input.Grade}).",
            $"- Critical risks due within 7 days: {input.CriticalAlerts}.",
            $"- Medium risks due within 30 days: {input.WarningAlerts}.",
            $"- WPS company readiness: {(input.WpsCompanyReady ? "complete" : "incomplete")}; employees missing payment profile: {input.EmployeesMissingPaymentData}.",
            $"- Current Saudization ratio: {input.SaudizationPercent:F1}%.",
            "- Action plan: close critical risks in 7 days, complete payment data in 30 days, and reduce document-expiry alerts within 60 days."
        });
        return new ComplianceAiResult(provider, true, english);
    }
}
