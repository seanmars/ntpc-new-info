using Discord;
using Discord.Rest;

namespace WebApi.Discord;

public sealed class DiscordBotLifecycleService(
    IDiscordSettingsStore store,
    DiscordBotState state,
    ILogger<DiscordBotLifecycleService> logger) : BackgroundService
{
    private EventHandler<DiscordSettingsSnapshot>? _subscription;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = await store.GetAsync(stoppingToken);
        await ReconcileAsync(initial, stoppingToken);

        _subscription = OnSettingsChanged;
        store.SettingsChanged += _subscription;

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null)
        {
            store.SettingsChanged -= _subscription;
            _subscription = null;
        }

        await base.StopAsync(cancellationToken);

        try
        {
            await state.ReconfigGate.WaitAsync(cancellationToken);
            try
            {
                await DisposeClientLockedAsync();
            }
            finally
            {
                state.ReconfigGate.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DiscordBotLifecycleService: error while disposing Discord client on shutdown.");
        }
    }

    private void OnSettingsChanged(object? sender, DiscordSettingsSnapshot snapshot)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ReconcileAsync(snapshot, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DiscordBotLifecycleService: reconcile failed for SettingsChanged event.");
            }
        });
    }

    private async Task ReconcileAsync(DiscordSettingsSnapshot snapshot, CancellationToken ct)
    {
        await state.ReconfigGate.WaitAsync(ct);
        try
        {
            if (!snapshot.Enabled || string.IsNullOrEmpty(snapshot.BotToken))
            {
                if (state.Client is not null)
                {
                    await DisposeClientLockedAsync();
                    logger.LogInformation("DiscordBotLifecycleService: Discord notifications disabled; bot logged out.");
                }
                state.IsReady = false;
                state.CurrentLoggedInToken = null;
                return;
            }

            if (state.Client is not null && string.Equals(state.CurrentLoggedInToken, snapshot.BotToken, StringComparison.Ordinal))
            {
                state.IsReady = true;
                return;
            }

            await DisposeClientLockedAsync();

            var client = new DiscordRestClient();
            try
            {
                await client.LoginAsync(TokenType.Bot, snapshot.BotToken);
            }
            catch (Exception ex)
            {
                client.Dispose();
                state.IsReady = false;
                state.CurrentLoggedInToken = null;
                logger.LogError(ex, "DiscordBotLifecycleService: failed to log in to Discord.");
                return;
            }

            state.Client = client;
            state.CurrentLoggedInToken = snapshot.BotToken;
            state.IsReady = true;

            logger.LogInformation(
                "DiscordBotLifecycleService: logged in to Discord as {BotUsername}#{Discriminator}.",
                client.CurrentUser?.Username ?? "<unknown>",
                client.CurrentUser?.Discriminator ?? "0000");
        }
        finally
        {
            state.ReconfigGate.Release();
        }
    }

    private async Task DisposeClientLockedAsync()
    {
        if (state.Client is null) return;

        try
        {
            await state.Client.LogoutAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "DiscordBotLifecycleService: logout threw; continuing to dispose.");
        }

        try
        {
            state.Client.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "DiscordBotLifecycleService: dispose threw; ignoring.");
        }

        state.Client = null;
    }
}
