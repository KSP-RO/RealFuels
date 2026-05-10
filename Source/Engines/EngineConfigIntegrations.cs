using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Handles integration with B9PartSwitch and TestFlight mods.
    /// Manages B9PS module linking and TestFlight interop values.
    /// </summary>
    public class EngineConfigIntegrations
    {
        private readonly ModuleEngineConfigsBase _module;

        // B9PartSwitch reflection fields
        protected static bool _b9psReflectionInitialized = false;
        protected static FieldInfo B9PS_moduleID;
        protected static MethodInfo B9PS_SwitchSubtype;
        protected static FieldInfo B9PS_switchInFlight;

        public Dictionary<string, PartModule> B9PSModules;
        protected Dictionary<string, string> RequestedB9PSVariants = new Dictionary<string, string>();

        public EngineConfigIntegrations(ModuleEngineConfigsBase module)
        {
            _module = module;
            InitializeB9PSReflection();
        }

        #region TestFlight Integration

        /// <summary>
        /// Updates TestFlight interop values with current configuration.
        /// </summary>
        public void UpdateTFInterops()
        {
            TestFlightWrapper.AddInteropValue(_module.part, _module.isMaster ? "engineConfig" : "vernierConfig", _module.configuration, "RealFuels");
        }

        #endregion

        #region B9PartSwitch Integration

        /// <summary>
        /// Initializes reflection for B9PartSwitch if available.
        /// </summary>
        private void InitializeB9PSReflection()
        {
            if (_b9psReflectionInitialized || !Utilities.B9PSFound) return;
            B9PS_moduleID = Type.GetType("B9PartSwitch.CustomPartModule, B9PartSwitch")?.GetField("moduleID");
            B9PS_SwitchSubtype = Type.GetType("B9PartSwitch.ModuleB9PartSwitch, B9PartSwitch")?.GetMethod("SwitchSubtype");
            B9PS_switchInFlight = Type.GetType("B9PartSwitch.ModuleB9PartSwitch, B9PartSwitch")?.GetField("switchInFlight");
            _b9psReflectionInitialized = true;
        }

        /// <summary>
        /// Loads all B9PartSwitch modules that are linked to configs.
        /// </summary>
        public void LoadB9PSModules()
        {
            IEnumerable<string> b9psModuleIDs = _module.configs
                .Where(cfg => cfg.HasNode("LinkB9PSModule"))
                .SelectMany(cfg => cfg.GetNodes("LinkB9PSModule"))
                .Select(link => link?.GetValue("name"))
                .Where(moduleID => moduleID != null)
                .Distinct();

            B9PSModules = new Dictionary<string, PartModule>(b9psModuleIDs.Count());

            foreach (string moduleID in b9psModuleIDs)
            {
                var module = ModuleEngineConfigsBase.GetSpecifiedModules(_module.part, string.Empty, -1, "ModuleB9PartSwitch", false)
                    .FirstOrDefault(m => (string)B9PS_moduleID?.GetValue(m) == moduleID);
                if (module == null)
                    Debug.LogError($"*RFMEC* B9PartSwitch module with ID {moduleID} was not found for {_module.part}!");
                else
                    B9PSModules[moduleID] = module;
            }
        }

        /// <summary>
        /// Hide the GUI for all `ModuleB9PartSwitch`s managed by RF.
        /// This is somewhat of a hack-ish approach...
        /// </summary>
        public void HideB9PSVariantSelectors()
        {
            if (B9PSModules == null) return;
            foreach (var module in B9PSModules.Values)
            {
                module.Fields["currentSubtypeTitle"].guiActive = false;
                module.Fields["currentSubtypeTitle"].guiActiveEditor = false;
                module.Fields["currentSubtypeIndex"].guiActive = false;
                module.Fields["currentSubtypeIndex"].guiActiveEditor = false;
                module.Events["ShowSubtypesWindow"].guiActive = false;
                module.Events["ShowSubtypesWindow"].guiActiveEditor = false;
            }
        }

        /// <summary>
        /// Coroutine to hide B9PS in-flight selector after a frame delay.
        /// </summary>
        private IEnumerator HideB9PSInFlightSelector_Coroutine(PartModule module)
        {
            yield return null;
            module.Events["ShowSubtypesWindow"].guiActive = false;
        }

        /// <summary>
        /// Requests B9PS variant changes for the given config.
        /// </summary>
        public void RequestB9PSVariantsForConfig(ConfigNode node)
        {
            if (B9PSModules == null || B9PSModules.Count == 0) return;
            RequestedB9PSVariants.Clear();
            if (node.GetNodes("LinkB9PSModule") is ConfigNode[] links)
            {
                foreach (ConfigNode link in links)
                {
                    string moduleID = null, subtype = null;
                    if (!link.TryGetValue("name", ref moduleID))
                        Debug.LogError($"*RFMEC* Config `{_module.configurationDisplay}` of {_module.part} has a LinkB9PSModule specification without a name key!");
                    if (!link.TryGetValue("subtype", ref subtype))
                        Debug.LogError($"*RFMEC* Config `{_module.configurationDisplay}` of {_module.part} has a LinkB9PSModule specification without a subtype key!");
                    if (moduleID != null && subtype != null)
                        RequestedB9PSVariants[moduleID] = subtype;
                }
            }
            _module.StartCoroutine(ApplyRequestedB9PSVariants_Coroutine());
        }

        /// <summary>
        /// Coroutine that applies requested B9PS variant changes after a frame delay.
        /// </summary>
        protected IEnumerator ApplyRequestedB9PSVariants_Coroutine()
        {
            yield return new WaitForEndOfFrame();

            if (RequestedB9PSVariants.Count == 0) yield break;

            foreach (var entry in B9PSModules)
            {
                string moduleID = entry.Key;
                PartModule module = entry.Value;

                if (HighLogic.LoadedSceneIsFlight
                    && B9PS_switchInFlight != null
                    && !(bool)B9PS_switchInFlight.GetValue(module)) continue;

                if (!RequestedB9PSVariants.TryGetValue(moduleID, out string subtypeName))
                {
                    Debug.LogError($"*RFMEC* Config {_module.configurationDisplay} of {_module.part} does not specify a subtype for linked B9PS module with ID {moduleID}; defaulting to `{_module.configuration}`.");
                    subtypeName = _module.configuration;
                }

                B9PS_SwitchSubtype?.Invoke(module, new object[] { subtypeName });
                if (HighLogic.LoadedSceneIsFlight) _module.StartCoroutine(HideB9PSInFlightSelector_Coroutine(module));
            }

            RequestedB9PSVariants.Clear();
            // Clear symmetry counterparts' queues since B9PS already handles symmetry.
            _module.DoForEachSymmetryCounterpart(mec => mec.Integrations.ClearRequestedB9PSVariants());
        }

        /// <summary>
        /// Clears the requested B9PS variants queue.
        /// </summary>
        public void ClearRequestedB9PSVariants()
        {
            RequestedB9PSVariants.Clear();
        }

        /// <summary>
        /// Updates B9PS variants based on current config.
        /// </summary>
        public void UpdateB9PSVariants() => RequestB9PSVariantsForConfig(_module.config);

        #endregion
    }
}
