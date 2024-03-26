
using B9PartSwitch.PartSwitch.PartModifiers;

namespace RealFuels.Harmony
{
    public class ModuleFuelTanksHandler : PartModifierBase
    {
        public const string PART_ASPECT_LOCK = "ModuleFuelTanks";

        private readonly PartModule module;
        private readonly ConfigNode originalNode;
        private readonly ConfigNode dataNode;
        private readonly BaseEventDetails moduleDataChangedEventDetails;
        public ModuleFuelTanksHandler(PartModule module, ConfigNode originalNode, ConfigNode dataNode, BaseEventDetails moduleDataChangedEventDetails)
        {
            this.module = module;
            this.originalNode = originalNode;
            this.dataNode = dataNode;
            this.moduleDataChangedEventDetails = moduleDataChangedEventDetails;
        }

        public object PartAspectLock => PART_ASPECT_LOCK;
        public override string Description => "a part's ModuleFuelTanks";
        public override void DeactivateOnStartEditor() => Deactivate();
        public override void ActivateOnStartEditor() => Activate();
        public override void DeactivateOnSwitchEditor() => Deactivate();
        public override void ActivateOnSwitchEditor() => Activate();

        private void Activate() => ApplyNode(dataNode);
        private void Deactivate() => ApplyNode(originalNode);

        private void ApplyNode(ConfigNode sourceNode)
        {
            var evtDetails = new BaseEventDetails(BaseEventDetails.Sender.USER);
            evtDetails.Set<ConfigNode>("MFTNode", sourceNode);
            module.Events.Send("LoadMFTModuleFromConfigNode", evtDetails);
            module.Events.Send("ModuleDataChanged", moduleDataChangedEventDetails);
        }
    }
}
