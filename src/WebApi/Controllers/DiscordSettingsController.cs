using Microsoft.AspNetCore.Mvc;

using WebApi.Discord;

namespace WebApi.Controllers;

public sealed record DiscordSettingsViewDto(bool Enabled, bool HasToken, string? TokenPreview, ulong ChannelId);

public sealed record DiscordSettingsUpdateRequest(bool Enabled, string? BotToken, ulong ChannelId);

[ApiController]
[Route("api/settings/discord")]
public sealed class DiscordSettingsController(IDiscordSettingsStore store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DiscordSettingsViewDto>> Get(CancellationToken ct)
    {
        var snapshot = await store.GetAsync(ct);
        return Ok(BuildView(snapshot));
    }

    [HttpPut]
    public async Task<ActionResult<DiscordSettingsViewDto>> Update(
        [FromBody] DiscordSettingsUpdateRequest request,
        CancellationToken ct)
    {
        var current = await store.GetAsync(ct);
        var effectiveToken = ResolveToken(current.BotToken, request.BotToken);

        if (request.Enabled)
        {
            if (string.IsNullOrEmpty(effectiveToken))
            {
                return BadRequest(new { field = "botToken", message = "啟用前需先設定 bot token." });
            }
            if (request.ChannelId == 0UL)
            {
                return BadRequest(new { field = "channelId", message = "啟用前需先設定 channel id (必須大於 0)." });
            }
        }

        var next = new DiscordSettings(request.Enabled, effectiveToken, request.ChannelId);
        var updated = await store.UpdateAsync(next, ct);
        return Ok(BuildView(updated));
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await store.ResetAsync(ct);
        return NoContent();
    }

    private static string ResolveToken(string currentToken, string? requestToken) =>
        requestToken switch
        {
            null => currentToken,
            "" => string.Empty,
            _ => requestToken,
        };

    private static DiscordSettingsViewDto BuildView(DiscordSettingsSnapshot snapshot) =>
        new(
            Enabled: snapshot.Enabled,
            HasToken: snapshot.HasToken,
            TokenPreview: BuildPreview(snapshot.BotToken),
            ChannelId: snapshot.ChannelId);

    private static string? BuildPreview(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var tail = token.Length <= 4 ? token : token[^4..];
        return $"...{tail}";
    }
}
