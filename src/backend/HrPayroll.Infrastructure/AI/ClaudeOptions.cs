namespace HrPayroll.Infrastructure.AI;

public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    public bool Enabled { get; set; } = false;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-3-5-sonnet-latest";
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
}
