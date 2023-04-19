using KSP.Localization;
namespace RealFuels
{
    public class ModuleHybridEngine : ModuleEngineConfigsBase
    {
        public override string GUIButtonName => Localizer.GetStringByTag("#RF_HybridEngine_ButtonName"); // "Multi-Mode Engine"
        public override string EditorDescription => Localizer.GetStringByTag("#RF_HybridEngine_ButtonName_desc"); // "Select a default configuration. You can cycle through all other configurations in flight."
        ModuleEngines ActiveEngine = null;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (!isMaster)
            {
                Actions[nameof(SwitchAction)].active = false;
                Events[nameof(SwitchEngine)].guiActive = false;
                Events[nameof(SwitchEngine)].guiActiveEditor = false;
                Events[nameof(SwitchEngine)].guiActiveUnfocused = false;
            }
        }

        [KSPAction("#RF_HybridEngine_SwitchEngineMode")] // Switch Engine Mode
        public void SwitchAction(KSPActionParam _) => SwitchEngine();

        [KSPEvent(guiActive = true, guiName = "#RF_HybridEngine_SwitchEngineMode")] // Switch Engine Mode
        public void SwitchEngine()
        {
            ConfigNode currentConfig = GetConfigByName(configuration);
            string nextConfiguration = configs[(configs.IndexOf(currentConfig) + 1) % configs.Count].GetValue("name");
            SetConfiguration(nextConfiguration);
            // TODO: Does Engine Ignitor get switched here?
        }

        override public void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            if (ActiveEngine == null)
                ActiveEngine = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType) as ModuleEngines;

            bool engineActive = ActiveEngine.getIgnitionState;
            ActiveEngine.EngineIgnited = false;

            base.SetConfiguration(newConfiguration, resetTechLevels);

            if (engineActive)
                ActiveEngine.Actions["ActivateAction"].Invoke(new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate));
        }
    }
}
