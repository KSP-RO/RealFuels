using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RealFuels
{
    public class BidirectionalDictionary<TForward, TReverse>
    {
        private Dictionary<TForward, TReverse> forward;
        private Dictionary<TReverse, TForward> reverse;

        public Indexer<TForward, TReverse> Fwd { get => new Indexer<TForward, TReverse>(ref forward, ref reverse); }
        public Indexer<TReverse, TForward> Rev { get => new Indexer<TReverse, TForward>(ref reverse, ref forward); }

        public class Indexer<TKey, TValue>
        {
            private Dictionary<TKey, TValue> fwd;
            private Dictionary<TValue, TKey> rev;

            public TValue this[TKey key]
            {
                get => fwd[key];
                set
                {
                    fwd[key] = value;
                    rev[value] = key;
                }
            }

            public ICollection<TKey> Keys { get => fwd.Keys; }

            public bool ContainsKey(TKey key) => fwd.ContainsKey(key);

            public Indexer(ref Dictionary<TKey, TValue> forward, ref Dictionary<TValue, TKey> reverse)
            {
                fwd = forward;
                rev = reverse;
            }
        }

        public int Count { get => forward.Count; }

        public void Add(TForward x, TReverse y)
        {
            forward.Add(x, y);
            reverse.Add(y, x);
        }

        public BidirectionalDictionary()
        {
            forward = new Dictionary<TForward, TReverse>();
            reverse = new Dictionary<TReverse, TForward>();
        }
    }

    public class ModulePairedConfigsEngine : ModuleEngineConfigs
    {
        #region fields
        [KSPField]
        public string primaryDescription = "primary";
        [KSPField]
        public string secondaryDescription = "secondary";
        [KSPField]
        public string toPrimaryText = string.Empty;
        [KSPField]
        public string toSecondaryText = string.Empty;
        [KSPField]
        public string pairSwitchDescription = string.Empty;
        #endregion


        #region bimodal state
        protected enum Mode { Primary, Secondary, Unpaired }
        protected BidirectionalDictionary<string, string> configPairs;  // (primary, secondary)
        protected Mode mode;
        protected ModuleEngines activeEngine;

        protected void LoadConfigPairs()
        {
            CheckConfigs(); // Ensure that `configs` have been deserialized already.

            configPairs = new BidirectionalDictionary<string, string>();

            // Consider all configs with `secondaryConfig` declared to be "primary".
            foreach (ConfigNode primaryCfg in configs.Where(c => c.HasValue("secondaryConfig")))
            {
                string primaryName = primaryCfg.GetValue("name");
                string secondaryName = primaryCfg.GetValue("secondaryConfig");

                // Look for the secondary config it specifies.
                if (!(GetConfigByName(secondaryName) is ConfigNode secondaryCfg))
                {
                    Debug.LogError($"*RFMEC* Config `{primaryName}` of {part} specifies a nonexistent secondaryConfig `{secondaryName}`!");
                    continue;
                }

                // Check if the primary config already exists.
                if (configPairs.Fwd.ContainsKey(primaryName))
                    Debug.LogError($"*RFMEC* {part} has multiple configs of the same name: `{primaryName}`!");
                // Check if the secondary config has already been claimed by another primary config.
                else if (configPairs.Rev.ContainsKey(secondaryName))
                    Debug.LogError($"*RFMEC* Config `{primaryName}` of {part} specifies `{secondaryName}` as its secondary config, but this config has already been declared as the secondary config of `{configPairs.Rev[primaryName]}`!");
                // Check if the primary config is the *secondary* config of another config.
                else if (configPairs.Rev.ContainsKey(primaryName))
                    Debug.LogError($"*RFMEC* Config `{primaryName}` declares a secondaryConfig itself, but it has already been specified to be the secondary of config `{configPairs.Rev[primaryName]}`!");
                else
                    configPairs.Add(primaryName, secondaryName);
            }
        }

        protected Mode GetMode(string configName)
        {
            if (configPairs == null) return Mode.Unpaired;
            if (configPairs.Fwd.ContainsKey(configName)) return Mode.Primary;
            if (configPairs.Rev.ContainsKey(configName)) return Mode.Secondary;
            return Mode.Unpaired;
        }

        protected string GetPairedConfig(string configName)
        {
            var mode = GetMode(configName);
            if (mode == Mode.Unpaired) return null;
            return mode == Mode.Primary ? configPairs.Fwd[configName] : configPairs.Rev[configName];
        }

        public string GetModeDescription(string configName)
        {
            var mode = GetMode(configName);
            return mode == Mode.Primary || mode == Mode.Unpaired ? primaryDescription : secondaryDescription;
        }

        public string GetToggleText(string configName)
        {
            var mode = GetMode(configName);
            if (mode == Mode.Unpaired) return "This config cannot be switched.";
            var toTargetText = mode == Mode.Primary ? toSecondaryText : toPrimaryText;
            return string.IsNullOrEmpty(toTargetText)
                ? $"Switch to {GetModeDescription(GetPairedConfig(configName))} mode"
                : toTargetText;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = true)]
        virtual public void ToggleMode()
        {
            if (mode == Mode.Unpaired) return;
            SetConfiguration(GetPairedConfig(configuration));
            UpdateSymmetryCounterparts();
        }
        #endregion


        #region PartModule overrides
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            LoadConfigPairs();
        }

        public override void OnStart(StartState state)
        {
            if (configPairs == null || configPairs.Count == 0) LoadConfigPairs();
            base.OnStart(state);
            activeEngine = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType) as ModuleEngines;
        }
        #endregion


        #region MEC overrides
        public override void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            base.SetConfiguration(newConfiguration, resetTechLevels);
            if (configPairs == null) return;
            mode = GetMode(configuration);
        }

        public override string GetConfigDisplayName(ConfigNode node)
        {
            string name = node.GetValue("name");
            var mode = GetMode(name);
            if (mode == Mode.Unpaired) return name;
            return $"{(mode == Mode.Primary ? name : configPairs.Rev[name])} ({GetModeDescription(name)})";
        }

        public override string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            string info = base.GetConfigInfo(config, addDescription, colorName);
            string name = config.GetValue("name");

            var mode = GetMode(name);
            if (mode == Mode.Unpaired) return info;

            var toggleDescription = mode == Mode.Primary ? "upgraded" : "downgraded";
            var pairedConfigInfo = base.GetConfigInfo(GetConfigByName(GetPairedConfig(name)), false, colorName);
            return $"{info}\nCan be {toggleDescription}:\n{pairedConfigInfo}";
        }

        public override string GUIButtonName => "Engine";
        public override string EditorDescription => "This engine has an optional upgrade. Select a configuration and whether you want to apply the upgrade using the button below.";

        virtual protected string GetToggleTextForConfigGUI(string configName)
        {
            if (GetMode(configName) == Mode.Unpaired) return GetToggleText(configName);
            var targetConfig = GetConfigByName(GetPairedConfig(configName));
            var costString = GetCostString(targetConfig);
            return $"{GetToggleText(configName)}{costString}";
        }

        // TODO(al2me6): Try to find ways to alleviate code duplication in this function.
        protected override void ConfigSelectionGUI()
        {
            GUILayout.BeginHorizontal();
            var toggleText = GetToggleTextForConfigGUI(configuration);
            if (GetMode(configuration) != Mode.Unpaired)
            {
                if (GUILayout.Button(new GUIContent(toggleText, pairSwitchDescription)))
                {
                    ToggleMode();
                    MarkWindowDirty();
                }
            }
            else
            {
                GUILayout.Label(toggleText);
            }
            GUILayout.EndHorizontal();

            // Display all primary and unpaired configs.
            foreach (ConfigNode primaryCfg in configs.Where(c => GetMode(c.GetValue("name")) != Mode.Secondary))
            {
                string primaryCfgName = primaryCfg.GetValue("name");
                // HACK: When the 'primary' is actually unpaired, make the secondary the same as the primary.
                // This way unpaired-ness is transparent to the rest of the code.
                string secondaryCfgName = GetMode(primaryCfgName) == Mode.Primary ? configPairs.Fwd[primaryCfgName] : primaryCfgName;
                ConfigNode secondaryCfg = GetConfigByName(secondaryCfgName);

                string displayName = primaryCfgName;

                ConfigNode targetCfg = mode == Mode.Primary || mode == Mode.Unpaired ? primaryCfg : secondaryCfg;
                string targetCfgName = targetCfg.GetValue("name");

                GUILayout.BeginHorizontal();

                var costString = GetCostString(targetCfg);

                if (configuration == primaryCfgName || configuration == secondaryCfgName)
                {
                    GUILayout.Label(new GUIContent($"Current config: {displayName}{costString}", GetConfigInfo(targetCfg)));
                }
                else
                {
                    if (CanConfig(primaryCfg))
                    {
                        if (UnlockedConfig(primaryCfg, part))
                        {
                            if (!UnlockedConfig(secondaryCfg, part))
                            {
                                EntryCostDatabase.SetUnlocked(secondaryCfgName);
                                EntryCostDatabase.UpdatePartEntryCosts();
                            }
                            if (GUILayout.Button(new GUIContent($"Switch to {displayName}{costString}", GetConfigInfo(targetCfg))))
                            {
                                SetConfiguration(targetCfgName, true);
                                UpdateSymmetryCounterparts();
                                MarkWindowDirty();
                            }
                        }
                        else
                        {
                            double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(primaryCfgName);
                            costString = string.Empty;
                            if (upgradeCost > 0d)
                            {
                                costString = $"({upgradeCost:N0}f)";
                                if (GUILayout.Button(new GUIContent($"Purchase {displayName}{costString}", GetConfigInfo(targetCfg))))
                                {
                                    if (EntryCostManager.Instance.PurchaseConfig(primaryCfgName))
                                    {
                                        EntryCostDatabase.SetUnlocked(secondaryCfgName);
                                        EntryCostDatabase.UpdatePartEntryCosts();
                                        SetConfiguration(targetCfgName, true);
                                        UpdateSymmetryCounterparts();
                                        MarkWindowDirty();
                                    }
                                }
                            }
                            else
                            {
                                // autobuy
                                EntryCostManager.Instance.PurchaseConfig(primaryCfgName);
                                EntryCostDatabase.SetUnlocked(secondaryCfgName);
                                EntryCostDatabase.UpdatePartEntryCosts();
                                if (GUILayout.Button(new GUIContent($"Switch to {displayName}{costString}", GetConfigInfo(targetCfg))))
                                {
                                    SetConfiguration(targetCfgName, true);
                                    UpdateSymmetryCounterparts();
                                    MarkWindowDirty();
                                }
                            }
                        }
                    }
                    else
                    {
                        if (techNameToTitle.TryGetValue(primaryCfg.GetValue("techRequired"), out string techStr))
                            techStr = "\nRequires: " + techStr;
                        GUILayout.Label(new GUIContent("Lack tech for " + displayName, GetConfigInfo(targetCfg) + techStr));
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        protected override void PartInfoGUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Current mode:</b> {GetModeDescription(configuration)}");
            GUILayout.EndHorizontal();
            base.PartInfoGUI();
        }
        #endregion
    }



    public class ModuleAnimatedBimodalEngine : ModulePairedConfigsEngine
    {
        #region fields
        [KSPField]
        public string animationName = string.Empty;
        [KSPField]
        public float thrustLerpTime = -1f;  // -1 is auto-compute from animation length.
        [KSPField(guiName = "Mode (toggleable)", isPersistant = true, guiActive = true,
            groupName = groupName, groupDisplayName = groupDisplayName)]
        public string stateDisplay;
        #endregion

        #region in-flight toggling
        [KSPAction("Toggle Engine Mode")]
        public void ToggleAction(KSPActionParam _) => ToggleMode();

        public override void ToggleMode()
        {
            if (mode == Mode.Unpaired) return;

            var oldAtmCurve = activeEngine.atmosphereCurve;
            float oldFuelFlow = activeEngine.maxFuelFlow;

            base.ToggleMode();

            StartToggleCoroutines(oldAtmCurve, oldFuelFlow);
            DoForEachSymmetryCounterpart(
                (eng) => (eng as ModuleAnimatedBimodalEngine).StartToggleCoroutines(oldAtmCurve, oldFuelFlow)
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
        }

        #region animation handling
        private enum AnimPosition { Begin, End, Forward, Reverse }
        private List<AnimationState> animationStates;
        private AnimPosition animPos;

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
            if (mode == Mode.Unpaired || animationStates == null) return;
            foreach (AnimationState animState in animationStates)
            {
                animState.normalizedTime = mode == Mode.Primary ? 0f : 1f;
                animState.speed = 0f;
                animPos = mode == Mode.Primary ? AnimPosition.Begin : AnimPosition.End;
            }
        }

        private void UpdateAnimationTarget(Mode oldMode)
        {
            if (animationStates == null) return;

            if (HighLogic.LoadedSceneIsEditor)
            {
                ForceAnimationPosition();
                return;
            }
            if (oldMode != Mode.Secondary && mode == Mode.Secondary)
                animPos = AnimPosition.Forward;
            if (oldMode != Mode.Primary && mode == Mode.Primary)
                animPos = AnimPosition.Reverse;
            if (mode == Mode.Unpaired)
                animPos = AnimPosition.Begin;
            if (oldMode == Mode.Unpaired || mode == Mode.Unpaired)
                ForceAnimationPosition();

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
        #endregion
        #endregion


        #region PartModule overrides
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            LoadAnimations();
            ForceAnimationPosition();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            CheckAnimationPosition();
        }
        #endregion


        #region MEC overrides
        public override void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            var oldMode = mode;
            base.SetConfiguration(newConfiguration, resetTechLevels);

            if (mode != Mode.Unpaired && isMaster)
            {
                Events[nameof(ToggleMode)].guiActive = true;
                Events[nameof(ToggleMode)].guiActiveEditor = true;
                Events[nameof(ToggleMode)].guiName = GetToggleText(configuration);
                stateDisplay = GetModeDescription(configuration);
            }
            else
            {
                Events[nameof(ToggleMode)].guiActive = false;
                Events[nameof(ToggleMode)].guiActiveEditor = false;
                stateDisplay = configuration;
            }

            UpdateAnimationTarget(oldMode);
        }

        public override string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            return base.GetConfigInfo(config, addDescription, colorName)
                .Replace("upgraded", "toggled in-flight")
                .Replace("downgraded", "toggled in-flight");
        }

        public override string GUIButtonName => "Bimodal Engine";
        public override string EditorDescription => "This engine can operate in two different modes. Select a configuration and an initial mode; you can change between modes (even in-flight) using the PAW or the button below.";

        protected override string GetToggleTextForConfigGUI(string config) => GetToggleText(config);
        #endregion
    }
}
