# Aim flow editor preview

This local osu!lazer build adds a deterministic cursor-flow overlay for osu! circle and slider-head placement, plus repositioning existing selected objects. It uses no machine learning.

## Using it

1. Start the portable release with `launch-aim-flow.cmd`. This prevents the official updater from replacing the custom build. Launching `osu!.exe` directly does not provide that protection.
2. Open a beatmap in the editor and begin placing a hit circle or slider, or select existing objects to reposition.
3. Expand the right-side **aim flow** toolbox and enable **Placement heatmap**. Hot pink/yellow cells have a higher deterministic placement score; blue/purple cells have a lower score.
4. Hover a heatmap cell to see its raw 0–100 score. Hovering never creates synthetic circles or secondary ribbons.
5. Optionally enable **Main cursor ribbon**. This is the one retained pink ribbon: it passes through recent hit positions and the active circle, slider head, or selection anchor.
6. Adjust **Rhythm / BPM correction** when the map cadence is interpreted incorrectly. `2×` halves the heatmap's preferred distance, matching a double-BPM interpretation; `0.5×` doubles it.
7. Adjust **Aim velocity / spacing** to make the heatmap tighter or wider without changing the target object's real editor timestamp. Setting rhythm and spacing both to `2×` preserves approximately the neutral preferred distance.
8. Choose **Minimum jerk**, **1€ smoothed**, or **Arm + wrist**. The cyan line in Arm + wrist mode is the simulated arm component.

Current-position guidance appears while a hit circle or slider is being previewed, or while existing aim objects are selected, once at least two earlier cursor waypoints establish an incoming flow. Active sliders are scored at their head and the heatmap does not alter or optimise their path shape. For multi-object selections, the earliest selected circle or slider is the stable group anchor; active placement always takes priority over selection. The heatmap depends on committed history and target time, not the live target position, so it remains stationary while being explored or while a selection is dragged. Hover is quantised to the cached 32×24 heatmap cells, ensuring the displayed number exactly matches the colour and preventing expensive recalculation for every mouse pixel. Earlier sliders are sampled along their repeated curve with every repeat endpoint preserved rather than treated as a straight chord; a slider tail sharing the target timestamp remains the sole zero-time placement anchor. Spinners reset the history and are not themselves heatmap targets.

Aim Flow draws no current-position or future-circle ghost markers and no hover-generated continuation ribbons. osu!'s native editor preview or selection blueprint remains responsible for displaying the real target object. The optional main cursor ribbon is the only generated path.

Rhythm and spacing modify the geometric prior independently: preferred distance is the neutral recent-velocity estimate × spacing ÷ rhythm. The actual editor timestamp remains authoritative for velocity, minimum-jerk, and arm-motion costs. Exact slider-tail anchors ignore both multipliers.

This is a portable build rather than a Velopack installation. If a renderer change asks osu! to restart, close it and run `launch-aim-flow.cmd` again.

## Models and optimisation

- **Minimum jerk** uses piecewise quintic Hermite motion. Each segment is the squared-jerk minimum for its selected endpoint velocities and zero endpoint accelerations. Candidate ranking includes the exact integrated squared-jerk cost of the two affected segments. This is a segmentwise model, not a global unconstrained via-point solution.
- **1€ smoothed** applies a forward/backward 1€ filter to the main ribbon's tangent guide while keeping every real hit position exact. `fc` controls low-speed smoothing and `β` adds responsiveness with speed. The heatmap uses this model's flow-weight profile; `fc` and `β` affect only the main ribbon.
- **Arm + wrist** separates a smooth broad arm anchor from a wrist residual bounded to 72 playfield pixels, then recombines them exactly at every target. Candidate ranking adds a virtual two-link inverse-kinematics velocity-change cost. It is a useful deterministic heuristic, not a calibrated biomechanical model of a specific player.

Target-position heatmap scoring evaluates the recent median cursor speed over the playfield. The objective combines:

- the characteristic acute object angle of the supplied slop maps;
- timing and spacing continuity;
- exact minimum-jerk or virtual arm-joint effort, depending on the chosen model;
- edge clearance, recent-object overlap, and turn-direction continuity.

Heatmap values are heuristic scores, not probabilities. Scoring is bounded to the 512×384 osu! playfield.

The heatmap evaluates the placement objective over a cached 32×24 playfield grid and only recomputes when relevant history, model, timing, rhythm, or spacing settings change. Hovering performs only a cached cell lookup to display the matching score; it does not run a future search or create any additional path drawables.

## Local calibration

The supplied examples contained 24 maps and 3,350 clean consecutive-circle triples. Their median object angle was 27.54°, median jump was 292 px, median object interval was 115 ms, and 81.2% of uninterrupted turns retained their rotation direction. Those aggregate measurements seed the deterministic objective; no player data is trained or retained.

## References

- [The 1€ Filter](https://gery.casiez.net/1euro/)
- [1€ Filter CHI 2012 paper](https://dl.acm.org/doi/10.1145/2207676.2208639)
- [Flash and Hogan: minimum-jerk arm trajectories](https://pmc.ncbi.nlm.nih.gov/articles/PMC6565116/)
- [Nakano et al.: minimum commanded torque change](https://journals.physiology.org/doi/full/10.1152/jn.1999.81.5.2140)
- [Lacquaniti et al.: two-thirds power law](https://pubmed.ncbi.nlm.nih.gov/6666647/)
