using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RealFuels
{
    public class ModuleBimodalEngineConfigs : ModulePatchableEngineConfigs
    {
        [KSPField]
        public string primaryDescription = "Retracted";
        [KSPField]
        public string secondaryDescription = "Extended";
        [KSPField]
        public string toPrimaryText = "Retract Nozzle";
        [KSPField]
        public string toSecondaryText = "Extend Nozzle";
        [KSPField]
        public string toggleButtonHoverInfo = string.Empty;

        [KSPField]
        public string animationName = string.Empty;
        private List<AnimationState> animationStates;
        [KSPField]
        public float switchB9PSAtAnimationTime = -1f;

        [KSPField]
        public float thrustLerpTime = -1f;  // -1 is auto-compute from animation length.

        [KSPField(guiName = "Mode", isPersistant = true, guiActive = true, guiActiveEditor = true, groupName = groupName, groupDisplayName = groupDisplayName)]
        public string modeDisplay;

        protected ModuleEngines activeEngine;


        protected void ValidatePairing()
        {
            foreach (var node in configs)
            {
                if (GetPatchesOfConfig(node).Length != 1)
                    Debug.LogError($"**ModuleAnimatedBimodalEngine** Configuration {node.GetValue("name")} does not specify a `SUBCONFIG` for its `{secondaryDescription}` mode!");
            }
        }

        protected bool ConfigHasSecondary(ConfigNode node) => ConfigHasPatch(node);

        protected ConfigNode SecondaryConfig(ConfigNode primary)
        {
            if (ConfigHasSecondary(primary))
                return PatchConfig(primary, GetPatchesOfConfig(primary)[0], false);
            return null;
        }

        protected string SecondaryPatchName(ConfigNode primary)
        {
            if (ConfigHasSecondary(primary))
                return GetPatchesOfConfig(primary)[0].GetValue("name");
            return null;
        }

        protected bool IsPrimaryMode => activePatchName == "";
        protected bool IsSecondaryMode => !IsPrimaryMode;

        protected string ActiveModeDescription => IsSecondaryMode ? secondaryDescription : primaryDescription;

        protected string ToggleText => IsPrimaryMode ? toSecondaryText : toPrimaryText;


        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            ValidatePairing();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            activeEngine = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType) as ModuleEngines;
            LoadAnimations();
            ForceAnimationPosition();
        }


        public override string GUIButtonName => "Bimodal Engine";
        public override string EditorDescription => "This engine can operate in two different modes. Select a configuration and an initial mode; you can change between modes (even in-flight) using the PAW or the button below.";

        public override string GetConfigDisplayName(ConfigNode node)
        {
            if (node.HasValue("displayName"))
                return node.GetValue("displayName");
            return node.GetValue("name");
        }

        public override void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            base.SetConfiguration(newConfiguration, resetTechLevels);
            modeDisplay = ActiveModeDescription;
            Events[nameof(ToggleMode)].guiName = ToggleText;
            ForceAnimationPosition();
        }

        public override string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            var info = ConfigInfoString(config, addDescription, colorName);

            if (!ConfigHasSecondary(config))
                return info;

            var isSecondary = ConfigIsPatched(config);
            var configName = config.GetValue("name");

            var counterpartInfo = ConfigInfoString(isSecondary ? GetConfigByName(configName) : SecondaryConfig(config), false, false);
            if (addDescription) info += "\n";
            var counterpartHeader = isSecondary ? primaryDescription : secondaryDescription;
            counterpartHeader = colorName
                ? $" <color=yellow>{counterpartHeader} mode:</color>"
                : $" {counterpartHeader} mode:";
            info += counterpartInfo.Replace(GetConfigDisplayName(config), counterpartHeader);
            return info;
        }

        protected override void DrawConfigSelectors(IEnumerable<ConfigNode> availableConfigNodes)
        {
            if (GUILayout.Button(new GUIContent(ToggleText, toggleButtonHoverInfo)))
                ToggleMode();
            foreach (var node in availableConfigNodes)
            {
                bool hasSecondary = ConfigHasSecondary(node);
                var nodeApplied = IsSecondaryMode && hasSecondary ? SecondaryConfig(node) : node;
                DrawSelectButton(
                    nodeApplied,
                    node.GetValue("name") == configuration,
                    (configName) =>
                    {
                        activePatchName = IsSecondaryMode && hasSecondary ? SecondaryPatchName(node) : "";
                        GUIApplyConfig(configName);
                    });
            }
        }

        protected override void DrawPartInfo()
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label($"<b>Current mode:</b> {ActiveModeDescription}");
            }
            base.DrawPartInfo();
        }


        [KSPAction("Toggle Engine Mode")]
        public void ToggleAction(KSPActionParam _) => ToggleMode();

        [KSPEvent(guiActive = true, guiActiveEditor = true, groupName = groupName, groupDisplayName = groupDisplayName)]
        public void ToggleMode()
        {
            if (!ConfigHasSecondary(config)) return;

            bool animateForward = IsPrimaryMode;
            FloatCurve oldAtmCurve = activeEngine.atmosphereCurve;
            float oldFuelFlow = activeEngine.maxFuelFlow;

            activePatchName = IsPrimaryMode ? SecondaryPatchName(config) : "";
            SetConfiguration();
            UpdateSymmetryCounterparts();

            StartToggleCoroutines(animateForward, oldAtmCurve, oldFuelFlow);
            DoForEachSymmetryCounterpart(
                (eng) => (eng as ModuleBimodalEngineConfigs).StartToggleCoroutines(animateForward, oldAtmCurve, oldFuelFlow));
        }

        private void StartToggleCoroutines(bool animateForward, FloatCurve oldAtmCurve, float oldFuelFlow)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            StartCoroutine(DriveAnimation(animateForward));

            if (activeEngine.getIgnitionState)
            {
                StartCoroutine(TemporarilyRemoveSpoolUp());
                if (thrustLerpTime > 0f)
                    StartCoroutine(LerpThrust(oldAtmCurve, oldFuelFlow));
            }
        }

        private IEnumerator TemporarilyRemoveSpoolUp()
        {
            if (!(activeEngine is ModuleEnginesRF merf)) yield break;
            float originalResponseRate = merf.throttleResponseRate;
            merf.throttleResponseRate = 1_000_000f;
            yield return new WaitForFixedUpdate();
            merf.throttleResponseRate = originalResponseRate;
        }

        private IEnumerator LerpThrust(FloatCurve oldAtmCurve, float oldFuelFlow)
        {
            float time = 0f;
            float? prevIspMult = null;
            float? prevFlowMult = null;

            if (!(activeEngine is SolverEngines.ModuleEnginesSolver eng)) yield break;

            double origMaxTemp = eng.maxEngineTemp;

            // If something else has overridden these values, bail because that thing is probably
            // more important.
            if (!Mathf.Approximately((float)eng.ispMult, 1f) || !Mathf.Approximately((float)eng.flowMult, 1f))
                yield break;

            while (time < thrustLerpTime)
            {
                if (prevIspMult is float iMult && !Mathf.Approximately(iMult, (float)eng.ispMult)
                        || prevFlowMult is float fMult && !Mathf.Approximately(fMult, (float)eng.flowMult))
                    yield break;

                var atmPressure = (float)(part.atmDensity * 0.8163265147242933); // kg/m^3 to atm
                prevIspMult = Mathf.Lerp(
                    oldAtmCurve.Evaluate(atmPressure) / eng.atmosphereCurve.Evaluate(atmPressure),
                    1f,
                    time / thrustLerpTime
                );
                eng.ispMult = prevIspMult.Value;

                prevFlowMult = Mathf.Lerp((float)(oldFuelFlow / eng.maxFuelFlow), 1f, time / thrustLerpTime);
                eng.flowMult = prevFlowMult.Value;

                if (oldFuelFlow > eng.maxFuelFlow)
                {
                    // 20% margin
                    eng.maxEngineTemp = origMaxTemp * eng.flowMult * eng.ispMult * 1.2d;
                }

                time += TimeWarp.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            if (oldFuelFlow > eng.maxFuelFlow)
            {
                time -= thrustLerpTime;
                double newLerpTime = thrustLerpTime * 0.5d;
                while (time < newLerpTime)
                {
                    eng.maxEngineTemp = origMaxTemp * UtilMath.LerpUnclamped(1.2d, 1.0d, time / newLerpTime);
                    time += TimeWarp.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                }
            }

            // Set it back to exactly 1 once we're done.
            eng.ispMult = 1d;
            eng.flowMult = 1d;
            eng.maxEngineTemp = origMaxTemp;
        }


        private void LoadAnimations()
        {
            // Adapted from https://github.com/post-kerbin-mining-corporation/DeployableEngines/blob/5cd045e1b2f65cf758b510c267af0c1dfb8c0502/Source/DeployableEngines/ModuleDeployableEngine.cs#L166
            if (string.IsNullOrEmpty(animationName)) return;

            animationStates = new List<AnimationState>();
            foreach (Animation anim in part.FindModelAnimators(animationName))
            {
                AnimationState animState = anim[animationName];
                animState.speed = 0;
                animState.enabled = true;
                animState.wrapMode = WrapMode.ClampForever;
                anim.Blend(animationName);
                animationStates.Add(animState);
            }

            if (thrustLerpTime == -1 && animationStates.Count > 0)
                thrustLerpTime = animationStates.Select(a => a.clip.length).Average();
        }

        private void ForceAnimationPosition()
        {
            SetAnimationTime(IsPrimaryMode ? 0f : 1f);
            SetAnimationSpeed(0f);
        }

        private void SetAnimationTime(float time)
        {
            if (animationStates == null) return;
            foreach (var state in animationStates)
                state.normalizedTime = time;
        }

        private void SetAnimationSpeed(float speed)
        {
            if (animationStates == null) return;
            foreach (var state in animationStates)
                state.speed = speed;
        }

        private IEnumerator DriveAnimation(bool forward)
        {
            if (animationStates == null) yield break;

            bool b9psNeedsReset = false;
            if (B9PSModules != null && B9PSModules.Count != 0 && switchB9PSAtAnimationTime >= 0f)
            {
                ActivateB9PSVariantsOfConfig(IsPrimaryMode ? SecondaryConfig(config) : GetConfigByName(configuration));
                b9psNeedsReset = true;
            }

            SetAnimationTime(forward ? 0f : 1f);
            SetAnimationSpeed(forward ? 1f : -1f);
            bool animationFinished = false;
            while (!animationFinished)
            {
                foreach (var animState in animationStates)
                {
                    if (forward && animState.normalizedTime >= 1f || !forward && animState.normalizedTime <= 0f)
                        animationFinished = true;

                    if (!b9psNeedsReset) continue;
                    if (forward && animState.normalizedTime >= switchB9PSAtAnimationTime
                        || !forward && animState.normalizedTime <= switchB9PSAtAnimationTime)
                    {
                        UpdateB9PSVariants();
                        b9psNeedsReset = false;
                    }
                }
                yield return new WaitForFixedUpdate();
            }
            SetAnimationSpeed(0f);

            if (b9psNeedsReset) UpdateB9PSVariants();
        }
    }
}
