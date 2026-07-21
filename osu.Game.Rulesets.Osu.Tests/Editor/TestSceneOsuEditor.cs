// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Testing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Edit.AimFlow;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
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

        [Test]
        public void TestAimFlowSupportsSliderPlacementAndSelectedObjects()
        {
            AimFlowOverlay overlay = null!;
            AimFlowToolboxGroup toolbox = null!;
            HitCircle firstCircle = null!;
            HitCircle secondCircle = null!;
            HitCircle selectedCircle = null!;
            Slider slider = null!;
            AimFlowHeatmap expectedHeatmap = AimFlowHeatmap.Empty;
            double[] stationaryHeatmap = null!;

            AddStep("enable deterministic aim flow", () =>
            {
                toolbox = Editor.ChildrenOfType<AimFlowToolboxGroup>().Single();
                toolbox.Enabled.Value = true;
                toolbox.HeatmapEnabled.Value = true;
                toolbox.Model.Value = AimFlowModel.MinimumJerk;
                toolbox.RhythmMultiplier.Value = 1;
                toolbox.SpacingMultiplier.Value = 1;
                overlay = Editor.ChildrenOfType<AimFlowOverlay>().Single();
            });
            AddStep("preview slider over selected circle", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.PlacementObject.Value = null!;
                EditorBeatmap.Clear();
                EditorBeatmap.AddRange(new[]
                {
                    firstCircle = new HitCircle { Position = new Vector2(90, 280), StartTime = 0 },
                    secondCircle = new HitCircle { Position = new Vector2(310, 250), StartTime = 120 },
                    selectedCircle = new HitCircle { Position = new Vector2(170, 115), StartTime = 240 },
                });
                EditorBeatmap.SelectedHitObjects.Add(selectedCircle);
                slider = createSlider(new Vector2(390, 210), 360);
                slider.ApplyDefaults(EditorBeatmap.ControlPointInfo, EditorBeatmap.Difficulty);
                EditorBeatmap.PlacementObject.Value = slider;
            });
            AddUntilStep("slider placement takes precedence", () => ReferenceEquals(overlay.CurrentTarget, slider) && !overlay.CurrentTargetIsSelection);
            AddUntilStep("slider placement heatmap visible", () => overlay.HeatmapVisible);
            AddStep("calculate slider placement heatmap", () => expectedHeatmap = createExpectedHeatmap(slider.StartTime, firstCircle, secondCircle, selectedCircle));
            AddAssert("slider placement heatmap matches", () => overlay.CurrentHeatmap.Scores, () => Is.EqualTo(expectedHeatmap.Scores));
            AddAssert("slider placement has fixed paths", () => overlay.ChildrenOfType<SmoothPath>().Count() == 3);
            AddAssert("slider placement has no circle ghosts", () => !overlay.ChildrenOfType<CircularContainer>().Any());

            AddStep("clear placement", () => EditorBeatmap.PlacementObject.Value = null!);
            AddUntilStep("selected circle becomes target", () => ReferenceEquals(overlay.CurrentTarget, selectedCircle) && overlay.CurrentTargetIsSelection);
            AddStep("calculate selected circle heatmap", () => expectedHeatmap = createExpectedHeatmap(selectedCircle.StartTime, firstCircle, secondCircle));
            AddUntilStep("selected circle heatmap matches", () => overlay.CurrentHeatmap.Scores.SequenceEqual(expectedHeatmap.Scores));

            AddStep("commit slider", () => EditorBeatmap.Add(slider));
            AddUntilStep("slider defaults applied", () => double.IsFinite(slider.Duration) && slider.Duration > 0);
            AddStep("select slider", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.Add(slider);
            });
            AddUntilStep("selected slider becomes target", () => ReferenceEquals(overlay.CurrentTarget, slider) && overlay.CurrentTargetIsSelection);
            AddStep("calculate selected slider heatmap", () => expectedHeatmap = createExpectedHeatmap(slider.StartTime, firstCircle, secondCircle, selectedCircle));
            AddUntilStep("selected slider heatmap matches", () => overlay.CurrentHeatmap.Scores.SequenceEqual(expectedHeatmap.Scores));
            AddStep("hover selected slider maximum", () => InputManager.MoveMouseTo(overlay.ToScreenSpace(getMaximumCellCentre(expectedHeatmap))));
            AddUntilStep("selected slider score matches cell", () => overlay.HoveredHeatmapScore.HasValue
                                                                     && Math.Abs(overlay.HoveredHeatmapScore.Value - expectedHeatmap.MaximumScore) < 0.000001);
            AddAssert("selected slider hover adds no paths", () => overlay.ChildrenOfType<SmoothPath>().Count() == 3);
            AddAssert("selected slider hover adds no ghosts", () => !overlay.ChildrenOfType<CircularContainer>().Any());

            AddStep("remember stationary heatmap", () => stationaryHeatmap = overlay.CurrentHeatmap.Scores.ToArray());
            AddStep("move selected slider head", () => slider.Position += new Vector2(-45, 30));
            AddUntilStep("ribbon follows slider head", () =>
            {
                SmoothPath[] visiblePaths = overlay.ChildrenOfType<SmoothPath>().Where(path => path.Alpha > 0.01f).ToArray();

                return visiblePaths.Length == 2
                       && visiblePaths.All(path => path.Vertices.Count > 0
                                                   && Vector2.Distance(path.Vertices[^1], slider.StackedPosition) < 0.01f);
            });
            AddAssert("moving head keeps heatmap stationary", () => overlay.CurrentHeatmap.Scores, () => Is.EqualTo(stationaryHeatmap));

            AddStep("select slider then earlier circle", () => EditorBeatmap.SelectedHitObjects.Add(selectedCircle));
            AddUntilStep("earliest selection is anchor", () => ReferenceEquals(overlay.CurrentTarget, selectedCircle) && overlay.CurrentTargetIsSelection);
            AddStep("disable ribbon for selection", () => toolbox.Enabled.Value = false);
            AddUntilStep("selection paths hidden", () => overlay.ChildrenOfType<SmoothPath>().All(path => path.Alpha < 0.01f));
            AddAssert("selection heatmap remains", () => overlay.HeatmapVisible);

            AddStep("clear selection", () => EditorBeatmap.SelectedHitObjects.Clear());
            AddUntilStep("selection heatmap hides", () => overlay.Alpha < 0.1f && !overlay.HeatmapVisible && overlay.HoveredHeatmapScore == null);
        }

        private static Slider createSlider(Vector2 position, double startTime) => new Slider
        {
            Position = position,
            StartTime = startTime,
            Path = new SliderPath
            {
                ControlPoints =
                {
                    new PathControlPoint(Vector2.Zero, PathType.LINEAR),
                    new PathControlPoint(new Vector2(90, -30)),
                }
            }
        };

        private static AimFlowHeatmap createExpectedHeatmap(double targetTime, params HitCircle[] history)
            => AimFlowAnalysis.CreatePlacementHeatmap(
                history.Select(circle => new AimWaypoint(circle.StackedPosition, circle.StartTime)).ToArray(),
                targetTime,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE);

        private static Vector2 getMaximumCellCentre(AimFlowHeatmap heatmap)
        {
            int index = heatmap.Scores.ToList().IndexOf(heatmap.MaximumScore);
            int column = index % heatmap.Columns;
            int row = index / heatmap.Columns;
            return new Vector2(
                (column + 0.5f) * OsuPlayfield.BASE_SIZE.X / heatmap.Columns,
                (row + 0.5f) * OsuPlayfield.BASE_SIZE.Y / heatmap.Rows);
        }
    }
}
