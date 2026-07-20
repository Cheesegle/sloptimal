// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osuTK;

namespace osu.Game.Rulesets.Osu.Edit.AimFlow
{
    /// <summary>
    /// Deterministic cursor-flow analysis used by the editor preview.
    /// </summary>
    /// <remarks>
    /// Each segment is a quintic Hermite curve. With fixed endpoint position, velocity and acceleration,
    /// that polynomial minimises integrated squared jerk. The 1€ variant filters only the tangent guide,
    /// keeping every hit position exact. The arm+wrist variant decomposes targets into a slow arm path and
    /// a bounded wrist residual before recombining them at every sample.
    /// </remarks>
    public static class AimFlowAnalysis
    {
        private const float playfield_margin = 24;

        // Calibrated from 3,350 consecutive-circle triples in the example maps supplied with this project.
        // The median object angle is 27.54 degrees and the median outgoing/incoming speed ratio is 1.01.
        private const double preferred_slop_object_angle = 27.5;
        private const double object_angle_scale = 22;

        public static AimFlowAnalysisResult Analyse(
            IReadOnlyList<AimWaypoint> inputWaypoints,
            AimFlowModel model,
            float oneEuroMinCutoff,
            float oneEuroBeta,
            Vector2 playfieldSize,
            int suggestionCount = 5,
            float rhythmMultiplier = 1,
            float spacingMultiplier = 1)
        {
            var waypoints = sanitiseWaypoints(inputWaypoints);

            if (waypoints.Count < 2)
                return AimFlowAnalysisResult.Empty;

            double nextDuration = applyRhythmMultiplier(estimateNextDuration(waypoints), rhythmMultiplier);
            var suggestions = createSuggestions(
                waypoints,
                model,
                playfieldSize,
                nextDuration,
                suggestionCount,
                spacingMultiplier: spacingMultiplier);

            var trajectoryWaypoints = new List<AimWaypoint>(waypoints);

            if (suggestions.Count > 0)
                trajectoryWaypoints.Add(new AimWaypoint(suggestions[0].Position, suggestions[0].Time));

            TrajectoryResult trajectory = GenerateTrajectory(trajectoryWaypoints, model, oneEuroMinCutoff, oneEuroBeta);
            return new AimFlowAnalysisResult(trajectory.Points, trajectory.ArmPoints, suggestions, nextDuration);
        }

        /// <summary>
        /// Ranks alternative positions for the circle currently being placed. Unlike <see cref="Analyse"/>,
        /// the placement position is only used to draw the live incoming trajectory; candidate positions are
        /// derived exclusively from committed history and the placement time.
        /// </summary>
        public static AimFlowPlacementResult AnalysePlacement(
            IReadOnlyList<AimWaypoint> inputHistory,
            AimWaypoint placement,
            AimFlowModel model,
            float oneEuroMinCutoff,
            float oneEuroBeta,
            Vector2 playfieldSize,
            int suggestionCount = 5,
            float spacingMultiplier = 1)
        {
            var history = sanitiseWaypoints(inputHistory.Where(w => w.Time < placement.Time - 0.01).ToArray());

            if (history.Count < 2 || placement.Time <= history[^1].Time + 0.01)
                return AimFlowPlacementResult.Empty;

            double placementDuration = estimatePlacementDuration(history, placement.Time);
            var suggestions = createSuggestions(
                history,
                model,
                playfieldSize,
                placementDuration,
                suggestionCount,
                placement.Time,
                spacingMultiplier: spacingMultiplier);

            if (placementDuration <= 1.01)
            {
                // Same-time objects after a slider tail have only one physically valid current location.
                // The overlay records that tail one millisecond earlier to keep waypoint times strictly ordered.
                suggestions = new List<AimFlowSuggestion>
                {
                    new AimFlowSuggestion(history[^1].Position, placement.Time, 100, 0),
                };
            }
            var trajectoryWaypoints = new List<AimWaypoint>(history)
            {
                placement,
            };

            TrajectoryResult trajectory = GenerateTrajectory(trajectoryWaypoints, model, oneEuroMinCutoff, oneEuroBeta);
            return new AimFlowPlacementResult(trajectory.Points, trajectory.ArmPoints, suggestions, placementDuration);
        }

