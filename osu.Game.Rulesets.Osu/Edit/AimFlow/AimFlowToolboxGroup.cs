// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Osu.Configuration;
using osuTK;

namespace osu.Game.Rulesets.Osu.Edit.AimFlow
{
    public partial class AimFlowToolboxGroup : EditorToolboxGroup
    {
        public BindableBool Enabled { get; } = new BindableBool();

        public BindableBool HeatmapEnabled { get; } = new BindableBool();

        public Bindable<AimFlowModel> Model { get; } = new Bindable<AimFlowModel>();

        public BindableFloat OneEuroMinCutoff { get; } = new BindableFloat(1)
        {
            MinValue = 0.1f,
            MaxValue = 5,
            Precision = 0.1f,
        };

        public BindableFloat OneEuroBeta { get; } = new BindableFloat(0.004f)
        {
            MinValue = 0,
            MaxValue = 0.02f,
            Precision = 0.0005f,
        };

        public BindableFloat RhythmMultiplier { get; } = new BindableFloat(1)
        {
            MinValue = 0.5f,
            MaxValue = 4,
            Precision = 0.05f,
        };

        public BindableFloat SpacingMultiplier { get; } = new BindableFloat(1)
        {
            MinValue = 0.5f,
            MaxValue = 2,
            Precision = 0.05f,
        };

        private ExpandableSlider<float> rhythmSlider = null!;
        private ExpandableSlider<float> spacingSlider = null!;
        private ExpandableSlider<float> minCutoffSlider = null!;
        private ExpandableSlider<float> betaSlider = null!;

        public AimFlowToolboxGroup()
            : base("aim flow")
        {
            Spacing = new Vector2(5);
        }

        public void BindConfig(OsuRulesetConfigManager config)
        {
            config.BindWith(OsuRulesetSetting.EditorAimFlowPreviewEnabled, Enabled);
            config.BindWith(OsuRulesetSetting.EditorAimFlowHeatmapEnabled, HeatmapEnabled);
            config.BindWith(OsuRulesetSetting.EditorAimFlowModel, Model);
            config.BindWith(OsuRulesetSetting.EditorAimFlowOneEuroMinCutoff, OneEuroMinCutoff);
            config.BindWith(OsuRulesetSetting.EditorAimFlowOneEuroBeta, OneEuroBeta);
            config.BindWith(OsuRulesetSetting.EditorAimFlowRhythmMultiplier, RhythmMultiplier);
            config.BindWith(OsuRulesetSetting.EditorAimFlowSpacingMultiplier, SpacingMultiplier);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new SettingsCheckbox
                {
                    LabelText = "Main cursor ribbon",
                    TooltipText = "Show the timing-aware main ribbon leading to the circle, slider head, or earliest selected aim object.",
                    Current = Enabled,
                },
                new SettingsCheckbox
                {
                    LabelText = "Placement heatmap",
                    TooltipText = "Score placement for the active circle/slider head or earliest selected aim object. Hover a cell for its number.",
                    Current = HeatmapEnabled,
                },
                rhythmSlider = new ExpandableSlider<float>
                {
                    Current = RhythmMultiplier,
                    ExpandedLabelText = "Rhythm / BPM correction (2x = half interval)",
                    KeyboardStep = 0.05f,
                },
                spacingSlider = new ExpandableSlider<float>
                {
                    Current = SpacingMultiplier,
                    ExpandedLabelText = "Aim velocity / spacing",
                    KeyboardStep = 0.05f,
                },
                new SettingsEnumDropdown<AimFlowModel>
                {
                    LabelText = "Motion model",
                    TooltipText = "All models pass exactly through hit positions. 1€ filters the tangent guide; arm + wrist separates broad and fine motion.",
                    Current = Model,
                },
                minCutoffSlider = new ExpandableSlider<float>
                {
                    Current = OneEuroMinCutoff,
                    ExpandedLabelText = "1€ minimum cutoff",
                    KeyboardStep = 0.1f,
                },
                betaSlider = new ExpandableSlider<float>
                {
                    Current = OneEuroBeta,
                    ExpandedLabelText = "1€ speed coefficient",
                    KeyboardStep = 0.0005f,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            OneEuroMinCutoff.BindValueChanged(value => minCutoffSlider.ContractedLabelText = $"1€ fc: {value.NewValue:0.0} Hz", true);
            OneEuroBeta.BindValueChanged(value => betaSlider.ContractedLabelText = $"1€ β: {value.NewValue:0.0000}", true);
            RhythmMultiplier.BindValueChanged(value => rhythmSlider.ContractedLabelText = $"rhythm: {value.NewValue:0.##}×", true);
            SpacingMultiplier.BindValueChanged(value => spacingSlider.ContractedLabelText = $"spacing: {value.NewValue:0.##}×", true);
            Model.BindValueChanged(value =>
            {
                float alpha = value.NewValue == AimFlowModel.OneEuro ? 1 : 0.35f;
                minCutoffSlider.FadeTo(alpha, 150);
                betaSlider.FadeTo(alpha, 150);
            }, true);
        }
    }
}
