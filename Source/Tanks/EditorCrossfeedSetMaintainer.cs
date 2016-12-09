using UnityEngine;

namespace RealFuels.Tanks
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorCrossfeedSetMaintainer : MonoBehaviour
    {
        public void Start()
        {
            GameEvents.onEditorShipModified.Add(UpdateCrossfeedSets);
        }

        public void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(UpdateCrossfeedSets);
        }
        
        private void UpdateCrossfeedSets(ShipConstruct ship)
        {
            PartSet.BuildPartSets(ship.parts, null);
        }
    }
}
