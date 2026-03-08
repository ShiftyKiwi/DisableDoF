# DisableDoF

DisableDoF is a minimal Dalamud plugin for Final Fantasy XIV that turns the Group Pose depth-of-field toggle off whenever you enter GPose, then restores your previous graphics setting when you leave.

## Compatibility

- FFXIV patch 7.45 target
- Dalamud API 14
- .NET 10 SDK

## Behavior

When `IClientState.IsGPosing` flips on, the plugin captures the current `DepthOfField` and `DepthOfField_DX11` system config values, forces them off for the GPose session, and restores the captured values after GPose ends or the plugin unloads.

## Build

Open `DisableDoF.sln` and build the solution with the .NET 10 SDK installed. The packaged plugin output lands under `DisableDoF/bin/x64/<Configuration>/DisableDoF/`.

## Custom Repo URL

`https://raw.githubusercontent.com/ShiftyKiwi/DisableDoF/main/pluginmaster.json`
