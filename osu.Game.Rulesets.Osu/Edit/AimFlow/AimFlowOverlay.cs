// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Osu.Edit.AimFlow
{
    public partial class AimFlowOverlay : CompositeDrawable
    {
        private const int history_waypoint_count = 7;

        private readonly AimFlowToolboxGroup settings;
        private readonly PlacementHeatmap heatmap;
        private readonly SmoothPath armPath;
        private readonly SmoothPath glowPath;
        private readonly SmoothPath cursorPath;
        private readonly OsuSpriteText hoverScoreText;
        private readonly OsuSpriteText statusText;

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        private AimFlowHeatmap currentHeatmap = AimFlowHeatmap.Empty;
        private Vector2? hoveredHeatmapPosition;
        private double? hoveredHeatmapScore;
        private double placementDuration;
        private int trajectoryStateHash;
        private int heatmapStateHash;
        private int hoveredHeatmapCell = -1;
        private bool hasTrajectoryState;
        private bool hasHeatmapState;

        public Vector2? HoveredHeatmapPosition => hoveredHeatmapPosition;

        public double? HoveredHeatmapScore => hoveredHeatmapScore;

        public bool HeatmapVisible => heatmap.Alpha > 0.9f;

        public bool HeatmapScoreVisible => hoverScoreText.Alpha > 0.9f;

        internal OsuHitObject? CurrentTarget { get; private set; }

        internal bool CurrentTargetIsSelection { get; private set; }

        internal AimFlowHeatmap CurrentHeatmap => currentHeatmap;

        public AimFlowOverlay(AimFlowToolboxGroup settings)
        {
            this.settings = settings;

            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;
            Alpha = 0;

            InternalChildren = new Drawable[]
            {
                heatmap = new PlacementHeatmap(),
                armPath = createPath(1.2f),
                glowPath = createPath(8),
                cursorPath = createPath(2.8f),
                hoverScoreText = new OsuSpriteText
                {
                    Origin = Anchor.BottomCentre,
                    Font = OsuFont.Default.With(size: 12, weight: FontWeight.Bold),
                    Colour = Color4.White,
                    Shadow = true,
                    Alpha = 0,
                },
                statusText = new OsuSpriteText
                {
                    Position = new Vector2(8),
                    Font = OsuFont.Default.With(size: 11, weight: FontWeight.SemiBold),
                    Shadow = true,
                },
            };
        }

        private static SmoothPath createPath(float radius) => new SmoothPath
        {
            AutoSizeAxes = Axes.None,
            RelativeSizeAxes = Axes.Both,
            PathRadius = radius,
        };

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            armPath.Colour = colours.Cyan;
            glowPath.Colour = colours.Pink1;
            cursorPath.Colour = colours.Pink0;
            statusText.Colour = colours.Pink0;
            heatmap.SetPalette(colours.BlueDark, colours.Purple0, colours.Pink0, colours.Yellow);
        }

        protected override void Update()
        {
            base.Update();

            bool showPreview = settings.Enabled.Value;
            bool showHeatmap = settings.HeatmapEnabled.Value;

            if ((!showPreview && !showHeatmap) || !tryGetTarget(out OsuHitObject target, out bool targetIsSelection))
            {
                hideEverything();
                return;
            }

            CurrentTarget = target;
            CurrentTargetIsSelection = targetIsSelection;
            Vector2 targetPosition = target.StackedPosition;
            List<AimWaypoint> history = collectHistory(target.StartTime);

            if (history.Count < 2 || target.StartTime <= history[^1].Time + 0.01)
            {
                hideEverything();
                return;
            }

            int newAnalysisHash = calculateAnalysisStateHash(history, target.StartTime);
            placementDuration = Math.Max(1, target.StartTime - history[^1].Time);

            if (showHeatmap)
            {
                if (!hasHeatmapState || heatmapStateHash != newAnalysisHash)
                {
                    currentHeatmap = AimFlowAnalysis.CreatePlacementHeatmap(
                        history,
                        target.StartTime,
                        settings.Model.Value,
                        OsuPlayfield.BASE_SIZE,
                        spacingMultiplier: settings.SpacingMultiplier.Value,
                        rhythmMultiplier: settings.RhythmMultiplier.Value);
                    heatmap.ShowHeatmap(currentHeatmap);
                    heatmapStateHash = newAnalysisHash;
                    hasHeatmapState = true;
                }

                heatmap.FadeIn(150);
                updateHoveredHeatmap();
            }
            else
            {
                heatmap.FadeOut(100);
                hasHeatmapState = false;
                currentHeatmap = AimFlowHeatmap.Empty;
                clearHeatmapHover();
            }

            if (showPreview)
            {
                int newTrajectoryHash = calculateTrajectoryStateHash(newAnalysisHash, targetPosition);

                if (!hasTrajectoryState || trajectoryStateHash != newTrajectoryHash)
                {
                    var trajectoryWaypoints = new List<AimWaypoint>(history)
                    {
                        new AimWaypoint(targetPosition, target.StartTime),
                    };
                    TrajectoryResult trajectory = AimFlowAnalysis.GenerateTrajectory(
                        trajectoryWaypoints,
                        settings.Model.Value,
                        settings.OneEuroMinCutoff.Value,
                        settings.OneEuroBeta.Value);
                    setVertices(cursorPath, trajectory.Points);
                    setVertices(glowPath, trajectory.Points);
                    setVertices(armPath, trajectory.ArmPoints);
                    trajectoryStateHash = newTrajectoryHash;
                    hasTrajectoryState = true;
                }

                armPath.Alpha = settings.Model.Value == AimFlowModel.ArmWrist ? 0.5f : 0;
                glowPath.Alpha = 0.2f;
                cursorPath.Alpha = 0.9f;
            }
            else
                hideLivePreview();

            updateStatus(showHeatmap, target, targetIsSelection);
            this.FadeIn(100);
        }

        private bool tryGetTarget(out OsuHitObject target, out bool targetIsSelection)
        {
            HitObject? placementObject = editorBeatmap.PlacementObject.Value;

            // Never fall back to selection while an unsupported placement (such as a spinner) is active.
            if (placementObject != null)
            {
                if (placementObject is HitCircle or Slider)
                {
                    target = (OsuHitObject)placementObject;
                    targetIsSelection = false;
                    return true;
                }

                target = null!;
                targetIsSelection = false;
                return false;
            }

            OsuHitObject? earliestSelectedTarget = null;

            foreach (HitObject selectedObject in editorBeatmap.SelectedHitObjects)
            {
                if (selectedObject is not HitCircle && selectedObject is not Slider)
                    continue;

                var candidate = (OsuHitObject)selectedObject;

                if (earliestSelectedTarget == null || candidate.StartTime < earliestSelectedTarget.StartTime)
                    earliestSelectedTarget = candidate;
            }

            target = earliestSelectedTarget!;
            targetIsSelection = earliestSelectedTarget != null;
            return earliestSelectedTarget != null;
        }

        private void updateHoveredHeatmap()
        {
            int newHoveredCell = -1;
            Vector2 newHoveredPosition = default;
            double newHoveredScore = 0;
            var inputManager = GetContainingInputManager();

            if (inputManager != null && currentHeatmap.Columns > 0 && currentHeatmap.Rows > 0)
            {
                Vector2 localMousePosition = ToLocalSpace(inputManager.CurrentState.Mouse.Position);

                if (localMousePosition.X >= 0 && localMousePosition.Y >= 0
                    && localMousePosition.X < OsuPlayfield.BASE_SIZE.X && localMousePosition.Y < OsuPlayfield.BASE_SIZE.Y)
                {
                    int column = (int)(localMousePosition.X * currentHeatmap.Columns / OsuPlayfield.BASE_SIZE.X);
                    int row = (int)(localMousePosition.Y * currentHeatmap.Rows / OsuPlayfield.BASE_SIZE.Y);
                    double score = currentHeatmap.GetScore(column, row);

                    if (score > 0)
                    {
                        newHoveredCell = row * currentHeatmap.Columns + column;
                        newHoveredPosition = new Vector2(
                            (column + 0.5f) * OsuPlayfield.BASE_SIZE.X / currentHeatmap.Columns,
                            (row + 0.5f) * OsuPlayfield.BASE_SIZE.Y / currentHeatmap.Rows);
                        newHoveredScore = score;
                    }
                }
            }

            if (newHoveredCell < 0)
            {
                if (hoveredHeatmapCell >= 0 || hoveredHeatmapScore.HasValue)
                    clearHeatmapHover();

                return;
            }

            if (newHoveredCell == hoveredHeatmapCell && hoveredHeatmapScore == newHoveredScore)
                return;

            hoveredHeatmapCell = newHoveredCell;
            hoveredHeatmapPosition = newHoveredPosition;
            hoveredHeatmapScore = newHoveredScore;
            hoverScoreText.Position = newHoveredPosition + new Vector2(0, -10);
            hoverScoreText.Text = newHoveredScore.ToString("0.0");
            hoverScoreText.FadeIn(80);
        }

        private void updateStatus(bool showHeatmap, OsuHitObject target, bool targetIsSelection)
        {
            string modelName = settings.Model.Value switch
            {
                AimFlowModel.OneEuro => "1€ tangent guide",
                AimFlowModel.ArmWrist => "arm + wrist",
                _ => "minimum jerk",
            };
            string multipliers = $"rhythm {settings.RhythmMultiplier.Value:0.##}× • spacing {settings.SpacingMultiplier.Value:0.##}×";
            string ribbonSuffix = settings.Enabled.Value ? " • main ribbon" : string.Empty;
            string targetName = target is Slider ? "slider head" : "circle";
            string targetContext = targetIsSelection ? $"selected {targetName}" : $"placing {targetName}";

            if (hoveredHeatmapScore.HasValue)
                statusText.Text = $"aim flow • {targetContext} • cell score {hoveredHeatmapScore.Value:0.0} • {multipliers}{ribbonSuffix}";
            else if (showHeatmap)
                statusText.Text = $"aim flow • {modelName} • {targetContext} • incoming {placementDuration:0} ms • hover heatmap for score • {multipliers}{ribbonSuffix}";
            else
                statusText.Text = $"aim flow • {modelName} • {targetContext} • incoming {placementDuration:0} ms • {multipliers}{ribbonSuffix}";

            statusText.FadeIn(100);
        }

        private List<AimWaypoint> collectHistory(double targetTime)
        {
            var result = new List<AimWaypoint>();

            // Hit objects are start-time sorted. Locate the insertion point first so placement near the
            // beginning of a large map does not require walking backwards over every later object.
            int lower = 0;
            int upper = editorBeatmap.HitObjects.Count;

            while (lower < upper)
            {
                int middle = (lower + upper) / 2;

                if (editorBeatmap.HitObjects[middle].StartTime < targetTime)
                    lower = middle + 1;
                else
                    upper = middle;
            }

            for (int objectIndex = lower - 1; objectIndex >= 0 && result.Count < history_waypoint_count; objectIndex--)
            {
                if (editorBeatmap.HitObjects[objectIndex] is not OsuHitObject hitObject)
                    continue;

                if (hitObject is Spinner)
                {
                    break;
                }

                if (hitObject.GetEndTime() > targetTime + 0.01)
                    continue;

                if (hitObject is Slider slider)
                {
                    int samplesPerSpan = Math.Clamp((int)Math.Ceiling(slider.SpanDuration / 80), 2, 4);
                    int sampleCount = slider.SpanCount() * samplesPerSpan;

                    // Walk samples backwards too, preserving every repeat endpoint while stopping as soon as
                    // the seven-waypoint history budget is filled.
                    for (int sample = sampleCount; sample >= 0 && result.Count < history_waypoint_count; sample--)
                    {
                        double progress = sample / (double)sampleCount;
                        double sampleTime = slider.StartTime + slider.Duration * progress;

                        if (Math.Abs(sampleTime - targetTime) <= 0.01)
                        {
                            // A slider tail at this exact timestamp is the cursor's zero-time anchor for the
                            // current target. Keep it one millisecond earlier so trajectory interpolation can
                            // represent the constraint without collapsing two same-time waypoints.
                            result.Add(new AimWaypoint(slider.StackedPositionAt(progress), targetTime - 1));
                            continue;
                        }

                        if (sampleTime > targetTime)
                            continue;

                        result.Add(new AimWaypoint(
                            slider.StackedPositionAt(progress),
                            sampleTime));
                    }
                }
                else
                    result.Add(new AimWaypoint(hitObject.StackedPosition, hitObject.StartTime));
            }

            result.Reverse();
            return result;
        }

        private int calculateAnalysisStateHash(IReadOnlyList<AimWaypoint> history, double placementTime)
        {
            var hash = new HashCode();
            hash.Add(settings.Model.Value);
            hash.Add(settings.OneEuroMinCutoff.Value);
            hash.Add(settings.OneEuroBeta.Value);
            hash.Add(settings.RhythmMultiplier.Value);
            hash.Add(settings.SpacingMultiplier.Value);
            hash.Add(placementTime);

            foreach (AimWaypoint waypoint in history)
            {
                hash.Add(waypoint.Position.X);
                hash.Add(waypoint.Position.Y);
                hash.Add(waypoint.Time);
            }

            return hash.ToHashCode();
        }

        private static int calculateTrajectoryStateHash(int analysisHash, Vector2 placementPosition)
            => HashCode.Combine(analysisHash, placementPosition.X, placementPosition.Y);

        private void hideEverything()
        {
            CurrentTarget = null;
            CurrentTargetIsSelection = false;
            hideLivePreview();
            clearHeatmapHover();
            heatmap.FadeOut(100);
            statusText.FadeOut(100);
            hasHeatmapState = false;
            currentHeatmap = AimFlowHeatmap.Empty;
            this.FadeOut(100);
        }

        private void hideLivePreview()
        {
            hasTrajectoryState = false;
            cursorPath.FadeOut(100);
            glowPath.FadeOut(100);
            armPath.FadeOut(100);
        }

        private void clearHeatmapHover()
        {
            hoveredHeatmapCell = -1;
            hoveredHeatmapPosition = null;
            hoveredHeatmapScore = null;
            hoverScoreText.FadeOut(80);
        }

        private static void setVertices(SmoothPath path, IReadOnlyList<Vector2> vertices)
        {
            path.ClearVertices();

            foreach (Vector2 vertex in vertices)
                path.AddVertex(vertex);
        }

        private partial class PlacementHeatmap : BufferedContainer
        {
            private const int columns = 32;
            private const int rows = 24;

            private readonly Box[] cells;
            private Color4 coldColour;
            private Color4 middleColour;
            private Color4 hotColour;
            private Color4 peakColour;

            public PlacementHeatmap()
                : base(cachedFrameBuffer: true)
            {
                RelativeSizeAxes = Axes.Both;
                Alpha = 0;
                cells = new Box[columns * rows];
                var children = new Drawable[cells.Length];
                var cellSize = new Vector2(OsuPlayfield.BASE_SIZE.X / columns, OsuPlayfield.BASE_SIZE.Y / rows);

                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int index = row * columns + column;
                        children[index] = cells[index] = new Box
                        {
                            Position = new Vector2(column * cellSize.X, row * cellSize.Y),
                            Size = cellSize + Vector2.One,
                            Alpha = 0,
                        };
                    }
                }

                Children = children;
            }

            public void SetPalette(Color4 cold, Color4 middle, Color4 hot, Color4 peak)
            {
                coldColour = cold;
                middleColour = middle;
                hotColour = hot;
                peakColour = peak;
            }

            public void ShowHeatmap(AimFlowHeatmap analysis)
            {
                if (analysis.Columns != columns || analysis.Rows != rows || analysis.MaximumScore <= 0)
                {
                    foreach (Box cell in cells)
                        cell.Alpha = 0;

                    ForceRedraw();
                    return;
                }

                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int index = row * columns + column;
                        double score = analysis.GetScore(column, row);

                        if (score <= 0)
                        {
                            cells[index].Alpha = 0;
                            continue;
                        }

                        float normalised = (float)Math.Clamp(score / analysis.MaximumScore, 0, 1);
                        Color4 colour = normalised < 0.55f
                            ? Interpolation.ValueAt(normalised, coldColour, middleColour, 0, 0.55f)
                            : normalised < 0.86f
                                ? Interpolation.ValueAt(normalised, middleColour, hotColour, 0.55f, 0.86f)
                                : Interpolation.ValueAt(normalised, hotColour, peakColour, 0.86f, 1);
                        cells[index].Colour = colour;
                        cells[index].Alpha = 0.025f + 0.25f * MathF.Pow(normalised, 2.4f);
                    }
                }

                ForceRedraw();
            }
        }

    }
}
