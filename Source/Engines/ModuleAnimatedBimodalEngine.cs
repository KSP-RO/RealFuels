using System;
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
        private enum State { Primary, Secondary, Unpaired }
        private enum AnimStatus { Begin, End, Forward, Reverse }

        [KSPField]
        public string animationName = String.Empty;
        [KSPField]
        public string toPrimaryText = "Retract Nozzle";
        [KSPField]
        public string toSecondaryText = "Deploy Nozzle";
        [KSPField]
        public bool shutdownWhileSwitching = false;

        private BidirectionalDictionary<string, string> configPairs;  // (primary, secondary)
        private State state;
        private ModuleEngines activeEngine;

        private List<AnimationState> animationStates;
        private AnimStatus animStatus;

        private void LoadConfigPairs()
        {
            configPairs = new BidirectionalDictionary<string, string>();

            // Consider all configs `secondaryConfig` declared to be "primary".
            foreach (ConfigNode primaryCfg in configs.Where(c => c.HasValue("secondaryConfig")))
            {
                string primaryName = primaryCfg.GetValue("name");
                string secondaryName = primaryCfg.GetValue("secondaryConfig");

                // Look for the secondary config it specifies.
                if (!(configs.Find(c => c.GetValue("name") == secondaryName) is ConfigNode secondaryCfg))
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

            // Dump all the unmatched configs to log.
            if (configPairs.Count * 2 != configs.Count)
            {
                configs
                    .Select(c => c.GetValue("name"))
                    .Where(c => !(configPairs.Fwd.ContainsKey(c) || configPairs.Rev.ContainsKey(c)))
                    .ToList()
                    .ForEach(c => Debug.LogWarning($"*RFMEC* {part} has unpaired config `{c}`!"));
            }
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

        public override void OnStart(StartState state)
        {
            ConfigSaveLoad();
            LoadConfigPairs();
            LoadAnimations();
            base.OnStart(state);
            ForceAnimationState();

            activeEngine = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType) as ModuleEngines;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (state == State.Unpaired || animationStates == null) return;

            foreach (AnimationState animState in animationStates)
            {
                if (animState.normalizedTime >= 1f && animStatus == AnimStatus.Forward)
                {
                    animStatus = AnimStatus.End;
                    UpdateAnimationSpeed();
                    break;
                }
                if (animState.normalizedTime <= 0f && animStatus == AnimStatus.Reverse)
                {
                    animStatus = AnimStatus.Begin;
                    UpdateAnimationSpeed();
                    break;
                }
            }
        }

        public override void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            var oldState = state;
            base.SetConfiguration(newConfiguration, resetTechLevels);

            if (configPairs == null) return;

            if (configPairs.Fwd.ContainsKey(configuration))
            {
                state = State.Primary;
                Events["ToggleMode"].guiActive = true;
                Events["ToggleMode"].guiActiveEditor = true;
                Events["ToggleMode"].guiName = toSecondaryText;
            }
            else if (configPairs.Rev.ContainsKey(configuration))
            {
                state = State.Secondary;
                Events["ToggleMode"].guiActive = true;
                Events["ToggleMode"].guiActiveEditor = true;
                Events["ToggleMode"].guiName = toPrimaryText;
            }
            else
            {
                state = State.Unpaired;
                Events["ToggleMode"].guiActive = false;
                Events["ToggleMode"].guiActiveEditor = false;
            }

            UpdateAnimationTarget(oldState);
        }

        [KSPEvent(guiActive = true, guiActiveEditor = true)]
        public void ToggleMode()
        {
            if (state == State.Unpaired) return;

            bool wasIgnited = activeEngine.getIgnitionState;
            if (shutdownWhileSwitching) activeEngine.EngineIgnited = false;

            var targetConfig = state == State.Primary ? configPairs.Fwd[configuration] : configPairs.Rev[configuration];
            SetConfiguration(targetConfig);

            if (wasIgnited && shutdownWhileSwitching)
                activeEngine.Actions["ActivateAction"].Invoke(new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate));
        }

        private void ForceAnimationState()
        {
            if (state == State.Unpaired || animationStates == null) return;
            foreach (AnimationState animState in animationStates)
            {
                animState.normalizedTime = state == State.Primary ? 0f : 1f;
                animState.speed = 0f;
                animStatus = state == State.Primary ? AnimStatus.Begin : AnimStatus.End;
            }
        }

        private void UpdateAnimationTarget(State oldState)
        {
            if (state == State.Unpaired || animationStates == null) return;

            if (HighLogic.LoadedSceneIsEditor)
            {
                ForceAnimationState();
                return;
            }
            if (oldState == State.Primary && state == State.Secondary)
                animStatus = AnimStatus.Forward;
            if (oldState == State.Secondary && state == State.Primary)
                animStatus = AnimStatus.Reverse;
            if (oldState == State.Unpaired && state != State.Unpaired)
                ForceAnimationState();

            UpdateAnimationSpeed();
        }

        private void UpdateAnimationSpeed()
        {
            if (animationStates == null) return;
            foreach (AnimationState animState in animationStates)
            {
                if (animStatus == AnimStatus.Forward) animState.speed = 1f;
                else if (animStatus == AnimStatus.Reverse) animState.speed = -1f;
                else animState.speed = 0f;
            }
        }
    }
}
