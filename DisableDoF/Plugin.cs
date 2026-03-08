using System;
using System.Collections.Generic;
using Dalamud.Game.Config;
using Dalamud.IoC;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DisableDoF;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private const string DepthOfFieldLabelText = "enable depth of field";
    private const int ScanIntervalFrames = 5;
    private const int VisibilityLogIntervalAttempts = 60;

    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private bool? lastKnownGposeState;
    private bool restorePending;
    private bool pendingUiDisable;
    private int scanFrameCounter;
    private int scanAttempts;
    private bool? originalDepthOfField;
    private bool? originalDepthOfFieldDx11;
    private bool? originalGraphicsDepthOfField;

    public string Name => "DisableDoF";

    public Plugin()
    {
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        pendingUiDisable = false;
        RestoreDepthOfField();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var isGPosing = ClientState.IsGPosing;
        if (lastKnownGposeState != isGPosing)
        {
            lastKnownGposeState = isGPosing;
            if (isGPosing)
            {
                OnGposeEntered();
            }
            else
            {
                OnGposeExited();
            }
        }

        if (!isGPosing || !pendingUiDisable)
        {
            return;
        }

        scanFrameCounter++;
        if (scanFrameCounter % ScanIntervalFrames != 0)
        {
            return;
        }

        try
        {
            // Keep the live render flag off while GPose finishes initializing its UI.
            DisableLiveDepthOfField();
            scanAttempts++;

            if (TryDisableDepthOfFieldFromUi())
            {
                pendingUiDisable = false;
                return;
            }

            if (scanAttempts % VisibilityLogIntervalAttempts == 0)
            {
                LogVisibleAddons();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DisableDoF failed while scanning the GPose UI.");
        }
    }

    private void OnGposeEntered()
    {
        CaptureOriginalSettings();
        DisableStoredDepthOfField();
        DisableLiveDepthOfField();

        pendingUiDisable = true;
        scanFrameCounter = 0;
        scanAttempts = 0;
    }

    private void OnGposeExited()
    {
        pendingUiDisable = false;
        scanFrameCounter = 0;
        scanAttempts = 0;
        RestoreDepthOfField();
    }

    private void CaptureOriginalSettings()
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
        }

        if (TryGetConfig(SystemConfigOption.DepthOfField_DX11, out var depthOfFieldDx11))
        {
            originalDepthOfFieldDx11 = depthOfFieldDx11;
            capturedAny = true;
        }

        var graphicsConfig = GraphicsConfig.Instance();
        if (graphicsConfig != null)
        {
            originalGraphicsDepthOfField = graphicsConfig->DepthOfField;
            capturedAny = true;
        }

        restorePending = capturedAny;
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

    private void DisableStoredDepthOfField()
    {
        SetConfigIfNeeded(SystemConfigOption.DepthOfField, false);
        SetConfigIfNeeded(SystemConfigOption.DepthOfField_DX11, false);
    }

    private static void DisableLiveDepthOfField()
    {
        var graphicsConfig = GraphicsConfig.Instance();
        if (graphicsConfig == null)
        {
            return;
        }

        graphicsConfig->DepthOfField = false;
    }

    private void RestoreDepthOfField()
    {
        if (!restorePending)
        {
            return;
        }

        RestoreConfig(SystemConfigOption.DepthOfField, originalDepthOfField);
        RestoreConfig(SystemConfigOption.DepthOfField_DX11, originalDepthOfFieldDx11);

        if (originalGraphicsDepthOfField is bool graphicsDepthOfField)
        {
            var graphicsConfig = GraphicsConfig.Instance();
            if (graphicsConfig != null)
            {
                graphicsConfig->DepthOfField = graphicsDepthOfField;
            }
        }

        originalDepthOfField = null;
        originalDepthOfFieldDx11 = null;
        originalGraphicsDepthOfField = null;
        restorePending = false;
    }

    private static void RestoreConfig(SystemConfigOption option, bool? originalValue)
    {
        if (originalValue is not bool value)
        {
            return;
        }

        SetConfigIfNeeded(option, value);
    }

    private bool TryDisableDepthOfFieldFromUi()
    {
        var unitManager = RaptureAtkUnitManager.Instance();
        if (unitManager == null)
        {
            return false;
        }

        foreach (var entry in unitManager->AllLoadedUnitsList.Entries)
        {
            var addon = entry.Value;
            if (!IsAddonReady(addon))
            {
                continue;
            }

            if (!TryFindDepthOfFieldCheckbox(addon, out var checkbox))
            {
                continue;
            }

            if (checkbox->IsChecked)
            {
                DisableStoredDepthOfField();
                DisableLiveDepthOfField();
                ClickCheckBox(checkbox, addon);
                checkbox->SetChecked(false);
                Log.Information("DisableDoF unchecked the GPose depth-of-field option on addon {AddonName}.", addon->NameString);
            }
            else
            {
                Log.Information("DisableDoF found the GPose depth-of-field option already unchecked on addon {AddonName}.", addon->NameString);
            }

            return true;
        }

        return false;
    }

    private static bool IsAddonReady(AtkUnitBase* addon)
        => addon != null
           && addon->IsVisible
           && addon->IsReady
           && addon->UldManager.LoadedState == AtkLoadState.Loaded
           && addon->IsFullyLoaded();

    private static bool TryFindDepthOfFieldCheckbox(AtkUnitBase* addon, out AtkComponentCheckBox* checkbox)
    {
        checkbox = null;
        return addon->RootNode != null
               && TryFindDepthOfFieldCheckboxInTree(addon->RootNode, out checkbox);
    }

    private static bool TryFindDepthOfFieldCheckboxInTree(AtkResNode* node, out AtkComponentCheckBox* checkbox)
    {
        for (var currentNode = node; currentNode != null; currentNode = currentNode->PrevSiblingNode)
        {
            if (TryMatchDepthOfFieldCheckbox(currentNode, out checkbox))
            {
                return true;
            }

            if (currentNode->GetNodeType() is not NodeType.Component && currentNode->ChildNode != null)
            {
                if (TryFindDepthOfFieldCheckboxInTree(currentNode->ChildNode, out checkbox))
                {
                    return true;
                }
            }

            if (currentNode->GetNodeType() is not NodeType.Component)
            {
                continue;
            }

            var componentNode = currentNode->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
            {
                continue;
            }

            var componentRoot = componentNode->Component->UldManager.RootNode;
            if (componentRoot != null && TryFindDepthOfFieldCheckboxInTree(componentRoot, out checkbox))
            {
                return true;
            }
        }

        checkbox = null;
        return false;
    }

    private static bool TryMatchDepthOfFieldCheckbox(AtkResNode* node, out AtkComponentCheckBox* checkbox)
    {
        checkbox = null;
        if (node == null || node->GetNodeType() is not NodeType.Component)
        {
            return false;
        }

        var componentNode = node->GetAsAtkComponentNode();
        if (componentNode == null || componentNode->Component == null)
        {
            return false;
        }

        if (componentNode->Component->GetComponentType() is not ComponentType.CheckBox)
        {
            return false;
        }

        var candidate = node->GetAsAtkComponentCheckBox();
        if (candidate == null)
        {
            return false;
        }

        var componentRoot = componentNode->Component->UldManager.RootNode;
        var labelText = componentRoot != null ? CollectNodeText(componentRoot) : string.Empty;
        if (string.IsNullOrWhiteSpace(labelText))
        {
            labelText = CollectNodeText(node);
        }

        if (!labelText.Contains(DepthOfFieldLabelText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        checkbox = candidate;
        return true;
    }

    private static string CollectNodeText(AtkResNode* node)
    {
        List<string> textParts = [];
        AppendNodeText(node, textParts);
        return string.Join(" ", textParts);
    }

    private static void AppendNodeText(AtkResNode* node, List<string> textParts)
    {
        for (var currentNode = node; currentNode != null; currentNode = currentNode->PrevSiblingNode)
        {
            var nodeText = GetNodeText(currentNode);
            if (!string.IsNullOrWhiteSpace(nodeText))
            {
                textParts.Add(nodeText);
            }

            if (currentNode->GetNodeType() is not NodeType.Component && currentNode->ChildNode != null)
            {
                AppendNodeText(currentNode->ChildNode, textParts);
            }

            if (currentNode->GetNodeType() is not NodeType.Component)
            {
                continue;
            }

            var componentNode = currentNode->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
            {
                continue;
            }

            var componentRoot = componentNode->Component->UldManager.RootNode;
            if (componentRoot != null)
            {
                AppendNodeText(componentRoot, textParts);
            }
        }
    }

    private static string? GetNodeText(AtkResNode* node)
    {
        var textNode = node->GetAsAtkTextNode();
        if (textNode == null)
        {
            return null;
        }

        var textValue = MemoryHelper.ReadSeString(&textNode->NodeText).TextValue;
        return string.IsNullOrWhiteSpace(textValue) ? null : textValue.Trim();
    }

    private static void ClickCheckBox(AtkComponentCheckBox* checkbox, AtkUnitBase* addon)
    {
        if (checkbox == null || addon == null || checkbox->OwnerNode == null)
        {
            return;
        }

        var node = checkbox->OwnerNode->AtkResNode;
        var evt = (AtkEvent*)node.AtkEventManager.Event;
        if (evt == null)
        {
            return;
        }

        var eventData = stackalloc AtkEventData[1];
        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt, eventData);
    }

    private static void LogVisibleAddons()
    {
        var unitManager = RaptureAtkUnitManager.Instance();
        if (unitManager == null)
        {
            return;
        }

        List<string> visibleAddons = [];
        foreach (var entry in unitManager->AllLoadedUnitsList.Entries)
        {
            var addon = entry.Value;
            if (addon != null && addon->IsVisible)
            {
                visibleAddons.Add(addon->NameString);
            }
        }

        Log.Warning(
            "DisableDoF is still searching for the GPose depth-of-field checkbox. Visible addons: {VisibleAddons}",
            string.Join(", ", visibleAddons));
    }
}
