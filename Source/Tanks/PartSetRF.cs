using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class PartSetRF : PartSet
    {
        private static Dictionary<PartResource, ResourceWrapper> _resToWrapper = new Dictionary<PartResource, ResourceWrapper>();

        public PartSetRF(HashSet<Part> parts) : base(parts) { UnhookEvents(); }

        public PartSetRF(Vessel v) : base(v) { UnhookEvents(); }

        public PartSetRF(PartSet set) : base(set) { UnhookEvents(); }

        public PartSetRF(ShipConstruct shipRef) : base(shipRef) { UnhookEvents(); }

        public PartSetRF(ShipConstruct shipRef, HashSet<Part> newParts) : base(shipRef, newParts) { UnhookEvents(); }

        protected void UnhookEvents()
        {
            GameEvents.onPartResourceFlowStateChange.Remove(OnFlowStateChange);
            GameEvents.onPartResourceFlowModeChange.Remove(OnFlowModeChange);
        }

        // Transpile to change new() calls:
        // internal static void BuildPartSets(List<Part> parts, Vessel v, PartBuildSetOptions buildoptions, SCCFlowGraph sccGraph)
        // - also prefix to clear vessel's crossfeed and sim crossfeed partsets
        // internal static HashSet<PartSet> BuildPartSimulationSets(List<Part> parts, SCCFlowGraph sccGraph)
        // Vessel.UpdateResourceSets
        // KSP.UI.Screens.ResourceDisplay.CreateResourceList
        // ShipConstruct.UpdateResourceSets

        // prefix/postfix:
        // OnPartResourceFlowStateChange, OnPartResourceFlowModeChange, OnPartPriorityChange

        public override void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, bool pulling, bool simulate)
        {
            GetConnectedResourceTotals(id, out amount, out maxAmount, 0f, pulling, simulate);
        }

        public void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, float pressure, bool pulling, bool simulate)
        {
            ResourcePrioritySetRF set = GetOrCreateList(id, pulling, simulate) as ResourcePrioritySetRF;
            if (set == null)
            {
                maxAmount = 0d;
                amount = 0d;
                return;
            }
            set.GetTotals(out amount, out maxAmount, pressure);
            if (!pulling)
                amount = maxAmount - amount;
        }

        public override void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, double threshold, bool pulling, bool simulate)
        {
            GetConnectedResourceTotals(id, out amount, out maxAmount, 0f, threshold, pulling, simulate);
        }

        public void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, float pressure, double threshold, bool pulling, bool simulate)
        {
            maxAmount = 0d;
            amount = 0d;
            ResourcePrioritySetRF set = GetOrCreateList(id, pulling, simulate) as ResourcePrioritySetRF;
            if (set == null)
            {
                return;
            }
            set.GetTotals(out double amt, out double max, pressure);
            if (pulling)
            {
                double thresh = max * threshold;
                double invThresh = max * (1d - thresh);
                if (invThresh < 1E-09)
                    return;
                
                maxAmount = max;

                if (amt - thresh < 1E-09)
                    return;

                amount = amt;
            }
            else
            {
                double thresh = max * threshold;
                if (thresh < 1E-09)
                    return;

                maxAmount = max;

                double headroom = thresh - amt;
                if (headroom < 1E-09)
                    return;

                amount = headroom;
            }
        }

        public override double RequestResource(Part part, int id, double demand, bool usePri, bool simulate)
        {
            return RequestResource(id, demand, 0f, usePri, simulate);
        }

        public double RequestResource(int id, double demand, float pressure, bool usePri, bool simulate)
        {
            bool pulling = demand > 0.0;
            ResourcePrioritySetRF set = GetOrCreateList(id, pulling, simulate) as ResourcePrioritySetRF;
            if (set == null)
            {
                Debug.Log("PartSet]: Unable to process resource request. PartSet has not been setup correctly.");
                return 0d;
            }
            return set.Request(demand, pressure, usePri, simulate);
        }

        protected virtual ResourcePrioritySet BuildResListOverride(int id, bool pull, bool simulate)
        {
            simulationSet = simulate;
            IEnumerator<Part> enumerator = null;
            if (simulate && targetParts != null && targetParts.Count > 0)
            {
                enumerator = targetParts.GetEnumerator();
            }
            else if (vesselWide)
            {
                if (vessel != null)
                {
                    enumerator = vessel.parts.GetEnumerator();
                }
                else if (ship != null)
                {
                    enumerator = ship.parts.GetEnumerator();
                }
            }
            else
            {
                enumerator = targetParts.GetEnumerator();
            }

            ResourcePrioritySetRF set = new ResourcePrioritySetRF(pull);
            if (enumerator == null)
                return set;

            while (enumerator.MoveNext())
            {
                Part current = enumerator.Current;
                ResourceWrapper rw = null;
                if (current.FindModuleImplementing<ModuleRFTank>() is var mrft && mrft.IsMain)
                {
                    var tSet = simulate ? mrft.tankSetsSim : mrft.tankSet;
                    foreach (var tg in tSet.groups)
                        if (tg.tanks.TryGetValue(id, out var lt))
                            rw = lt;
                }
                else
                {
                    PartResource res = (simulate ? current.SimulationResources.Get(id) : current.Resources.Get(id));
                    if (res != null)
                    {
                        // TODO: check for bad item in cache?
                        if (!_resToWrapper.TryGetValue(res, out rw))
                        {
                            rw = new PartResourceWrapper(res);
                            _resToWrapper[res] = rw;
                        }
                    }
                }
                if (rw != null)
                    set.Add(rw);
            }
            enumerator.Dispose();

            return set;
        }

        protected void ClearSets()
        {
            foreach (var set in pullList.Values)
                (set as ResourcePrioritySetRF).Clear();
            foreach (var set in pushList.Values)
                (set as ResourcePrioritySetRF).Clear();
        }

        public override void RebuildInPlace()
        {
            ClearSets();
            base.RebuildInPlace();
        }

        public override void RebuildParts(HashSet<Part> newParts)
        {
            ClearSets();
            base.RebuildParts(newParts);
        }

        public override void RebuildVessel(ShipConstruct newShip)
        {
            ClearSets();
            base.RebuildVessel(newShip);
        }

        public override void RebuildVessel(ShipConstruct newShip, HashSet<Part> newParts)
        {
            ClearSets();
            base.RebuildVessel(newShip, newParts);
        }

        public override void RebuildVessel(Vessel newVessel)
        {
            ClearSets();
            base.RebuildVessel(newVessel);
        }
    }
}