        /// <summary>
        /// Generates several deterministic multi-circle continuations from a hovered current-circle candidate.
        /// A bounded beam search retains low-cost and spatially distinct branches without any learned model.
        /// </summary>
        public static IReadOnlyList<AimFlowPattern> CreateContinuationPatterns(
            IReadOnlyList<AimWaypoint> inputHistory,
            AimFlowSuggestion currentSuggestion,
            AimFlowModel model,
            float oneEuroMinCutoff,
            float oneEuroBeta,
            Vector2 playfieldSize,
            int patternCount = 3,
            int depth = 3,
            float rhythmMultiplier = 1,
            float spacingMultiplier = 1)
        {
            if (patternCount <= 0 || depth <= 0)
                return Array.Empty<AimFlowPattern>();

            patternCount = Math.Clamp(patternCount, 1, 5);
            depth = Math.Clamp(depth, 1, 4);

            var history = sanitiseWaypoints(inputHistory.Where(w => w.Time < currentSuggestion.Time - 0.01).ToArray());

            if (history.Count < 2 || currentSuggestion.Time <= history[^1].Time)
                return Array.Empty<AimFlowPattern>();

            var seed = new List<AimWaypoint>(history)
            {
                new AimWaypoint(currentSuggestion.Position, currentSuggestion.Time),
            };
            var beam = new List<PatternSearchState>
            {
                new PatternSearchState(seed, new List<AimFlowSuggestion>(), 0),
            };
            double continuationDuration = applyRhythmMultiplier(estimateNextDuration(seed), rhythmMultiplier);
            // Freeze the target spacing before expanding synthetic points, otherwise a non-neutral spacing
            // multiplier would feed back into the inferred speed and compound at later search depths.
            float continuationPreferredDistance = estimateNextDistance(seed, continuationDuration, spacingMultiplier);

            const int branch_factor = 5;
            const int beam_width = 15;
            // The 0.5 fallback is required when a recent full-field jump is followed by a hovered position
            // near the centre, where no 0.68 × 480 px continuation can fit inside the playfield margins.
            float[] continuationRadii = { 0.5f, 0.72f, 0.92f, 1.08f };

            for (int step = 0; step < depth; step++)
            {
                var expanded = new List<PatternSearchState>(beam.Count * branch_factor);

                foreach (PatternSearchState state in beam)
                {
                    IReadOnlyList<AimFlowSuggestion> nextSuggestions = createSuggestions(
                        state.Waypoints,
                        model,
                        playfieldSize,
                        continuationDuration,
                        branch_factor,
                        angleStep: 8,
                        radiusFactors: continuationRadii,
                        preferredDistanceOverride: continuationPreferredDistance);

                    foreach (AimFlowSuggestion suggestion in nextSuggestions)
                    {
                        var waypoints = new List<AimWaypoint>(state.Waypoints)
                        {
                            new AimWaypoint(suggestion.Position, suggestion.Time),
                        };
                        var future = new List<AimFlowSuggestion>(state.Future)
                        {
                            suggestion,
                        };
                        double discount = 1 / (1 + step * 0.35);
                        expanded.Add(new PatternSearchState(
                            waypoints,
                            future,
                            state.Cost + scoreToCost(suggestion.Score) * discount));
                    }
                }

                List<PatternSearchState> ordered = expanded
                                                        .OrderBy(state => state.Cost)
                                                        .ThenBy(state => state.Future[^1].Position.X)
                                                        .ThenBy(state => state.Future[^1].Position.Y)
                                                        .ToList();
                var nextBeam = new List<PatternSearchState>(beam_width);

                // Retain the best descendant of each immediate continuation before filling globally.
                // This prevents a slightly cheaper first branch from consuming the entire beam at depth 2+.
                foreach (PatternSearchState state in ordered)
                {
                    if (nextBeam.Any(existing => existing.Future[0].Position == state.Future[0].Position))
                        continue;

                    nextBeam.Add(state);
                }

                foreach (PatternSearchState state in ordered)
                {
                    if (nextBeam.Count >= beam_width)
                        break;

                    if (!nextBeam.Contains(state))
                        nextBeam.Add(state);
                }

                beam = nextBeam;

                if (beam.Count == 0)
                    return Array.Empty<AimFlowPattern>();
            }

            var selected = new List<PatternSearchState>(patternCount);

            // Prefer branches which diverge immediately, because these read as genuinely different flow choices.
            foreach (PatternSearchState state in beam)
            {
                if (selected.Count >= patternCount)
                    break;

                if (selected.Any(existing => Vector2.Distance(existing.Future[0].Position, state.Future[0].Position) < 40))
                    continue;

                selected.Add(state);

            }

            // Near edges the search may not have enough first-step diversity, so fall back to endpoint diversity.
            foreach (PatternSearchState state in beam)
            {
                if (selected.Count >= patternCount)
                    break;

                if (selected.Contains(state)
                    || selected.Any(existing => Vector2.Distance(existing.Future[^1].Position, state.Future[^1].Position) < 42))
                    continue;

                selected.Add(state);

            }

            foreach (PatternSearchState state in beam)
            {
                if (selected.Count >= patternCount)
                    break;

                if (selected.Contains(state))
                    continue;

                selected.Add(state);

            }

            var result = new List<AimFlowPattern>(selected.Count);

            foreach (PatternSearchState state in selected)
            {
                int contextStart = Math.Max(0, seed.Count - 3);
                var trajectoryWaypoints = seed.Skip(contextStart)
                                              .Concat(state.Future.Select(s => new AimWaypoint(s.Position, s.Time)))
                                              .ToArray();
                TrajectoryResult trajectory = GenerateTrajectory(trajectoryWaypoints, model, oneEuroMinCutoff, oneEuroBeta);
                int currentWaypointIndex = trajectoryWaypoints.Length - state.Future.Count - 1;
                int currentIndex = getWaypointSampleIndex(trajectoryWaypoints, currentWaypointIndex);
                IReadOnlyList<Vector2> points = trajectory.Points.Skip(currentIndex).ToArray();
                IReadOnlyList<Vector2> armPoints = trajectory.ArmPoints.Count == trajectory.Points.Count
                    ? trajectory.ArmPoints.Skip(currentIndex).ToArray()
                    : Array.Empty<Vector2>();
                result.Add(new AimFlowPattern(
                    points,
                    armPoints,
                    state.Future,
                    100 / (1 + state.Cost)));
            }

            return result;
        }

