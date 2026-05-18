using Discord;
using Discord.Rest;

using Microsoft.AspNetCore.Mvc;

using WebApi.Discord;

namespace WebApi.Controllers;

public sealed record DiscordSettingsViewDto(bool Enabled, bool HasToken, string? TokenPreview, ulong ChannelId, bool NotifyAllAlerts);

public sealed record DiscordSettingsUpdateRequest(bool Enabled, string? BotToken, ulong ChannelId, bool NotifyAllAlerts);

public sealed record DiscordSettingsTestRequest(string? BotToken, ulong ChannelId);

public sealed record DiscordSettingsTestResponse(bool Success, string Message, string? Field = null);

[ApiController]
[Route("api/settings/discord")]
public sealed class DiscordSettingsController(
    IDiscordSettingsStore store,
    ILogger<DiscordSettingsController> logger) : ControllerBase
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

        var next = new DiscordSettings(request.Enabled, effectiveToken, request.ChannelId, request.NotifyAllAlerts);
        var updated = await store.UpdateAsync(next, ct);
        return Ok(BuildView(updated));
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await store.ResetAsync(ct);
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<ActionResult<DiscordSettingsTestResponse>> Test(
        [FromBody] DiscordSettingsTestRequest request,
        CancellationToken ct)
    {
        var current = await store.GetAsync(ct);
        var effectiveToken = ResolveToken(current.BotToken, request.BotToken);

        if (string.IsNullOrEmpty(effectiveToken))
        {
            return BadRequest(new DiscordSettingsTestResponse(false, "請先輸入 bot token.", "botToken"));
        }
        if (request.ChannelId == 0UL)
        {
            return BadRequest(new DiscordSettingsTestResponse(false, "請先輸入 channel id (必須大於 0).", "channelId"));
        }

        var client = new DiscordRestClient();
        try
        {
            try
            {
                await client.LoginAsync(TokenType.Bot, effectiveToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DiscordSettingsController.Test: login failed");
                return Ok(new DiscordSettingsTestResponse(false, $"登入失敗: {ex.Message}", "botToken"));
            }

            IChannel? rawChannel;
            try
            {
                rawChannel = await client.GetChannelAsync(request.ChannelId, options: null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DiscordSettingsController.Test: failed to fetch channel {ChannelId}", request.ChannelId);
                return Ok(new DiscordSettingsTestResponse(false, $"無法取得 channel: {ex.Message}", "channelId"));
            }

            if (rawChannel is null)
            {
                return Ok(new DiscordSettingsTestResponse(false, "找不到指定的 channel id (bot 可能未加入該伺服器).", "channelId"));
            }
            if (rawChannel is not IMessageChannel messageChannel)
            {
                return Ok(new DiscordSettingsTestResponse(
                    false,
                    $"指定的 channel 不是訊息頻道 (type={rawChannel.GetType().Name}).",
                    "channelId"));
            }

            var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            try
            {
                await messageChannel.SendMessageAsync($"Discord 通知測試 - {stamp}", options: null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DiscordSettingsController.Test: SendMessageAsync failed for channel {ChannelId}", request.ChannelId);
                return Ok(new DiscordSettingsTestResponse(false, $"訊息發送失敗 (可能權限不足): {ex.Message}"));
            }

            return Ok(new DiscordSettingsTestResponse(true, $"測試訊息已送出 ({stamp})."));
        }
        finally
        {
            try { await client.LogoutAsync(); }
            catch (Exception ex) { logger.LogDebug(ex, "DiscordSettingsController.Test: logout threw; continuing"); }
            try { client.Dispose(); }
            catch (Exception ex) { logger.LogDebug(ex, "DiscordSettingsController.Test: dispose threw; ignoring"); }
        }
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
            ChannelId: snapshot.ChannelId,
            NotifyAllAlerts: snapshot.NotifyAllAlerts);

    private static string? BuildPreview(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var tail = token.Length <= 4 ? token : token[^4..];
        return $"...{tail}";
    }
}
