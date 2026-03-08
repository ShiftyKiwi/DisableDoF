using Dalamud.Game.Config;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DisableDoF;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;

    public string Name => "DisableDoF";

    private bool? lastKnownGposeState;
    private bool restorePending;
    private bool? originalDepthOfField;
    private bool? originalDepthOfFieldDx11;

    public Plugin()
    {
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        RestoreDepthOfField();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var isGPosing = ClientState.IsGPosing;
        if (lastKnownGposeState == isGPosing)
        {
            return;
        }

        lastKnownGposeState = isGPosing;

        if (isGPosing)
        {
            DisableDepthOfField();
            return;
        }

        RestoreDepthOfField();
    }

    private void DisableDepthOfField()
    {
        if (restorePending)
        {
            return;
        }

        var capturedAny = false;

        if (TryGetConfig(SystemConfigOption.DepthOfField, out var depthOfField))
        {
            originalDepthOfField = depthOfField;
            capturedAny = true;
            SetConfigIfNeeded(SystemConfigOption.DepthOfField, false);
        }

        if (TryGetConfig(SystemConfigOption.DepthOfField_DX11, out var depthOfFieldDx11))
        {
            originalDepthOfFieldDx11 = depthOfFieldDx11;
            capturedAny = true;
            SetConfigIfNeeded(SystemConfigOption.DepthOfField_DX11, false);
        }

        restorePending = capturedAny;
    }

    private void RestoreDepthOfField()
    {
        if (!restorePending)
        {
            return;
        }

        RestoreConfig(SystemConfigOption.DepthOfField, originalDepthOfField);
        RestoreConfig(SystemConfigOption.DepthOfField_DX11, originalDepthOfFieldDx11);

        originalDepthOfField = null;
        originalDepthOfFieldDx11 = null;
        restorePending = false;
    }

    private static bool TryGetConfig(SystemConfigOption option, out bool value)
        => GameConfig.TryGet(option, out value);

    private static void SetConfigIfNeeded(SystemConfigOption option, bool targetValue)
    {
        if (TryGetConfig(option, out var currentValue) && currentValue == targetValue)
        {
            return;
        }

        GameConfig.Set(option, targetValue);
    }

    private static void RestoreConfig(SystemConfigOption option, bool? originalValue)
    {
        if (originalValue is not bool value)
        {
            return;
        }

        SetConfigIfNeeded(option, value);
    }
}