        /// <summary>
        /// Samples the full playfield placement objective for a current-circle quality heatmap.
        /// </summary>
        public static AimFlowHeatmap CreatePlacementHeatmap(
            IReadOnlyList<AimWaypoint> inputHistory,
            double placementTime,
            AimFlowModel model,
            Vector2 playfieldSize,
            int columns = 32,
            int rows = 24,
            float spacingMultiplier = 1,
            float rhythmMultiplier = 1)
        {
            columns = Math.Clamp(columns, 4, 64);
            rows = Math.Clamp(rows, 3, 48);
            var history = sanitiseWaypoints(inputHistory.Where(w => w.Time < placementTime - 0.01).ToArray());

            if (history.Count < 2 || placementTime <= history[^1].Time + 0.01)
                return AimFlowHeatmap.Empty;

            double placementDuration = estimatePlacementDuration(history, placementTime);
            bool exactTailAnchor = placementDuration <= 1.01;
            float preferredDistance = estimateNextDistance(history, placementDuration);

            if (!exactTailAnchor)
            {
                // Rhythm is a cadence correction for the geometric spacing prior only. The current circle's
                // real timestamp remains authoritative for velocity, minimum-jerk, and arm-motion costs.
                preferredDistance = Math.Clamp(
                    preferredDistance * normaliseMultiplier(spacingMultiplier, 0.5f, 2)
                    / normaliseMultiplier(rhythmMultiplier, 0.5f, 4),
                    24,
                    480);
            }

            AimWaypoint previous = history[^2];
            AimWaypoint current = history[^1];
            Vector2 incoming = current.Position - previous.Position;
            var scores = new double[columns * rows];
            double maximumScore = 0;

            if (incoming.LengthSquared < 1)
                incoming = new Vector2(1, 0);

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    var candidate = new Vector2(
                        (column + 0.5f) * playfieldSize.X / columns,
                        (row + 0.5f) * playfieldSize.Y / rows);

                    if (!exactTailAnchor && (candidate.X < playfield_margin || candidate.Y < playfield_margin
                                            || candidate.X > playfieldSize.X - playfield_margin || candidate.Y > playfieldSize.Y - playfield_margin))
                        continue;

                    double objectAngle = angleBetween(-incoming, candidate - current.Position) * 180 / Math.PI;
                    double cost = exactTailAnchor
                        ? square(Vector2.Distance(candidate, current.Position) / 24)
                        : scoreCandidate(history, candidate, placementDuration, preferredDistance, objectAngle, model, playfieldSize);
                    double score = 100 / (1 + cost);
                    scores[row * columns + column] = score;
                    maximumScore = Math.Max(maximumScore, score);
                }
            }

