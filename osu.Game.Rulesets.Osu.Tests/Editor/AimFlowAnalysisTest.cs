// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Osu.Edit.AimFlow;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Editor
{
    [TestFixture]
    public class AimFlowAnalysisTest
    {
        private static readonly AimWaypoint[] example_pattern =
        {
            new AimWaypoint(new Vector2(110, 285), 0),
            new AimWaypoint(new Vector2(350, 265), 115),
            new AimWaypoint(new Vector2(135, 150), 230),
            new AimWaypoint(new Vector2(365, 115), 345),
        };

        [TestCase(AimFlowModel.MinimumJerk)]
        [TestCase(AimFlowModel.OneEuro)]
        [TestCase(AimFlowModel.ArmWrist)]
        public void TestTrajectoryPassesExactlyThroughTargets(AimFlowModel model)
        {
            TrajectoryResult result = AimFlowAnalysis.GenerateTrajectory(example_pattern, model);

            Assert.That(result.Points.Count, Is.GreaterThan(example_pattern.Length));

            foreach (AimWaypoint waypoint in example_pattern)
                Assert.That(result.Points.Any(point => Vector2.Distance(point, waypoint.Position) < 0.01f), Is.True);
        }

        [Test]
        public void TestArmWristProvidesArmComponent()
        {
            TrajectoryResult result = AimFlowAnalysis.GenerateTrajectory(example_pattern, AimFlowModel.ArmWrist);

            Assert.That(result.ArmPoints, Has.Count.EqualTo(result.Points.Count));
            Assert.That(result.ArmPoints.Zip(result.Points, Vector2.Distance).Any(distance => distance > 1), Is.True);
        }

        [TestCase(AimFlowModel.MinimumJerk)]
        [TestCase(AimFlowModel.OneEuro)]
        [TestCase(AimFlowModel.ArmWrist)]
        public void TestCurrentPositionsIgnoreLivePlacementPosition(AimFlowModel model)
        {
            AimWaypoint[] history = example_pattern.Take(3).ToArray();
            var firstIntent = new AimWaypoint(new Vector2(40, 40), 345);
            var secondIntent = new AimWaypoint(new Vector2(470, 340), 345);

            AimFlowPlacementResult first = AimFlowAnalysis.AnalysePlacement(history, firstIntent, model, 1, 0.004f, OsuPlayfield.BASE_SIZE);
            AimFlowPlacementResult second = AimFlowAnalysis.AnalysePlacement(history, secondIntent, model, 1, 0.004f, OsuPlayfield.BASE_SIZE);

            Assert.That(first.CurrentPositions, Has.Count.EqualTo(5));
            Assert.That(second.CurrentPositions, Has.Count.EqualTo(5));

            for (int i = 0; i < first.CurrentPositions.Count; i++)
            {
                Assert.That(Vector2.Distance(first.CurrentPositions[i].Position, second.CurrentPositions[i].Position), Is.LessThan(0.001f));
                Assert.That(first.CurrentPositions[i].Time, Is.EqualTo(345));
            }

            Assert.That(Vector2.Distance(first.Trajectory[^1], firstIntent.Position), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(second.Trajectory[^1], secondIntent.Position), Is.LessThan(0.01f));
        }

        [Test]
        public void TestCurrentPositionsIgnoreObjectsAtOrAfterPlacementTime()
        {
            AimWaypoint[] historyWithFuture = example_pattern.Concat(new[]
            {
                new AimWaypoint(new Vector2(40, 40), 500),
            }).ToArray();
            var placement = new AimWaypoint(new Vector2(365, 115), 345);

            AimFlowPlacementResult expected = AimFlowAnalysis.AnalysePlacement(example_pattern.Take(3).ToArray(), placement, AimFlowModel.MinimumJerk, 1, 0.004f, OsuPlayfield.BASE_SIZE);
            AimFlowPlacementResult actual = AimFlowAnalysis.AnalysePlacement(historyWithFuture, placement, AimFlowModel.MinimumJerk, 1, 0.004f, OsuPlayfield.BASE_SIZE);

            Assert.That(actual.CurrentPositions.Select(s => s.Position), Is.EqualTo(expected.CurrentPositions.Select(s => s.Position)));
        }

        [TestCase(20)]
        [TestCase(1500)]
        public void TestCurrentPositionsUseExactPlacementInterval(double interval)
        {
            AimWaypoint[] history = example_pattern.Take(3).ToArray();
            double placementTime = history[^1].Time + interval;
            AimFlowPlacementResult result = AimFlowAnalysis.AnalysePlacement(
                history,
                new AimWaypoint(new Vector2(365, 115), placementTime),
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE);

            Assert.That(result.PlacementDuration, Is.EqualTo(interval));
            Assert.That(result.CurrentPositions, Has.Count.EqualTo(5));
            Assert.That(result.CurrentPositions.All(position => position.Time == placementTime && double.IsFinite(position.Score)), Is.True);
        }

        [Test]
        public void TestSameTimeSliderTailAnchorIsTheOnlyCurrentPosition()
        {
            var tail = new Vector2(256, 192);
            var history = new[]
            {
                new AimWaypoint(new Vector2(100, 100), 0),
                new AimWaypoint(new Vector2(200, 100), 200),
                new AimWaypoint(tail, 344),
            };
            var placement = new AimWaypoint(new Vector2(420, 300), 345);

            AimFlowPlacementResult result = AimFlowAnalysis.AnalysePlacement(
                history, placement, AimFlowModel.MinimumJerk, 1, 0.004f, OsuPlayfield.BASE_SIZE);
            AimFlowHeatmap heatmap = AimFlowAnalysis.CreatePlacementHeatmap(
                history, placement.Time, AimFlowModel.MinimumJerk, OsuPlayfield.BASE_SIZE);

            Assert.That(result.PlacementDuration, Is.EqualTo(1));
            Assert.That(result.CurrentPositions, Has.Count.EqualTo(1));
            Assert.That(Vector2.Distance(result.CurrentPositions[0].Position, tail), Is.LessThan(0.001f));
            Assert.That(result.CurrentPositions[0].Time, Is.EqualTo(placement.Time));
            Assert.That(Vector2.Distance(result.Trajectory[^1], placement.Position), Is.LessThan(0.01f));
            Assert.That(heatmap.GetScore(15, 11), Is.GreaterThan(heatmap.GetScore(4, 4)));
        }

        [TestCase(AimFlowModel.MinimumJerk)]
        [TestCase(AimFlowModel.OneEuro)]
        [TestCase(AimFlowModel.ArmWrist)]
        public void TestHoveredCurrentPositionProducesMultiCirclePatterns(AimFlowModel model)
        {
            AimWaypoint[] history = example_pattern.Take(3).ToArray();
            AimFlowPlacementResult placement = AimFlowAnalysis.AnalysePlacement(
                history,
                example_pattern[^1],
                model,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE);
            AimFlowSuggestion current = placement.CurrentPositions[0];
            IReadOnlyList<AimFlowPattern> patterns = AimFlowAnalysis.CreateContinuationPatterns(
                history,
                current,
                model,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE,
                3,
                3);

            Assert.That(patterns, Has.Count.EqualTo(3));

            foreach (AimFlowPattern pattern in patterns)
            {
                Assert.That(pattern.FutureCircles, Has.Count.EqualTo(3));
                Assert.That(Vector2.Distance(pattern.Trajectory[0], current.Position), Is.LessThan(0.01f));
                Assert.That(double.IsFinite(pattern.Score), Is.True);

                double previousTime = current.Time;

                foreach (AimFlowSuggestion future in pattern.FutureCircles)
                {
                    Assert.That(future.Time, Is.GreaterThan(previousTime));
                    Assert.That(future.Position.X, Is.InRange(24, OsuPlayfield.BASE_SIZE.X - 24));
                    Assert.That(future.Position.Y, Is.InRange(24, OsuPlayfield.BASE_SIZE.Y - 24));
                    Assert.That(pattern.Trajectory.Any(point => Vector2.Distance(point, future.Position) < 0.01f), Is.True);
                    previousTime = future.Time;
                }
            }

            Assert.That(Vector2.Distance(patterns[0].FutureCircles[0].Position, patterns[1].FutureCircles[0].Position), Is.GreaterThanOrEqualTo(40));
        }

        [Test]
        public void TestContinuationPatternsAreDeterministic()
        {
            AimWaypoint[] history = example_pattern.Take(3).ToArray();
            AimFlowPlacementResult placement = AimFlowAnalysis.AnalysePlacement(
                history,
                example_pattern[^1],
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE);

            IReadOnlyList<AimFlowPattern> first = AimFlowAnalysis.CreateContinuationPatterns(
                history, placement.CurrentPositions[0], AimFlowModel.MinimumJerk, 1, 0.004f, OsuPlayfield.BASE_SIZE);
            IReadOnlyList<AimFlowPattern> second = AimFlowAnalysis.CreateContinuationPatterns(
                history, placement.CurrentPositions[0], AimFlowModel.MinimumJerk, 1, 0.004f, OsuPlayfield.BASE_SIZE);

            Assert.That(second.SelectMany(pattern => pattern.FutureCircles).Select(circle => circle.Position),
                Is.EqualTo(first.SelectMany(pattern => pattern.FutureCircles).Select(circle => circle.Position)));
            Assert.That(second.Select(pattern => pattern.Score), Is.EqualTo(first.Select(pattern => pattern.Score)));
        }

        [Test]
        public void TestRhythmMultiplierHalvesEveryFutureIntervalWithoutCompounding()
        {
            AimWaypoint[] history = example_pattern.Take(3).ToArray();
            AimFlowPlacementResult placement = AimFlowAnalysis.AnalysePlacement(
                history,
                example_pattern[^1],
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE);

            IReadOnlyList<AimFlowPattern> patterns = AimFlowAnalysis.CreateContinuationPatterns(
                history,
                placement.CurrentPositions[0],
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE,
                patternCount: 3,
                depth: 3,
                rhythmMultiplier: 2);

            Assert.That(patterns, Has.Count.EqualTo(3));

            foreach (AimFlowPattern pattern in patterns)
            {
                double previousTime = placement.CurrentPositions[0].Time;

                foreach (AimFlowSuggestion future in pattern.FutureCircles)
                {
                    Assert.That(future.Time - previousTime, Is.EqualTo(57.5).Within(0.001));
                    previousTime = future.Time;
                }
            }
        }

        [Test]
        public void TestSpacingMultiplierDoesNotCompoundAcrossFutureDepth()
        {
            var history = new[]
            {
                new AimWaypoint(new Vector2(156, 192), 0),
                new AimWaypoint(new Vector2(206, 192), 100),
                new AimWaypoint(new Vector2(256, 192), 200),
            };
            var current = new AimFlowSuggestion(new Vector2(306, 192), 300, 100, 0);
            float[] allowedDistances = { 35, 50.4f, 64.4f, 75.6f };

            IReadOnlyList<AimFlowPattern> patterns = AimFlowAnalysis.CreateContinuationPatterns(
                history,
                current,
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE,
                patternCount: 3,
                depth: 3,
                spacingMultiplier: 1.4f);

            Assert.That(patterns, Has.Count.EqualTo(3));

            foreach (AimFlowPattern pattern in patterns)
            {
                Vector2 previousPosition = current.Position;

                foreach (AimFlowSuggestion future in pattern.FutureCircles)
                {
                    float distance = Vector2.Distance(previousPosition, future.Position);
                    Assert.That(allowedDistances.Any(allowed => Math.Abs(distance - allowed) < 0.01f), Is.True,
                        $"Unexpected compounded continuation distance {distance:0.###}");
                    previousPosition = future.Position;
                }
            }
        }

        [Test]
        public void TestSpacingMultiplierChangesPlacementRadiusAndHeatmapHotspot()
        {
            var history = new[]
            {
                new AimWaypoint(new Vector2(156, 192), 0),
                new AimWaypoint(new Vector2(256, 192), 100),
            };
            var placement = new AimWaypoint(new Vector2(256, 192), 200);

            AimFlowPlacementResult tight = AimFlowAnalysis.AnalysePlacement(
                history,
                placement,
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE,
                suggestionCount: 1,
                spacingMultiplier: 0.6f);
            AimFlowPlacementResult wide = AimFlowAnalysis.AnalysePlacement(
                history,
                placement,
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE,
                suggestionCount: 1,
                spacingMultiplier: 1.4f);
            AimFlowHeatmap tightHeatmap = AimFlowAnalysis.CreatePlacementHeatmap(
                history,
                placement.Time,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE,
                spacingMultiplier: 0.6f);
            AimFlowHeatmap wideHeatmap = AimFlowAnalysis.CreatePlacementHeatmap(
                history,
                placement.Time,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE,
                spacingMultiplier: 1.4f);

            float tightRadius = Vector2.Distance(history[^1].Position, tight.CurrentPositions[0].Position);
            float wideRadius = Vector2.Distance(history[^1].Position, wide.CurrentPositions[0].Position);
            float tightHotspotRadius = Vector2.Distance(history[^1].Position, getMaximumCellCentre(tightHeatmap));
            float wideHotspotRadius = Vector2.Distance(history[^1].Position, getMaximumCellCentre(wideHeatmap));

            Assert.That(wideRadius, Is.GreaterThan(tightRadius + 30));
            Assert.That(wideHotspotRadius, Is.GreaterThan(tightHotspotRadius + 30));
            Assert.That(tight.CurrentPositions[0].Time, Is.EqualTo(placement.Time));
            Assert.That(wide.CurrentPositions[0].Time, Is.EqualTo(placement.Time));
            Assert.That(wideHeatmap.Scores, Is.Not.EqualTo(tightHeatmap.Scores));
        }

        [Test]
        public void TestRhythmMultiplierScalesHeatmapRadiusAndSpacingCanCompensate()
        {
            var history = new[]
            {
                new AimWaypoint(new Vector2(156, 192), 0),
                new AimWaypoint(new Vector2(256, 192), 100),
            };
            const double placementTime = 200;

            AimFlowHeatmap neutral = AimFlowAnalysis.CreatePlacementHeatmap(
                history,
                placementTime,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE);
            AimFlowHeatmap doubleRhythm = AimFlowAnalysis.CreatePlacementHeatmap(
                history,
                placementTime,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE,
                rhythmMultiplier: 2);
            AimFlowHeatmap compensated = AimFlowAnalysis.CreatePlacementHeatmap(
                history,
                placementTime,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE,
                spacingMultiplier: 2,
                rhythmMultiplier: 2);
            AimFlowHeatmap invalid = AimFlowAnalysis.CreatePlacementHeatmap(
                history,
                placementTime,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE,
                spacingMultiplier: float.PositiveInfinity,
                rhythmMultiplier: float.NaN);

            float neutralRadius = Vector2.Distance(history[^1].Position, getMaximumCellCentre(neutral));
            float doubleRhythmRadius = Vector2.Distance(history[^1].Position, getMaximumCellCentre(doubleRhythm));

            Assert.That(doubleRhythmRadius, Is.LessThan(neutralRadius - 30));
            Assert.That(compensated.Scores, Is.EqualTo(neutral.Scores));
            Assert.That(invalid.Scores, Is.EqualTo(neutral.Scores));
        }

        [Test]
        public void TestExactTailHeatmapAndFastContinuationRemainAvailableAtPlayfieldEdge()
        {
            var tail = new Vector2(4, 4);
            var history = new[]
            {
                new AimWaypoint(new Vector2(180, 180), 0),
                new AimWaypoint(new Vector2(80, 80), 200),
                new AimWaypoint(tail, 344),
            };
            var current = new AimFlowSuggestion(tail, 345, 100, 0);

            AimFlowHeatmap heatmap = AimFlowAnalysis.CreatePlacementHeatmap(
                history,
                current.Time,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE,
                spacingMultiplier: 2,
                rhythmMultiplier: 4);
            AimFlowHeatmap neutralHeatmap = AimFlowAnalysis.CreatePlacementHeatmap(
                history,
                current.Time,
                AimFlowModel.MinimumJerk,
                OsuPlayfield.BASE_SIZE);
            IReadOnlyList<AimFlowPattern> patterns = AimFlowAnalysis.CreateContinuationPatterns(
                history,
                current,
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE,
                rhythmMultiplier: 4,
                spacingMultiplier: 2);

            Assert.That(heatmap.GetScore(0, 0), Is.GreaterThan(0));
            Assert.That(heatmap.Scores, Is.EqualTo(neutralHeatmap.Scores));
            Assert.That(patterns, Has.Count.EqualTo(3));
            Assert.That(patterns.All(pattern => pattern.FutureCircles.Count == 3), Is.True);
        }

        [Test]
        public void TestNeutralAndInvalidMultipliersPreserveDefaults()
        {
            AimFlowAnalysisResult defaults = AimFlowAnalysis.Analyse(
                example_pattern,
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE);
            AimFlowAnalysisResult explicitNeutral = AimFlowAnalysis.Analyse(
                example_pattern,
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE,
                rhythmMultiplier: 1,
                spacingMultiplier: 1);
            AimFlowAnalysisResult invalid = AimFlowAnalysis.Analyse(
                example_pattern,
                AimFlowModel.MinimumJerk,
                1,
                0.004f,
                OsuPlayfield.BASE_SIZE,
                rhythmMultiplier: float.NaN,
                spacingMultiplier: float.PositiveInfinity);

            Assert.That(explicitNeutral.NextDuration, Is.EqualTo(defaults.NextDuration));
            Assert.That(explicitNeutral.Suggestions, Is.EqualTo(defaults.Suggestions));
            Assert.That(invalid.NextDuration, Is.EqualTo(defaults.NextDuration));
            Assert.That(invalid.Suggestions, Is.EqualTo(defaults.Suggestions));
        }

        [Test]
        public void TestCurrentPositionsAndPatternsDoNotStarveNearEdges()
        {
            var history = new[]
            {
                new AimWaypoint(new Vector2(24, 24), 0),
                new AimWaypoint(new Vector2(488, 360), 600),
                new AimWaypoint(new Vector2(24, 360), 655),
            };
            var intent = new AimWaypoint(new Vector2(256, 192), 1255);
            AimFlowPlacementResult placement = AimFlowAnalysis.AnalysePlacement(
                history, intent, AimFlowModel.ArmWrist, 1, 0.004f, OsuPlayfield.BASE_SIZE);
            IReadOnlyList<AimFlowPattern> patterns = AimFlowAnalysis.CreateContinuationPatterns(
                history, placement.CurrentPositions[0], AimFlowModel.ArmWrist, 1, 0.004f, OsuPlayfield.BASE_SIZE);

            Assert.That(placement.CurrentPositions, Has.Count.EqualTo(5));
            Assert.That(patterns, Has.Count.EqualTo(3));
            Assert.That(patterns.All(pattern => pattern.FutureCircles.Count == 3), Is.True);
            Assert.That(patterns.SelectMany(pattern => pattern.FutureCircles).All(circle =>
                circle.Position.X >= 24 && circle.Position.X <= OsuPlayfield.BASE_SIZE.X - 24
                && circle.Position.Y >= 24 && circle.Position.Y <= OsuPlayfield.BASE_SIZE.Y - 24), Is.True);
        }

        [Test]
        public void TestContinuationDoesNotStarveAtCentreAfterFullFieldJump()
        {
            var history = new[]
            {
                new AimWaypoint(new Vector2(24, 192), 0),
                new AimWaypoint(new Vector2(488, 192), 55),
            };
            var current = new AimFlowSuggestion(new Vector2(256, 192), 110, 50, 27.5);

            IReadOnlyList<AimFlowPattern> patterns = AimFlowAnalysis.CreateContinuationPatterns(
                history, current, AimFlowModel.MinimumJerk, 1, 0.004f, OsuPlayfield.BASE_SIZE);

            Assert.That(patterns, Has.Count.EqualTo(3));
            Assert.That(patterns.All(pattern => pattern.FutureCircles.Count == 3), Is.True);
        }

        [TestCase(AimFlowModel.MinimumJerk)]
        [TestCase(AimFlowModel.OneEuro)]
        [TestCase(AimFlowModel.ArmWrist)]
        public void TestPlacementHeatmapIsFinite(AimFlowModel model)
        {
            AimFlowHeatmap heatmap = AimFlowAnalysis.CreatePlacementHeatmap(
                example_pattern.Take(3).ToArray(),
                example_pattern[^1].Time,
                model,
                OsuPlayfield.BASE_SIZE);

            Assert.That(heatmap.Columns, Is.EqualTo(32));
            Assert.That(heatmap.Rows, Is.EqualTo(24));
            Assert.That(heatmap.Scores, Has.Count.EqualTo(32 * 24));
            Assert.That(heatmap.MaximumScore, Is.GreaterThan(0));
            Assert.That(heatmap.Scores.All(score => double.IsFinite(score) && score >= 0), Is.True);
        }

        [Test]
        public void TestPlacementHeatmapIsDeterministicAndKeepsMarginsClear()
        {
            AimWaypoint[] history = example_pattern.Take(3).ToArray();
            AimFlowHeatmap first = AimFlowAnalysis.CreatePlacementHeatmap(
                history, example_pattern[^1].Time, AimFlowModel.MinimumJerk, OsuPlayfield.BASE_SIZE);
            AimFlowHeatmap second = AimFlowAnalysis.CreatePlacementHeatmap(
                history, example_pattern[^1].Time, AimFlowModel.MinimumJerk, OsuPlayfield.BASE_SIZE);

            Assert.That(second.Scores, Is.EqualTo(first.Scores));
            Assert.That(first.MaximumScore, Is.EqualTo(first.Scores.Max()));
            Assert.That(first.GetScore(0, 0), Is.Zero);
            Assert.That(first.GetScore(first.Columns - 1, first.Rows - 1), Is.Zero);
        }

        [Test]
        public void TestSuggestionsUseSuppliedMapStatistics()
        {
            AimFlowAnalysisResult result = AimFlowAnalysis.Analyse(example_pattern, AimFlowModel.MinimumJerk, 1, 0.004f, OsuPlayfield.BASE_SIZE);

            Assert.That(result.Suggestions, Has.Count.EqualTo(5));
            Assert.That(result.Suggestions[0].ObjectAngle, Is.InRange(18, 38));

            float previousDistance = Vector2.Distance(example_pattern[^1].Position, example_pattern[^2].Position);
            float suggestedDistance = Vector2.Distance(result.Suggestions[0].Position, example_pattern[^1].Position);
            Assert.That(suggestedDistance / previousDistance, Is.InRange(0.82f, 1.18f));
        }

        [Test]
        public void TestSuggestionsRemainInsidePlayfield()
        {
            var edgePattern = new List<AimWaypoint>
            {
                new AimWaypoint(new Vector2(300, 180), 0),
                new AimWaypoint(new Vector2(485, 30), 120),
            };

            AimFlowAnalysisResult result = AimFlowAnalysis.Analyse(edgePattern, AimFlowModel.ArmWrist, 1, 0.004f, OsuPlayfield.BASE_SIZE);

            Assert.That(result.Suggestions, Is.Not.Empty);

            foreach (AimFlowSuggestion suggestion in result.Suggestions)
            {
                Assert.That(suggestion.Position.X, Is.InRange(24, OsuPlayfield.BASE_SIZE.X - 24));
                Assert.That(suggestion.Position.Y, Is.InRange(24, OsuPlayfield.BASE_SIZE.Y - 24));
                Assert.That(double.IsFinite(suggestion.Score), Is.True);
            }
        }

        [Test]
        public void TestSuggestionsDoNotStarveWhenPreferredJumpCannotFit()
        {
            var longJumpPattern = new List<AimWaypoint>
            {
                new AimWaypoint(new Vector2(24, 24), 0),
                new AimWaypoint(new Vector2(488, 360), 600),
                new AimWaypoint(new Vector2(24, 360), 655),
                new AimWaypoint(new Vector2(256, 192), 1255),
            };

            AimFlowAnalysisResult result = AimFlowAnalysis.Analyse(longJumpPattern, AimFlowModel.MinimumJerk, 1, 0.004f, OsuPlayfield.BASE_SIZE);

            Assert.That(result.Suggestions, Has.Count.EqualTo(5));
        }

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
