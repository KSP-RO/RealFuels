using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RealFuels
{
    internal class BidirectionalDictionary<TForward, TReverse>
    {
        private Dictionary<TForward, TReverse> forward;
        private Dictionary<TReverse, TForward> reverse;

        public Indexer<TForward, TReverse> Fwd { get => new Indexer<TForward, TReverse>(ref forward, ref reverse); }
        public Indexer<TReverse, TForward> Rev { get => new Indexer<TReverse, TForward>(ref reverse, ref forward); }

        internal class Indexer<TKey, TValue>
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

    public class ModuleAnimatedBimodalEngine : ModuleEngineConfigs
    {
        #region fields
        [KSPField]
        public string animationName = string.Empty;
        [KSPField]
        public string primaryDescription = "retracted";
        [KSPField]
        public string secondaryDescription = "extended";
        [KSPField]
        public string toPrimaryText = "Retract Nozzle";
        [KSPField]
        public string toSecondaryText = "Extend Nozzle";
        #endregion


        #region bimodal state
        private enum Mode { Primary, Secondary, Unpaired }
        private BidirectionalDictionary<string, string> configPairs;  // (primary, secondary)
        private Mode mode;
        [KSPField(guiName = "Mode (toggleable)", isPersistant = true, guiActive = true,
            groupName = groupName, groupDisplayName = groupDisplayName)]
        public string stateDisplay;
        private ModuleEngines activeEngine;

        private void LoadConfigPairs()
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
                    Debug.LogError($"*RFMEC* Config `{primaryName}` declares an secondaryConfig itself, but it has already been specified to be the secondary of config `{configPairs.Rev[primaryName]}`!");
                else
                    configPairs.Add(primaryName, secondaryName);
            }

            // Delete all unmatched configs.
            if (configPairs.Count * 2 != configs.Count)
            {
                List<ConfigNode> badConfigs = configs
                    .Where(c => GetMode(c.GetValue("name")) == Mode.Unpaired)
                    .ToList();
                foreach (var badConfig in badConfigs)
                {
                    Debug.LogWarning($"*RFMEC* {part} has unpaired config `{badConfig.GetValue("name")}`; removing!");
                    configs.Remove(badConfig);
                }
                SetConfiguration();
            }
        }

        private Mode GetMode(string configName)
        {
            if (configPairs == null) return Mode.Unpaired;
            if (configPairs.Fwd.ContainsKey(configName)) return Mode.Primary;
            if (configPairs.Rev.ContainsKey(configName)) return Mode.Secondary;
            return Mode.Unpaired;
        }

        private string GetPairedConfig(string configName)
        {
            var status = GetMode(configName);
            if (status == Mode.Unpaired) return null;
            return status == Mode.Primary ? configPairs.Fwd[configName] : configPairs.Rev[configName];
        }

        public string GetModeDescription(string configName)
        {
            return GetMode(configName) == Mode.Primary ? primaryDescription : secondaryDescription;
        }

        public string GetToggleText(string configName)
        {
            return GetMode(configName) == Mode.Primary ? toSecondaryText : toPrimaryText;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = true)]
        public void ToggleMode()
        {
            if (mode == Mode.Unpaired) return;

            SetConfiguration(GetPairedConfig(configuration));

            if (HighLogic.LoadedSceneIsFlight && activeEngine.getIgnitionState)
                StartCoroutine(TemporarilyRemoveSpoolUp());
        }

        private IEnumerator TemporarilyRemoveSpoolUp()
        {
            if (activeEngine is ModuleEnginesRF merf)
            {
                float originalResponseRate = merf.throttleResponseRate;
                merf.throttleResponseRate = 1_000_000f;
                // Wait a few frames.
                yield return null;
                yield return null;
                merf.throttleResponseRate = originalResponseRate;
            }
        }

        [KSPAction("Toggle Engine Mode")]
        public void ToggleAction(KSPActionParam _) => ToggleMode();

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
            if (mode == Mode.Unpaired || animationStates == null) return;

            if (HighLogic.LoadedSceneIsEditor)
            {
                ForceAnimationPosition();
                return;
            }
            if (oldMode == Mode.Primary && mode == Mode.Secondary)
                animPos = AnimPosition.Forward;
            if (oldMode == Mode.Secondary && mode == Mode.Primary)
                animPos = AnimPosition.Reverse;
            if (oldMode == Mode.Unpaired && mode != Mode.Unpaired)
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
            if (mode == Mode.Unpaired || animationStates == null) return;

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


        #region part module overrides
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            LoadConfigPairs();
        }

        public override void OnStart(StartState state)
        {
            if (configPairs == null || configPairs.Count == 0) LoadConfigPairs();
            LoadAnimations();
            base.OnStart(state);
            ForceAnimationPosition();

            activeEngine = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType) as ModuleEngines;
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
            base.SetConfiguration(newConfiguration, resetTechLevels);

            if (configPairs == null) return;

            var oldMode = mode;
            mode = GetMode(configuration);

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

        public override string GetConfigDisplayName(ConfigNode node)
        {
            string name = node.GetValue("name");
            return $"{(GetMode(name) == Mode.Secondary ? configPairs.Rev[name] : name)} ({GetModeDescription(name)} mode)";
        }

        public override string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            string info = base.GetConfigInfo(config, addDescription, colorName);
            string name = config.GetValue("name");

            if (GetMode(name) != Mode.Unpaired)
                return $"{info}\nCan be toggled in-flight:\n{base.GetConfigInfo(GetConfigByName(GetPairedConfig(name)), false, colorName)}";

            return info;
        }

        public override string GUIButtonName => "Bimodal Engine";
        public override string EditorDescription => "This engine can operate in two different modes. Select a configuration and an initial mode; you can change between modes (even in-flight) using the PAW or the button below.";

        // TODO(al2me6): Try to find ways to alleviate code duplication in this function.
        protected override void ConfigSelectionGUI()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(GetToggleText(configuration))))
            {
                ToggleMode();
                UpdateSymmetryCounterparts();
                MarkWindowDirty();
            }
            GUILayout.EndHorizontal();

            foreach (string primaryCfgName in configPairs.Fwd.Keys)
            {
                ConfigNode primaryCfg = GetConfigByName(primaryCfgName);
                string secondaryCfgName = configPairs.Fwd[primaryCfgName];
                ConfigNode secondaryCfg = GetConfigByName(secondaryCfgName);

                string displayName = primaryCfgName;

                ConfigNode targetCfg = mode == Mode.Primary ? primaryCfg : secondaryCfg;
                string targetCfgName = targetCfg.GetValue("name");

                GUILayout.BeginHorizontal();

                var costString = GetCostString(primaryCfg);

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
                                SetConfiguration(displayName, true);
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
}
