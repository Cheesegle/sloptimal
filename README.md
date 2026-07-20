<p align="center">
  <img width="500" alt="osu! logo" src="assets/lazer.png">
</p>

# sloptimal

**sloptimal is an unofficial, experimental fork of [osu!lazer](https://github.com/ppy/osu).** It adds deterministic aim-flow guidance to the osu! beatmap editor. It is not an official osu! release and is not affiliated with or endorsed by ppy.

## Aim-flow editor tools

While placing a hit circle, the editor can show:

- A placement heatmap ranking possible positions from recent cursor flow, timing, spacing, turn continuity, and motion cost.
- A raw `0–100` score when hovering a heatmap cell. Scores are heuristic rankings, not probabilities.
- One optional main cursor ribbon through recent hit positions and the native circle placement preview.
- **Rhythm / BPM correction** for maps whose apparent cadence is half or double the intended rhythm.
- **Aim velocity / spacing** to tighten or widen the preferred placement distance independently.
- Deterministic **Minimum jerk**, **1€ smoothed**, and **Arm + wrist** motion models.

The tool uses no machine learning. It does not create predicted-circle points, synthetic circle markers, hover-generated continuation ribbons, or other ghost previews.

See [AIM_FLOW_PREVIEW.md](AIM_FLOW_PREVIEW.md) for the scoring model, controls, calibration data, performance details, and research references.

## Using it

With a packaged Windows version:

1. Extract the archive if necessary.
2. Start it with `launch-aim-flow.cmd`. The launcher prevents the official updater from replacing the custom portable build.
3. Open a beatmap in the editor and select the hit-circle placement tool.
4. Expand the right-side **aim flow** toolbox.
5. Enable **Placement heatmap**, then adjust the rhythm, spacing, and motion-model controls as needed.
6. Optionally enable **Main cursor ribbon**.

Guidance appears when a circle is being previewed and at least two earlier cursor waypoints establish an incoming flow.

## Building from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). While the feature PR is pending, clone its branch directly:

```shell
git clone --branch agent/aim-flow-heatmap --single-branch https://github.com/Cheesegle/sloptimal.git
cd sloptimal
dotnet run --project ./osu.Desktop/osu.Desktop.csproj -c Release
```

The active changes are tracked in [draft PR #1](https://github.com/Cheesegle/sloptimal/pull/1). Once they are merged, a normal clone of the default branch is sufficient.

To create a self-contained Windows x64 package:

```powershell
dotnet publish .\osu.Desktop\osu.Desktop.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
Copy-Item .\launch-aim-flow.cmd, .\AIM_FLOW_PREVIEW.md, .\LICENCE .\publish\win-x64\
```

## Tests

Run the focused aim-flow and editor suite with:

```shell
dotnet test osu.Game.Rulesets.Osu.Tests/osu.Game.Rulesets.Osu.Tests.csproj -c Release --filter "FullyQualifiedName~AimFlowAnalysisTest|FullyQualifiedName~osu.Game.Rulesets.Osu.Tests.Editor.TestSceneOsuEditor"
```

The current implementation passes all 46 focused tests.

## Upstream and licence

This project is based on [ppy/osu](https://github.com/ppy/osu) and retains its MIT licence. See [LICENCE](LICENCE) for the copyright and licence terms.

The licence does not grant rights to the osu! or ppy trademarks. Game resources may have separate terms; see [ppy/osu-resources](https://github.com/ppy/osu-resources) for details.
