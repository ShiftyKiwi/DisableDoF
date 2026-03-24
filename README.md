# DisableDoF

DisableDoF is a minimal Dalamud plugin for Final Fantasy XIV that turns the Group Pose depth-of-field toggle off whenever you enter GPose, then restores your previous graphics setting when you leave.

## Compatibility

- FFXIV patch 7.45 HotFix Patch 2 target
- Dalamud API 14
- .NET 10 SDK

## Behavior

When GPose starts, the plugin captures the current native FFXIV depth-of-field state, turns the vanilla GPose `Enable depth of field` option off for that session, and restores the captured value after GPose ends or the plugin unloads.

DisableDoF only performs that automatic toggle once per GPose session. If you manually re-enable the vanilla depth-of-field checkbox while you are still in the same GPose session, the plugin will not turn it off again until you leave GPose and enter a new session.

The `DepthOfField` and `DepthOfField_DX11` settings referenced by the plugin are FFXIV's own graphics options. They are not GShade/ReShade shader settings. External post-processing effects such as `DOF.fx` remain separate and should still work normally.

## Build

Open `DisableDoF.sln` and build the solution with the .NET 10 SDK installed. The packaged plugin output lands under `DisableDoF/bin/x64/<Configuration>/DisableDoF/`.

## Custom Repo URL

`https://raw.githubusercontent.com/ShiftyKiwi/DisableDoF/main/pluginmaster.json`

