// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Testing;
using osu.Game.Rulesets.Osu.Edit.AimFlow;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Editor
{
    [TestFixture]
    public partial class TestSceneOsuEditor : EditorTestScene
    {
        protected override Ruleset CreateEditorRuleset() => new OsuRuleset();

        [Test]
        public void TestAimFlowPreviewLoads()
        {
            AimFlowOverlay overlay = null!;
            AimFlowToolboxGroup toolbox = null!;
            double heatmapScore = 0;

            AddStep("enable aim flow", () =>
            {
                toolbox = Editor.ChildrenOfType<AimFlowToolboxGroup>().Single();
                toolbox.Enabled.Value = true;
                toolbox.HeatmapEnabled.Value = true;
                overlay = Editor.ChildrenOfType<AimFlowOverlay>().Single();
            });
            AddStep("add history and placement", () =>
            {
                EditorBeatmap.Clear();
                EditorBeatmap.AddRange(new HitCircle[]
                {
                    new HitCircle { Position = new Vector2(110, 285), StartTime = 0 },
                    new HitCircle { Position = new Vector2(350, 265), StartTime = 120 },
                });
                EditorBeatmap.PlacementObject.Value = new HitCircle { Position = new Vector2(135, 150), StartTime = 240 };
            });
            AddUntilStep("overlay is visible", () => overlay.Alpha > 0.9f);
            AddUntilStep("heatmap is visible", () => overlay.HeatmapVisible);
            AddAssert("only main ribbon paths exist", () => overlay.ChildrenOfType<SmoothPath>().Count() == 3);
            AddAssert("no aim flow circle ghosts", () => !overlay.ChildrenOfType<CircularContainer>().Any());
            AddStep("hover heatmap cell", () => InputManager.MoveMouseTo(overlay.ToScreenSpace(new Vector2(360, 120))));
            AddUntilStep("cell score visible", () => overlay.HeatmapScoreVisible && overlay.HoveredHeatmapScore > 0);
            AddAssert("hover adds no ghost paths", () => overlay.ChildrenOfType<SmoothPath>().Count() == 3);
            AddAssert("main ribbon remains visible", () => overlay.ChildrenOfType<SmoothPath>().Count(path => path.Alpha > 0.01f) == 2);
            AddStep("remember heatmap score", () => heatmapScore = overlay.HoveredHeatmapScore!.Value);
            AddStep("double calculation rhythm", () => toolbox.RhythmMultiplier.Value = 2);
            AddUntilStep("rhythm refreshes heatmap", () => Math.Abs(overlay.HoveredHeatmapScore!.Value - heatmapScore) > 0.001);
            AddStep("remember rhythm score", () => heatmapScore = overlay.HoveredHeatmapScore!.Value);
            AddStep("increase spacing", () => toolbox.SpacingMultiplier.Value = 1.4f);
            AddUntilStep("spacing refreshes heatmap", () => Math.Abs(overlay.HoveredHeatmapScore!.Value - heatmapScore) > 0.001);
            AddStep("disable main ribbon", () => toolbox.Enabled.Value = false);
            AddUntilStep("main ribbon hidden", () => overlay.ChildrenOfType<SmoothPath>().All(path => path.Alpha < 0.01f));
            AddStep("move to another heatmap cell", () => InputManager.MoveMouseTo(overlay.ToScreenSpace(new Vector2(344, 136))));
            AddUntilStep("heatmap-only cell score visible", () => overlay.HoveredHeatmapPosition.HasValue
                                                                   && Vector2.Distance(overlay.HoveredHeatmapPosition.Value, new Vector2(344, 136)) < 0.01f
                                                                   && overlay.HoveredHeatmapScore > 0);
            AddAssert("heatmap remains without ribbon", () => overlay.HeatmapVisible);
            AddStep("hover clear margin", () => InputManager.MoveMouseTo(overlay.ToScreenSpace(new Vector2(8, 8))));
            AddUntilStep("margin clears score", () => overlay.HoveredHeatmapScore == null);
            AddAssert("preview remains visible", () => overlay.Alpha > 0.9f);
        }
    }
}
