namespace HrPayroll.Infrastructure.Integrations;

public sealed class MudadOptions
{
    public const string SectionName = "Mudad";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}
