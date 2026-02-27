namespace HrPayroll.Infrastructure.Integrations;

public sealed class QiwaOptions
{
    public const string SectionName = "Qiwa";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}
