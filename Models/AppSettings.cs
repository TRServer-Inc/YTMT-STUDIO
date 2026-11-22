namespace YTMT_STUDIO.Models;

public sealed class AppSettings
{
    public string BotApiUrl { get; set; } = "https://ytmt-bot.onrender.com";
    public string TerminalUrl { get; set; } = "https://bot-site-zpli.onrender.com";
    public int RefreshIntervalSeconds { get; set; } = 15;
    public string RenderServiceId { get; set; } = string.Empty;
    public string DiscordClientId { get; set; } = string.Empty;
}