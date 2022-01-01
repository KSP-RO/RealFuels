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
        private enum AnimPosition { Begin, End, Forward, Reverse }
        private List<AnimationState> animationStates;
        private AnimPosition animPos;

        [KSPField]
        public float thrustLerpTime = -1f;  // -1 is auto-compute from animation length.
        [KSPField]
        public bool switchB9PSAfterLerpOnSecondaryToPrimary = false;

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

        public override void OnUpdate()
        {
            base.OnUpdate();
            CheckAnimationPosition();
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
        }

        protected void SetConfigurationAndMode(string newConfiguration, string patchName, bool resetTechLevels = false)
        {
            bool modeWasPrimary = IsPrimaryMode;
            activePatchName = patchName;
            SetConfiguration(newConfiguration);
            UpdateAnimationTarget(modeWasPrimary);
        }

        public override string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            var info = ConfigInfoString(config, addDescription, colorName);

            if (!ConfigHasSecondary(config))
                return info;

            var secondaryInfo = ConfigInfoString(SecondaryConfig(config), false, false);
            if (addDescription) info += "\n";
            var secondaryHeader = colorName
                ? $" <color=yellow>{secondaryDescription} mode:</color>"
                : $" {secondaryDescription} mode:";
            info += secondaryInfo.Replace(GetConfigDisplayName(config), secondaryHeader);
            return info;
        }

        protected override void DrawConfigSelectors()
        {
            if (GUILayout.Button(new GUIContent(ToggleText, toggleButtonHoverInfo)))
                ToggleMode();
            foreach (var node in configs)
            {
                bool hasSecondary = ConfigHasSecondary(node);
                var nodeApplied = IsSecondaryMode && hasSecondary ? SecondaryConfig(node) : node;
                DrawSelectButton(
                    nodeApplied,
                    node.GetValue("name") == configuration,
                    (configName) =>
                    {
                        SetConfigurationAndMode(configName, IsSecondaryMode && hasSecondary ? SecondaryPatchName(node) : "", true);
                        UpdateSymmetryCounterparts();
                        MarkWindowDirty();
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

            var oldAtmCurve = activeEngine.atmosphereCurve;
            float oldFuelFlow = activeEngine.maxFuelFlow;

            SetConfigurationAndMode(configuration, IsSecondaryMode ? "" : SecondaryPatchName(config));
            UpdateSymmetryCounterparts();

            StartToggleCoroutines(oldAtmCurve, oldFuelFlow);
            DoForEachSymmetryCounterpart(
                (eng) => (eng as ModuleBimodalEngineConfigs).StartToggleCoroutines(oldAtmCurve, oldFuelFlow)
            );
        }

        private void StartToggleCoroutines(FloatCurve oldAtmCurve, float oldFuelFlow)
        {
            if (HighLogic.LoadedSceneIsFlight && activeEngine.getIgnitionState)
            {
                StartCoroutine(TemporarilyRemoveSpoolUp());

                if (thrustLerpTime == -1 && animationStates != null)
                    thrustLerpTime = animationStates.Select(a => a.clip.length).Average();
                if (thrustLerpTime > 0f) StartCoroutine(LerpThrust(oldAtmCurve, oldFuelFlow));
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

            if (IsPrimaryMode && switchB9PSAfterLerpOnSecondaryToPrimary)
                ActivateB9PSVariantsOfConfig(SecondaryConfig(config));

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
                    eng.maxEngineTemp = origMaxTemp * eng.flowMult * 1.2d;
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

            if (IsPrimaryMode && switchB9PSAfterLerpOnSecondaryToPrimary)
                UpdateB9PSVariants();
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
        }

        private void ForceAnimationPosition()
        {
            if (animationStates == null) return;
            foreach (AnimationState animState in animationStates)
            {
                animState.normalizedTime = IsPrimaryMode ? 0f : 1f;
                animState.speed = 0f;
                animPos = IsPrimaryMode ? AnimPosition.Begin : AnimPosition.End;
            }
        }

        private void UpdateAnimationTarget(bool modeWasPrimary)
        {
            if (animationStates == null) return;

            if (HighLogic.LoadedSceneIsEditor)
            {
                ForceAnimationPosition();
                return;
            }
            if (modeWasPrimary && IsSecondaryMode)
                animPos = AnimPosition.Forward;
            if (!modeWasPrimary && IsPrimaryMode)
                animPos = AnimPosition.Reverse;

            UpdateAnimationSpeed();
        }

        private void UpdateAnimationSpeed()
        {
            if (animationStates == null) return;
            foreach (AnimationState animState in animationStates)
            {
                if (animPos == AnimPosition.Forward) animState.speed = 1f;
                else if (animPos == AnimPosition.Reverse) animState.speed = -1f;
                else animState.speed = 0f;
            }
        }

        private void CheckAnimationPosition()
        {
            if (animationStates == null) return;

            foreach (AnimationState animState in animationStates)
            {
                if (animState.normalizedTime >= 1f && animPos == AnimPosition.Forward)
                {
                    animPos = AnimPosition.End;
                    UpdateAnimationSpeed();
                    break;
                }
                if (animState.normalizedTime <= 0f && animPos == AnimPosition.Reverse)
                {
                    animPos = AnimPosition.Begin;
                    UpdateAnimationSpeed();
                    break;
                }
            }
        }
    }
}
