using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels.Tanks
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorPartSetMaintainer : MonoBehaviour
    {
        public static EditorPartSetMaintainer Instance { get; private set; }

        private bool _updateScheduled = false;

        protected void Awake()
        {
            if (Instance != null)
                Destroy(Instance);

            Instance = this;
        }

        protected void Start()
        {
            GameEvents.onEditorShipModified.Add(UpdateCrossfeedSets);
            GameEvents.onPartAttach.Add(OnPartAttach);
            GameEvents.onPartRemove.Add(OnPartRemove);
        }

        protected void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(UpdateCrossfeedSets);
            GameEvents.onPartAttach.Remove(OnPartAttach);
            GameEvents.onPartRemove.Remove(OnPartRemove);

            if (Instance == this)
                Instance = null;
        }

        public void ScheduleUsedBySetsUpdate()
        {
            if (!_updateScheduled)
            {
                StartCoroutine(UsedBySetsUpdateRoutine());
                _updateScheduled = true;
            }
        }

        private void UpdateCrossfeedSets(ShipConstruct ship)
        {
            PartSet.BuildPartSets(ship.parts, null);
        }

        // Only trigger updates if a part in the tree that was added/removed is a fuel consumer
        // Note that part packed status will be false when a ShipContruct is being loaded.
        // We don't want to run the checks and scheduling in this case.
        // Also note that we do not run updates on ghosted parts.
        private void OnPartAttach(GameEvents.HostTargetAction<Part, Part> hostTarget)
        {
            // Attaching: host is the incoming part
            if (hostTarget.target?.packed == true)
                ScheduleUpdateIfNeeded(hostTarget, isAttachEvent: true);
        }

        private void OnPartRemove(GameEvents.HostTargetAction<Part, Part> hostTarget)
        {
            // Removing: target is the detaching part
            if (hostTarget.target.localRoot == EditorLogic.RootPart)
                ScheduleUpdateIfNeeded(hostTarget, isAttachEvent: false);
        }

        private void ScheduleUpdateIfNeeded(GameEvents.HostTargetAction<Part, Part> hostTarget, bool isAttachEvent)
        {
            if (PartContainsEngineOrRCS(hostTarget.host, testChildren: isAttachEvent) ||
                PartContainsEngineOrRCS(hostTarget.target, testChildren: !isAttachEvent))
            {
                ScheduleUsedBySetsUpdate();
            }
        }

        private static bool PartContainsEngineOrRCS(Part p, bool testChildren = false)
        {
            if (p == null) return false;
            bool result = p.FindModuleImplementing<ModuleEngines>() || p.FindModuleImplementing<ModuleRCS>();
            if (testChildren && !result)
                foreach (Part p2 in p.children)
                    result |= PartContainsEngineOrRCS(p2, testChildren);
            return result;
        }

        private IEnumerator UsedBySetsUpdateRoutine()
        {
            yield return new WaitForEndOfFrame();
            _updateScheduled = false;
            UpdateUsedBySets();
        }

        private static void UpdateUsedBySets()
        {
            Debug.Log("[RF] UpdateUsedBySets start");
            if (EditorLogic.fetch.ship == null)
                return;

            var thrusterModules = new List<PartModule>();
            var tankModules = new List<ModuleFuelTanks>();
            List<Part> parts = EditorLogic.fetch.ship.parts;
            foreach (Part p in parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    if (m is ModuleEngines || m is ModuleRCS)
                        thrusterModules.Add(m);
                    else if (m is ModuleFuelTanks mft)
                        tankModules.Add(mft);
                }
            }

            foreach (ModuleFuelTanks mft in tankModules)
            {
                mft.usedBy.Clear();
                mft.usedByTanks.Clear();

                foreach (PartModule m in thrusterModules)
                {
                    FuelInfo f = null;
                    if (m is ModuleEngines me)
                        f = new FuelInfo(me.propellants, mft, m);
                    else if (m is ModuleRCS mRcs)
                        f = new FuelInfo(mRcs.propellants, mft, m);

                    if (f?.valid == true)
                        mft.UpdateFuelInfo(f, m);
                }

                mft.UpdateTweakableButtonsDelegate();
            }
        }
    }
}