            return new AimFlowHeatmap(columns, rows, scores, maximumScore);
        }

        public static TrajectoryResult GenerateTrajectory(
            IReadOnlyList<AimWaypoint> inputWaypoints,
            AimFlowModel model,
            float oneEuroMinCutoff = 1,
            float oneEuroBeta = 0.004f)
        {
            var waypoints = sanitiseWaypoints(inputWaypoints);

            if (waypoints.Count < 2)
                return TrajectoryResult.Empty;

            Vector2[] rawPositions = waypoints.Select(w => w.Position).ToArray();
            double[] times = waypoints.Select(w => w.Time).ToArray();

            if (model == AimFlowModel.ArmWrist)
                return generateArmWristTrajectory(rawPositions, times);

            Vector2[] guidePositions = model == AimFlowModel.OneEuro
                ? filterGuidePositions(rawPositions, times, Math.Max(0.01f, oneEuroMinCutoff), Math.Max(0, oneEuroBeta))
                : rawPositions;

            Vector2[] velocities = estimateVelocities(guidePositions, times, model == AimFlowModel.OneEuro ? 0.82f : 0.92f);
            return new TrajectoryResult(sampleQuinticTrajectory(rawPositions, times, velocities), Array.Empty<Vector2>());
        }

        private static List<AimWaypoint> sanitiseWaypoints(IReadOnlyList<AimWaypoint> input)
        {
            var result = new List<AimWaypoint>(input.Count);

            foreach (var waypoint in input.OrderBy(w => w.Time))
            {
                if (!float.IsFinite(waypoint.Position.X) || !float.IsFinite(waypoint.Position.Y) || !double.IsFinite(waypoint.Time))
                    continue;

                if (result.Count > 0 && waypoint.Time <= result[^1].Time + 0.01)
                    result[^1] = waypoint;
                else
                    result.Add(waypoint);
            }

            return result;
        }

        private static List<AimFlowSuggestion> createSuggestions(
            IReadOnlyList<AimWaypoint> waypoints,
            AimFlowModel model,
            Vector2 playfieldSize,
            double nextDuration,
            int suggestionCount,
            double? suggestionTime = null,
            int angleStep = 4,
            IReadOnlyList<float>? radiusFactors = null,
            float spacingMultiplier = 1,
            float? preferredDistanceOverride = null)
        {
            if (suggestionCount <= 0)
                return new List<AimFlowSuggestion>();

            AimWaypoint previous = waypoints[^2];
            AimWaypoint current = waypoints[^1];
            Vector2 incoming = current.Position - previous.Position;

            if (incoming.LengthSquared < 1)
                incoming = new Vector2(1, 0);

            float preferredDistance = preferredDistanceOverride.HasValue && preferredDistanceOverride.Value > 0 && float.IsFinite(preferredDistanceOverride.Value)
                ? preferredDistanceOverride.Value
                : estimateNextDistance(waypoints, nextDuration, spacingMultiplier);
            double incomingAngle = Math.Atan2(incoming.Y, incoming.X);
            var all = new List<AimFlowSuggestion>(450);

            // Shorter fallbacks keep suggestions available when a recently observed full-field jump cannot
            // geometrically fit from the current point. The spacing cost still favours the observed distance.
            radiusFactors ??= new[] { 0.5f, 0.7f, 0.88f, 1, 1.12f };
            angleStep = Math.Clamp(angleStep, 2, 30);

            foreach (float radiusFactor in radiusFactors)
            {
                float radius = preferredDistance * radiusFactor;

                for (int degrees = 0; degrees < 360; degrees += angleStep)
                {
                    double angle = incomingAngle + degrees * Math.PI / 180;
                    var candidate = current.Position + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;

                    if (candidate.X < playfield_margin || candidate.Y < playfield_margin
                        || candidate.X > playfieldSize.X - playfield_margin || candidate.Y > playfieldSize.Y - playfield_margin)
                        continue;

                    double objectAngle = angleBetween(-incoming, candidate - current.Position) * 180 / Math.PI;
                    double cost = scoreCandidate(waypoints, candidate, nextDuration, preferredDistance, objectAngle, model, playfieldSize);
                    double score = 100 / (1 + cost);

                    all.Add(new AimFlowSuggestion(candidate, suggestionTime ?? current.Time + nextDuration, score, objectAngle));
                }
            }

            all.Sort((a, b) =>
            {
                int scoreComparison = b.Score.CompareTo(a.Score);

                if (scoreComparison != 0)
                    return scoreComparison;

                int xComparison = a.Position.X.CompareTo(b.Position.X);
                return xComparison != 0 ? xComparison : a.Position.Y.CompareTo(b.Position.Y);
            });

            var selected = new List<AimFlowSuggestion>(suggestionCount);

            foreach (float minimumSeparation in new[] { 34f, 24f, 12f, 0f })
            {
                foreach (var candidate in all)
                {
                    if (selected.Contains(candidate)
                        || selected.Any(existing => Vector2.Distance(existing.Position, candidate.Position) < minimumSeparation))
                        continue;

                    selected.Add(candidate);

                    if (selected.Count == suggestionCount)
                        return selected;
                }
            }

            return selected;
        }

        private static double scoreCandidate(
            IReadOnlyList<AimWaypoint> waypoints,
            Vector2 candidate,
            double nextDuration,
            float preferredDistance,
            double objectAngle,
            AimFlowModel model,
            Vector2 playfieldSize)
        {
            AimWaypoint previous = waypoints[^2];
            AimWaypoint current = waypoints[^1];
            Vector2 incoming = current.Position - previous.Position;
            Vector2 outgoing = candidate - current.Position;

            double angleCost = square((objectAngle - preferred_slop_object_angle) / object_angle_scale);
            double spacingCost = square((outgoing.Length - preferredDistance) / preferredDistance);

            double previousDuration = Math.Max(1, current.Time - previous.Time);
            Vector2 incomingVelocity = incoming / (float)previousDuration;
            Vector2 outgoingVelocity = outgoing / (float)nextDuration;
            double meanSpeed = Math.Max(0.001, (incomingVelocity.Length + outgoingVelocity.Length) / 2);
            double velocityChangeCost = square((outgoingVelocity - incomingVelocity).Length / meanSpeed / 2.5);

            float edgeClearance = Math.Min(Math.Min(candidate.X, playfieldSize.X - candidate.X), Math.Min(candidate.Y, playfieldSize.Y - candidate.Y));
            double edgeCost = edgeClearance >= 64 ? 0 : square((64 - edgeClearance) / 40);

            double overlapCost = 0;

            for (int i = Math.Max(0, waypoints.Count - 5); i < waypoints.Count - 1; i++)
            {
                float distance = Vector2.Distance(candidate, waypoints[i].Position);

                if (distance < 64)
                    overlapCost += square((64 - distance) / 32);
            }

            double turnConsistencyCost = 0;

            if (waypoints.Count >= 3)
            {
                Vector2 earlier = previous.Position - waypoints[^3].Position;
                float previousCross = cross(earlier, incoming);
                float candidateCross = cross(incoming, outgoing);

                // 81.2% of uninterrupted turns in the supplied examples retain their rotation sign.
                if (Math.Sign(previousCross) != 0 && Math.Sign(candidateCross) != 0 && Math.Sign(previousCross) != Math.Sign(candidateCross))
                    turnConsistencyCost = 0.2;
            }

            double minimumJerkCost = model == AimFlowModel.MinimumJerk
                ? computeMinimumJerkCost(waypoints, candidate, nextDuration)
                : 0;
            double armCost = model == AimFlowModel.ArmWrist
                ? computeArmJointCost(waypoints, candidate, nextDuration)
                : 0;

            return model switch
            {
                AimFlowModel.OneEuro => 0.53 * angleCost + 0.9 * spacingCost + 0.08 * velocityChangeCost + 0.11 * edgeCost
                                         + 0.2 * overlapCost + turnConsistencyCost,
                AimFlowModel.ArmWrist => 0.46 * angleCost + 0.85 * spacingCost + 0.04 * velocityChangeCost + 0.12 * edgeCost
                                         + 0.2 * overlapCost + turnConsistencyCost + 0.24 * armCost,
                _ => 0.56 * angleCost + spacingCost + 0.05 * velocityChangeCost + 0.12 * edgeCost
                     + 0.2 * overlapCost + turnConsistencyCost + 0.14 * minimumJerkCost,
            };
        }

        private static double computeMinimumJerkCost(IReadOnlyList<AimWaypoint> waypoints, Vector2 candidate, double nextDuration)
        {
            AimWaypoint previous = waypoints[^2];
            AimWaypoint current = waypoints[^1];
            double previousDuration = Math.Max(1, current.Time - previous.Time);

            Vector2 incomingSlope = (current.Position - previous.Position) / (float)previousDuration;
            Vector2 outgoingSlope = (candidate - current.Position) / (float)Math.Max(1, nextDuration);
            Vector2 currentVelocity = estimateInteriorVelocity(incomingSlope, outgoingSlope, previousDuration, nextDuration, 0.92f);

            double energy = quinticJerkEnergy(
                current.Position,
                candidate,
                currentVelocity * (float)nextDuration,
                Vector2.Zero) / Math.Pow(nextDuration, 5);
            double baseline = 720 * (candidate - current.Position).LengthSquared / Math.Pow(nextDuration, 5);
            Vector2 previousVelocity = Vector2.Zero;

            if (waypoints.Count >= 3)
            {
                AimWaypoint earlier = waypoints[^3];
                double earlierDuration = Math.Max(1, previous.Time - earlier.Time);
                Vector2 earlierSlope = (previous.Position - earlier.Position) / (float)earlierDuration;
                previousVelocity = estimateInteriorVelocity(earlierSlope, incomingSlope, earlierDuration, previousDuration, 0.92f);
            }

            energy += quinticJerkEnergy(
                previous.Position,
                current.Position,
                previousVelocity * (float)previousDuration,
                currentVelocity * (float)previousDuration) / Math.Pow(previousDuration, 5);
            baseline += 720 * (current.Position - previous.Position).LengthSquared / Math.Pow(previousDuration, 5);

            return baseline > 0.0000001 ? Math.Clamp(energy / baseline, 0, 4) : 0;
        }

        private static double quinticJerkEnergy(Vector2 start, Vector2 end, Vector2 startVelocity, Vector2 endVelocity)
        {
            Vector2 delta = end - start;
            Vector2 c3 = 10 * delta - 6 * startVelocity - 4 * endVelocity;
            Vector2 c4 = -15 * delta + 8 * startVelocity + 7 * endVelocity;
            Vector2 c5 = 6 * delta - 3 * startVelocity - 3 * endVelocity;
            Vector2 a = 6 * c3;
            Vector2 b = 24 * c4;
            Vector2 c = 60 * c5;

            return dot(a, a) + dot(a, b) + (dot(b, b) + 2 * dot(a, c)) / 3
                   + dot(b, c) / 2 + dot(c, c) / 5;
        }

        private static double computeArmJointCost(IReadOnlyList<AimWaypoint> waypoints, Vector2 candidate, double nextDuration)
        {
            if (waypoints.Count < 2)
                return 0;

            int start = Math.Max(0, waypoints.Count - 3);
            var angles = new List<Vector2>(4);

            for (int i = start; i < waypoints.Count; i++)
                angles.Add(inverseKinematics(waypoints[i].Position));

            angles.Add(inverseKinematics(candidate));

            if (angles.Count < 3)
                return 0;

            Vector2 firstDelta = angularDelta(angles[^3], angles[^2]);
            Vector2 secondDelta = angularDelta(angles[^2], angles[^1]);
            double previousDuration = Math.Max(1, waypoints[^1].Time - waypoints[^2].Time);

            Vector2 firstVelocity = firstDelta / (float)previousDuration;
            Vector2 secondVelocity = secondDelta / (float)Math.Max(1, nextDuration);
            return square((secondVelocity - firstVelocity).Length * Math.Min(previousDuration, nextDuration) / 0.65);
        }

        private static Vector2 inverseKinematics(Vector2 point)
        {
            // Virtual tablet/desk geometry: shoulder below the playfield and two links long enough to cover it.
            var shoulder = new Vector2(256, 455);
            const float upperArm = 290;
            const float forearm = 270;

            Vector2 relative = point - shoulder;
            double radiusSquared = relative.LengthSquared;
            double cosineElbow = Math.Clamp((radiusSquared - upperArm * upperArm - forearm * forearm) / (2 * upperArm * forearm), -1, 1);
            double elbow = Math.Acos(cosineElbow);
            double shoulderAngle = Math.Atan2(relative.Y, relative.X)
                                   - Math.Atan2(forearm * Math.Sin(elbow), upperArm + forearm * Math.Cos(elbow));

            return new Vector2((float)shoulderAngle, (float)elbow);
        }

        private static Vector2 angularDelta(Vector2 from, Vector2 to) => new Vector2(wrapAngle(to.X - from.X), wrapAngle(to.Y - from.Y));

        private static float wrapAngle(float angle)
        {
            while (angle > Math.PI) angle -= 2 * (float)Math.PI;
            while (angle < -Math.PI) angle += 2 * (float)Math.PI;
            return angle;
        }

        private static double estimateNextDuration(IReadOnlyList<AimWaypoint> waypoints)
        {
            var durations = new List<double>(3);

            for (int i = Math.Max(1, waypoints.Count - 3); i < waypoints.Count; i++)
            {
                double duration = waypoints[i].Time - waypoints[i - 1].Time;

                if (duration >= 35 && duration <= 1000)
                    durations.Add(duration);
            }

            if (durations.Count == 0)
                return 120;

            durations.Sort();
            return Math.Clamp(durations[durations.Count / 2], 55, 600);
        }

        private static double estimatePlacementDuration(IReadOnlyList<AimWaypoint> history, double placementTime)
        {
            double duration = placementTime - history[^1].Time;
            return Math.Max(1, duration);
        }

        private static double applyRhythmMultiplier(double duration, float rhythmMultiplier)
        {
            if (!double.IsFinite(duration) || duration <= 0)
                duration = 120;

            return Math.Max(1, duration / normaliseMultiplier(rhythmMultiplier, 0.5f, 4));
        }

        private static float estimateNextDistance(IReadOnlyList<AimWaypoint> waypoints, double nextDuration, float spacingMultiplier = 1)
        {
            var speeds = new List<float>(3);

            for (int i = Math.Max(1, waypoints.Count - 3); i < waypoints.Count; i++)
            {
                double duration = waypoints[i].Time - waypoints[i - 1].Time;
                float distance = Vector2.Distance(waypoints[i].Position, waypoints[i - 1].Position);

                if (duration >= 35 && distance >= 8)
                    speeds.Add(distance / (float)duration);
            }

            float estimatedDistance;

            if (speeds.Count == 0)
                estimatedDistance = 120;
            else
            {
                speeds.Sort();
                // Aim-slop jumps commonly span most of the playfield (median 292 px, 90th percentile 411 px
                // in the supplied examples), so retaining the full observed speed is important here. Sub-35 ms
                // transitions are allowed below the normal 48 px floor for exact slider-tail anchoring.
                float minimumDistance = nextDuration < 35 ? 1 : 48;
                estimatedDistance = Math.Clamp(speeds[speeds.Count / 2] * (float)nextDuration, minimumDistance, 480);
            }

            // Scale after the baseline clamp so values below 1x can deliberately produce tighter-than-default
            // spacing. The final bound still prevents degenerate or off-playfield search radii.
            float scaledMinimumDistance = nextDuration < 35 ? 1 : 24;
            return Math.Clamp(estimatedDistance * normaliseMultiplier(spacingMultiplier, 0.5f, 2), scaledMinimumDistance, 480);
        }

        private static float normaliseMultiplier(float value, float minimum, float maximum)
            => float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : 1;

        private static TrajectoryResult generateArmWristTrajectory(Vector2[] targets, double[] times)
        {
            Vector2[] armAnchors = targets.ToArray();
            const float armSmoothing = 0.72f;
            const float wristReach = 72;

            for (int iteration = 0; iteration < 7; iteration++)
            {
                Vector2[] previous = armAnchors.ToArray();

                for (int i = 1; i < armAnchors.Length - 1; i++)
                {
                    float interpolation = (float)((times[i] - times[i - 1]) / (times[i + 1] - times[i - 1]));
                    Vector2 neighbourGuide = Vector2.Lerp(previous[i - 1], previous[i + 1], interpolation);
                    Vector2 proposed = Vector2.Lerp(targets[i], neighbourGuide, armSmoothing);
                    Vector2 wrist = targets[i] - proposed;

                    if (wrist.Length > wristReach)
                        proposed = targets[i] - wrist.Normalized() * wristReach;

                    armAnchors[i] = proposed;
                }
            }

            Vector2[] wristAnchors = targets.Zip(armAnchors, (target, arm) => target - arm).ToArray();
            Vector2[] armVelocities = estimateVelocities(armAnchors, times, 1.04f);
            Vector2[] zeroVelocities = new Vector2[targets.Length];

            List<Vector2> armPath = sampleQuinticTrajectory(armAnchors, times, armVelocities);
            List<Vector2> wristPath = sampleQuinticTrajectory(wristAnchors, times, zeroVelocities);
            var combined = new List<Vector2>(Math.Min(armPath.Count, wristPath.Count));

            for (int i = 0; i < Math.Min(armPath.Count, wristPath.Count); i++)
                combined.Add(armPath[i] + wristPath[i]);

            return new TrajectoryResult(combined, armPath);
        }

        private static Vector2[] estimateVelocities(Vector2[] positions, double[] times, float tension)
        {
            var velocities = new Vector2[positions.Length];

            for (int i = 1; i < positions.Length - 1; i++)
            {
                double previousDuration = Math.Max(1, times[i] - times[i - 1]);
                double nextDuration = Math.Max(1, times[i + 1] - times[i]);
                Vector2 previousSlope = (positions[i] - positions[i - 1]) / (float)previousDuration;
                Vector2 nextSlope = (positions[i + 1] - positions[i]) / (float)nextDuration;

                velocities[i] = estimateInteriorVelocity(previousSlope, nextSlope, previousDuration, nextDuration, tension);
            }

            return velocities;
        }

        private static Vector2 estimateInteriorVelocity(Vector2 previousSlope, Vector2 nextSlope, double previousDuration, double nextDuration, float tension)
        {
            Vector2 velocity = (previousSlope * (float)nextDuration + nextSlope * (float)previousDuration)
                               / (float)(previousDuration + nextDuration) * tension;
            float maximum = Math.Min(previousSlope.Length, nextSlope.Length) * 1.5f;

            if (maximum > 0 && velocity.Length > maximum)
                velocity = velocity.Normalized() * maximum;

            return velocity;
        }

        private static List<Vector2> sampleQuinticTrajectory(Vector2[] positions, double[] times, Vector2[] velocities)
        {
            var result = new List<Vector2>();

            for (int i = 0; i < positions.Length - 1; i++)
            {
                double duration = Math.Max(1, times[i + 1] - times[i]);
                int sampleCount = Math.Clamp((int)Math.Ceiling(duration / 10), 8, 32);

                if (i == 0)
                    result.Add(positions[i]);

                Vector2 startVelocity = velocities[i] * (float)duration;
                Vector2 endVelocity = velocities[i + 1] * (float)duration;

                for (int sample = 1; sample <= sampleCount; sample++)
                {
                    float progress = sample / (float)sampleCount;
                    result.Add(quinticHermite(positions[i], positions[i + 1], startVelocity, endVelocity, progress));
                }
            }

            return result;
        }

        private static int getWaypointSampleIndex(IReadOnlyList<AimWaypoint> waypoints, int waypointIndex)
        {
            int sampleIndex = 0;

            for (int i = 0; i < waypointIndex; i++)
            {
                double duration = Math.Max(1, waypoints[i + 1].Time - waypoints[i].Time);
                sampleIndex += Math.Clamp((int)Math.Ceiling(duration / 10), 8, 32);
            }

            return sampleIndex;
        }

        private static Vector2 quinticHermite(Vector2 start, Vector2 end, Vector2 startVelocity, Vector2 endVelocity, float t)
        {
            Vector2 delta = end - start;
            Vector2 c3 = 10 * delta - 6 * startVelocity - 4 * endVelocity;
            Vector2 c4 = -15 * delta + 8 * startVelocity + 7 * endVelocity;
            Vector2 c5 = 6 * delta - 3 * startVelocity - 3 * endVelocity;

            float t2 = t * t;
            float t3 = t2 * t;
            float t4 = t3 * t;
            float t5 = t4 * t;
            return start + startVelocity * t + c3 * t3 + c4 * t4 + c5 * t5;
        }

        private static Vector2[] filterGuidePositions(Vector2[] positions, double[] times, float minCutoff, float beta)
        {
            Vector2[] forward = filterPass(positions, times, minCutoff, beta);
            Vector2[] reversedPositions = positions.Reverse().ToArray();
            double[] reversedTimes = new double[times.Length];

            for (int i = 1; i < reversedTimes.Length; i++)
                reversedTimes[i] = reversedTimes[i - 1] + times[^i] - times[^(i + 1)];

            Vector2[] backward = filterPass(reversedPositions, reversedTimes, minCutoff, beta).Reverse().ToArray();
            var result = new Vector2[positions.Length];

            for (int i = 0; i < result.Length; i++)
                result[i] = (forward[i] + backward[i]) / 2;

            result[0] = positions[0];
            result[^1] = positions[^1];
            return result;
        }

        private static Vector2[] filterPass(Vector2[] positions, double[] times, float minCutoff, float beta)
        {
            var xFilter = new OneEuroFilter(minCutoff, beta);
            var yFilter = new OneEuroFilter(minCutoff, beta);
            var result = new Vector2[positions.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                double seconds = (times[i] - times[0]) / 1000;
                result[i] = new Vector2(xFilter.Filter(positions[i].X, seconds), yFilter.Filter(positions[i].Y, seconds));
            }

            return result;
        }

        private static double angleBetween(Vector2 first, Vector2 second)
        {
            double denominator = Math.Sqrt(first.LengthSquared * second.LengthSquared);

            if (denominator < 0.0001)
                return 0;

            return Math.Acos(Math.Clamp((first.X * second.X + first.Y * second.Y) / denominator, -1, 1));
        }

        private static float cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;

        private static double dot(Vector2 first, Vector2 second) => first.X * second.X + first.Y * second.Y;

        private static double square(double value) => value * value;

        private static double scoreToCost(double score) => 100 / Math.Max(0.0001, score) - 1;

        private sealed class PatternSearchState
        {
            public List<AimWaypoint> Waypoints { get; }
            public List<AimFlowSuggestion> Future { get; }
            public double Cost { get; }

            public PatternSearchState(List<AimWaypoint> waypoints, List<AimFlowSuggestion> future, double cost)
            {
                Waypoints = waypoints;
                Future = future;
                Cost = cost;
            }
        }

        private sealed class OneEuroFilter
        {
            private readonly float minCutoff;
            private readonly float beta;
            private bool initialised;
            private double previousTime;
            private float previousRaw;
            private float filteredValue;
            private float filteredDerivative;

            public OneEuroFilter(float minCutoff, float beta)
            {
                this.minCutoff = minCutoff;
                this.beta = beta;
            }

            public float Filter(float value, double time)
            {
                if (!initialised)
                {
                    initialised = true;
                    previousTime = time;
                    previousRaw = filteredValue = value;
                    return value;
                }

                double elapsed = Math.Max(0.0001, time - previousTime);
                float derivative = (value - previousRaw) / (float)elapsed;
                filteredDerivative = lowPass(derivative, filteredDerivative, alpha(1, elapsed));
                float cutoff = minCutoff + beta * Math.Abs(filteredDerivative);
                filteredValue = lowPass(value, filteredValue, alpha(cutoff, elapsed));
                previousRaw = value;
                previousTime = time;
                return filteredValue;
            }

            private static float alpha(float cutoff, double elapsed)
            {
                double timeConstant = 1 / (2 * Math.PI * Math.Max(0.0001, cutoff));
                return (float)(1 / (1 + timeConstant / elapsed));
            }

            private static float lowPass(float value, float previous, float alpha) => alpha * value + (1 - alpha) * previous;
        }
    }

    public readonly record struct AimWaypoint(Vector2 Position, double Time);

    public readonly record struct AimFlowSuggestion(Vector2 Position, double Time, double Score, double ObjectAngle);

    public sealed class AimFlowAnalysisResult
    {
        public static AimFlowAnalysisResult Empty { get; } = new AimFlowAnalysisResult(Array.Empty<Vector2>(), Array.Empty<Vector2>(), Array.Empty<AimFlowSuggestion>(), 0);

        public IReadOnlyList<Vector2> Trajectory { get; }
        public IReadOnlyList<Vector2> ArmTrajectory { get; }
        public IReadOnlyList<AimFlowSuggestion> Suggestions { get; }
        public double NextDuration { get; }

        public AimFlowAnalysisResult(IReadOnlyList<Vector2> trajectory, IReadOnlyList<Vector2> armTrajectory, IReadOnlyList<AimFlowSuggestion> suggestions, double nextDuration)
        {
            Trajectory = trajectory;
            ArmTrajectory = armTrajectory;
            Suggestions = suggestions;
            NextDuration = nextDuration;
        }
    }

    public sealed class AimFlowPlacementResult
    {
        public static AimFlowPlacementResult Empty { get; } = new AimFlowPlacementResult(
            Array.Empty<Vector2>(),
            Array.Empty<Vector2>(),
            Array.Empty<AimFlowSuggestion>(),
            0);

        public IReadOnlyList<Vector2> Trajectory { get; }
        public IReadOnlyList<Vector2> ArmTrajectory { get; }
        public IReadOnlyList<AimFlowSuggestion> CurrentPositions { get; }
        public double PlacementDuration { get; }

        public AimFlowPlacementResult(
            IReadOnlyList<Vector2> trajectory,
            IReadOnlyList<Vector2> armTrajectory,
            IReadOnlyList<AimFlowSuggestion> currentPositions,
            double placementDuration)
        {
            Trajectory = trajectory;
            ArmTrajectory = armTrajectory;
            CurrentPositions = currentPositions;
            PlacementDuration = placementDuration;
        }
    }

    public sealed class AimFlowPattern
    {
        public IReadOnlyList<Vector2> Trajectory { get; }
        public IReadOnlyList<Vector2> ArmTrajectory { get; }
        public IReadOnlyList<AimFlowSuggestion> FutureCircles { get; }
        public double Score { get; }

        public AimFlowPattern(
            IReadOnlyList<Vector2> trajectory,
            IReadOnlyList<Vector2> armTrajectory,
            IReadOnlyList<AimFlowSuggestion> futureCircles,
            double score)
        {
            Trajectory = trajectory;
            ArmTrajectory = armTrajectory;
            FutureCircles = futureCircles;
            Score = score;
        }
    }

    public sealed class AimFlowHeatmap
    {
        public static AimFlowHeatmap Empty { get; } = new AimFlowHeatmap(0, 0, Array.Empty<double>(), 0);

        public int Columns { get; }
        public int Rows { get; }
        public IReadOnlyList<double> Scores { get; }
        public double MaximumScore { get; }

        public AimFlowHeatmap(int columns, int rows, IReadOnlyList<double> scores, double maximumScore)
        {
            Columns = columns;
            Rows = rows;
            Scores = scores;
            MaximumScore = maximumScore;
        }

        public double GetScore(int column, int row) => Scores[row * Columns + column];
    }

    public readonly record struct TrajectoryResult(IReadOnlyList<Vector2> Points, IReadOnlyList<Vector2> ArmPoints)
    {
        public static TrajectoryResult Empty => new TrajectoryResult(Array.Empty<Vector2>(), Array.Empty<Vector2>());
    }
}
