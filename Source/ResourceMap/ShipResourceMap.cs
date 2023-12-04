using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    // Transpile to change new() calls:
    // internal static void BuildPartSets(List<Part> parts, Vessel v, PartBuildSetOptions buildoptions, SCCFlowGraph sccGraph)
    // - also prefix to clear vessel's crossfeed and sim crossfeed partsets
    // internal static HashSet<PartSet> BuildPartSimulationSets(List<Part> parts, SCCFlowGraph sccGraph)
    // Vessel.UpdateResourceSets
    // Also load/unload (swap between part and proto
    // KSP.UI.Screens.ResourceDisplay.CreateResourceList - uses RebuildInPlace as well as creating set.
    // ShipConstruct.UpdateResourceSets

    // prefix/postfix:
    // OnPartResourceFlowStateChange, OnPartResourceFlowModeChange, OnPartPriorityChange

    public class ShipResourceMap
    {
        public class ResourceData
        {
            // ship-wide pressuresets
            protected PressureSet<PrioritySet> _shipWidePri = new PressureSet<PrioritySet>();
            protected PressureSet<ResourceSet> _shipWide = new PressureSet<ResourceSet>();

            // inactive RWs
            protected List<ResourceWrapper> _inactives = new List<ResourceWrapper>();

            /// <summary>
            /// For use when constructing the ShipResourceMap
            /// </summary>
            /// <param name="rw"></param>
            /// <param name="part"></param>
            /// <param name="crossfeedSet"></param>
            public void Add(ResourceWrapper rw)
            {
                if (rw.Flowing())
                {
                    _shipWide.Add(rw, false);
                    _shipWidePri.Add(rw, false);
                }
                else
                {
                    _inactives.Add(rw);
                }
            }

            public virtual bool Remove(ResourceWrapper rw)
            {
                if (_inactives.Remove(rw))
                    return true;

                bool foundSW = _shipWide.Remove(rw, true);
                bool foundSWP = _shipWidePri.Remove(rw, true);
                return foundSW && foundSWP;
            }

            public virtual void Recalc()
            {
                _shipWide.Recalc();
                _shipWidePri.Recalc();
            }

            public virtual bool MakeActive(ResourceWrapper rw)
            {
                int idx = _inactives.IndexOf(rw);
                if (idx < 0)
                    return false;

                _inactives.FastRemoveAt(idx);

                _shipWide.Add(rw, true);
                _shipWidePri.Add(rw, true);
                return true;
            }

            public virtual bool MakeInactive(ResourceWrapper rw)
            {
                if (_inactives.Contains(rw))
                    return false;

                if (!_shipWide.Remove(rw, true) || !_shipWidePri.Remove(rw, true))
                    return false;

                _inactives.Add(rw);

                return true;
            }

            public virtual void ChangePriority(ResourceWrapper rw, int oldPri)
            {
                _shipWidePri.ChangePriority(rw, oldPri);
            }

            public void GetTotals(out double amount, out double maxAmount, float pressure, bool pulling)
            {
                _shipWide.GetTotals(out amount, out maxAmount, pressure);
                if (!pulling)
                    amount = maxAmount - amount;
            }

            public virtual void GetTotals(object part, out double amount, out double maxAmount, float pressure, bool pulling, ResourceFlowMode mode) =>
                GetTotals(out amount, out maxAmount, pressure, pulling);

            public virtual void GetTotals(object part, out double amount, out double maxAmount, float pressure, bool pulling) =>
                GetTotals(out amount, out maxAmount, pressure, pulling);

            public virtual void GetTotalsNoFlow(object part, out double amount, out double maxAmount, float pressure, bool pulling) =>
                GetTotals(out amount, out maxAmount, pressure, pulling);

            public double Request(double demand, float pressure, bool usePri, bool simulate)
            {
                if (usePri)
                    return demand - _shipWidePri.Request(demand, pressure, simulate);

                return demand - _shipWide.Request(demand, pressure, simulate);
            }

            public virtual double Request(double demand, object part, float pressure, ResourceFlowMode mode, bool simulate) =>
                Request(demand, pressure, mode != ResourceFlowMode.ALL_VESSEL && mode != ResourceFlowMode.ALL_VESSEL_BALANCE, simulate);

            public virtual double Request(double demand, object part, float pressure, bool simulate) =>
                Request(demand, pressure, true, simulate);

            public virtual double RequestNoFlow(double demand, object part, float pressure, bool simulate) =>
                Request(demand, pressure, true, simulate);
        }

        public class CrossfeedResourceData : ResourceData
        {
            // Crossfeed set lookups (also stores actual pressuresets)
            protected Dictionary<object, PressureSet<PrioritySet>> _partToSet = new Dictionary<object, PressureSet<PrioritySet>>();
            protected Dictionary<ResourceWrapper, PressureSet<PrioritySet>> _rwToPS = new Dictionary<ResourceWrapper, PressureSet<PrioritySet>>();
            protected List<PressureSet<PrioritySet>> _crossfeedSets = new List<PressureSet<PrioritySet>>();

            // No-Flow resources
            // Note that there doesn't need to be a separate List to store these sets
            // because the part<->PartPressureSet relationship is one-to-one
            protected Dictionary<object, PartPressureSet> _partToPartPS = new Dictionary<object, PartPressureSet>();
            protected Dictionary<ResourceWrapper, PartPressureSet> _rwToPartPS = new Dictionary<ResourceWrapper, PartPressureSet>();

            public PressureSet<PrioritySet> GetOrCreateCrossfeedSet(object part, HashSet<object> crossfeedSet)
            {
                if (_partToSet.TryGetValue(part, out var ps))
                    return ps;

                // If we're making a new PressureSet, link *all*
                // parts in this crossfeed set to it
                ps = new PressureSet<PrioritySet>();
                _crossfeedSets.Add(ps);
                foreach (var p in crossfeedSet)
                    _partToSet.Add(p, ps);

                return ps;
            }

            public PartPressureSet GetOrCreatePartSet(object part)
            {
                if (_partToPartPS.TryGetValue(part, out var ps))
                    return ps;

                ps = new PartPressureSet();
                _partToPartPS.Add(part, ps);

                return ps;
            }

            /// <summary>
            /// For use when constructing the ShipResourceMap
            /// </summary>
            /// <param name="rw"></param>
            /// <param name="part"></param>
            /// <param name="crossfeedSet"></param>
            public void Add(ResourceWrapper rw, object part, HashSet<object> crossfeedSet)
            {
                // Get or create the PS (links part plus all crossfeed parts)
                // but *also* add the rw to a lookup to this new set.
                var ps = GetOrCreateCrossfeedSet(part, crossfeedSet);
                _rwToPS[rw] = ps;

                // same for part PS
                var pps = GetOrCreatePartSet(part);
                _rwToPartPS[rw] = pps;

                if(rw.Flowing())
                {
                    ps.Add(rw, false);
                    pps.Add(rw);
                }
            }

            /// <summary>
            /// For use when reconfiguring ModuleRFTanks
            /// </summary>
            /// <param name="rw"></param>
            /// <param name="part"></param>
            /// <returns></returns>
            public bool Add(ResourceWrapper rw, object part)
            {
                if (!_partToSet.TryGetValue(part, out var ps) || !_partToPartPS.TryGetValue(part, out var pps))
                    return false;

                _rwToPS[rw] = ps;
                _rwToPartPS[rw] = pps;

                Add(rw);
                if (rw.Flowing())
                {
                    ps.Add(rw, true);
                    pps.Add(rw);
                }
                return true;
            }

            public override bool Remove(ResourceWrapper rw)
            {
                // We don't call base, because we need to check presence in _inactives ourselves
                if (!_rwToPS.TryGetValue(rw, out var ps) || !_rwToPartPS.TryGetValue(rw, out var pps))
                    return false;

                _rwToPS.Remove(rw);
                _rwToPartPS.Remove(rw);

                if (_inactives.Remove(rw))
                    return true;

                bool foundSW = _shipWide.Remove(rw, true);
                bool foundSWP = _shipWidePri.Remove(rw, true);
                bool foundPS = ps.Remove(rw, true);
                bool foundPPS = pps.Remove(rw);
                return foundSW && foundSWP && foundPS && foundPPS;
            }

            public override void Recalc()
            {
                base.Recalc();

                foreach (var ps in _crossfeedSets)
                    ps.Recalc();
            }

            public override bool MakeActive(ResourceWrapper rw)
            {
                if (!base.MakeActive(rw))
                    return false;

                // safe to not trygetvalue (I hope) because we know it was added to inactives
                _rwToPS[rw].Add(rw, true);
                _rwToPartPS[rw].Add(rw);
                return true;
            }

            public override bool MakeInactive(ResourceWrapper rw)
            {
                if (!base.MakeInactive(rw))
                    return false;

                // safe to not trygetvalue (I hope) because we know it was added to shipwide.
                _rwToPS[rw].Remove(rw, true);
                _inactives.Add(rw);

                return true;
            }

            public override void ChangePriority(ResourceWrapper rw, int oldPri)
            {
                base.ChangePriority(rw, oldPri);

                if (_rwToPS.TryGetValue(rw, out var ps))
                    ps.ChangePriority(rw, oldPri);
            }

            public override void GetTotals(object part, out double amount, out double maxAmount, float pressure, bool pulling, ResourceFlowMode mode)
            {
                switch (mode)
                {
                    case ResourceFlowMode.NO_FLOW:
                        GetTotalsNoFlow(part, out amount, out maxAmount, pressure, pulling);
                        return;

                    case ResourceFlowMode.ALL_VESSEL:
                    case ResourceFlowMode.ALL_VESSEL_BALANCE:
                    case ResourceFlowMode.STAGE_PRIORITY_FLOW:
                    case ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE:
                        GetTotals(out amount, out maxAmount, pressure, pulling);
                        return;

                    default:
                        GetTotals(part, out amount, out maxAmount, pressure, pulling);
                        return;
                }
            }

            public override void GetTotals(object part, out double amount, out double maxAmount, float pressure, bool pulling)
            {
                amount = 0d;
                maxAmount = 0d;
                if (!_partToSet.TryGetValue(part, out var ps))
                    return;

                ps.GetTotals(out amount, out maxAmount, pressure);
                if (!pulling)
                    amount = maxAmount - amount;
            }

            public override void GetTotalsNoFlow(object part, out double amount, out double maxAmount, float pressure, bool pulling)
            {
                amount = 0d;
                maxAmount = 0d;
                if (!_partToSet.TryGetValue(part, out var ps))
                    return;

                ps.GetTotals(out amount, out maxAmount, pressure);
                if (!pulling)
                    amount = maxAmount - amount;
            }

            public override double Request(double demand, object part, float pressure, ResourceFlowMode mode, bool simulate)
            {
                switch (mode)
                {
                    case ResourceFlowMode.NO_FLOW:
                        return RequestNoFlow(demand, part, pressure, simulate);

                    case ResourceFlowMode.ALL_VESSEL:
                    case ResourceFlowMode.ALL_VESSEL_BALANCE:
                        return Request(demand, pressure, false, simulate);

                    case ResourceFlowMode.STAGE_PRIORITY_FLOW:
                    case ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE:
                        return Request(demand, pressure, true, simulate);

                    default:
                        return Request(demand, part, pressure, simulate);
                }
            }

            public override double Request(double demand, object part, float pressure, bool simulate)
            {
                if (!_partToSet.TryGetValue(part, out var ps))
                    return demand;

                return demand - ps.Request(demand, pressure, simulate);
            }

            public override double RequestNoFlow(double demand, object part, float pressure, bool simulate)
            {
                if (!_partToPartPS.TryGetValue(part, out var pps))
                    return demand;

                return demand - pps.Request(demand, pressure, simulate);
            }
        }

        private bool _isSim;
        public bool isSim => _isSim;

        private bool _isLive = true;
        public bool isLive => _isLive;


        private List<Part> _parts;
        private List<ProtoPartSnapshot> _protos;
        private FastFlowGraphP _graph;
        // TODO: Support crossfeed snapshot maps

        private Dictionary<int, ResourceData> _resources = new Dictionary<int, ResourceData>();
        public ResourceData Resource(int id)
        {
            if (!_resources.TryGetValue(id, out var data))
            {
                // TODO: this can eventually just always be CrossfeedResourceData?
                data = _isLive ? new CrossfeedResourceData() : new ResourceData();
                _resources.Add(id, data);
            }

            return data;
        }
        public CrossfeedResourceData CrossfeedResource(int id) => Resource(id) as CrossfeedResourceData;

        private Vessel _vessel;
        private ShipConstruct _ship;

        public ShipResourceMap(List<Part> parts, bool isSim)
        {
            _isSim = isSim;
            _parts = parts;
            Init(parts);
            BuildFromParts();
        }

        public ShipResourceMap(List<ProtoPartSnapshot> parts, bool isSim)
        {
            _isSim = isSim;
            _protos = parts;
            Init(parts);
            BuildFromSnapshots();
        }

        public ShipResourceMap(Vessel v)
        {
            _isSim = false;
            _vessel = v;
            v.crossfeedSets.Clear();
            v.simulationCrossfeedSets.Clear();

            var vs = new PartSetRF(v);
            vs.SetResMap(this);
            v.resourcePartSet = vs;

            if (!v.loaded)
            {
                Init(v.protoVessel.protoPartSnapshots);
                BuildFromSnapshots();
                return;
            }

            InitVessel();
            BuildFromParts();
        }

        public ShipResourceMap(ShipConstruct ship)
        {
            _ship = ship;
            _isSim = true;

            var vs = new PartSetRF(ship);
            vs.SetResMap(this);
            ship.resourcePartSet = vs;

            InitShip();
            BuildFromParts();
        }

        public void Rebuild()
        {
            _resources.Clear();
            if (_parts.Count > 0)
            {
                if (_vessel != null && !_vessel.loaded)
                {
                    _parts = null;
                    _vessel.simulationResourcePartSet = _vessel.resourcePartSet; // or maybe null?
                    _vessel.crossfeedSets.Clear();
                    _vessel.simulationCrossfeedSets.Clear();
                    Init(_vessel.protoVessel.protoPartSnapshots);
                    BuildFromSnapshots();
                }
                else
                {
                    BuildFromParts();
                }
            }
            else
            {
                if (_vessel != null && _vessel.loaded)
                {
                    _protos = null;
                    _vessel.crossfeedSets.Clear();
                    InitVessel();
                    BuildFromParts();
                }
                else
                {
                    BuildFromSnapshots();
                }
            }
        }

        private void Init(List<Part> parts)
        {
            _graph = new FastFlowGraphP();
            _graph.Create(parts);
        }

        private void InitVessel()
        {
            Init(_vessel.parts);
            var simMap = new ShipResourceMap(_vessel.parts, true);
            var vs = new PartSetRF(_vessel);
            vs.SetResMap(simMap);
            _vessel.simulationResourcePartSet = vs;

            foreach (var hs in _graph.sets)
            {
                var ps = new PartSetRF(hs);
                ps.SetResMap(this);
                var simPS = new PartSetRF(hs);
                simPS.SetResMap(simMap);
                _vessel.crossfeedSets.Add(ps);
                _vessel.simulationCrossfeedSets.Add(simPS);
                foreach (var p in hs)
                {
                    p.crossfeedPartSet = ps;
                    p.simulationCrossfeedPartSet = simPS;
                }
            }
        }

        private void InitShip()
        {
            Init(_ship.parts);

            foreach (var hs in _graph.sets)
            {
                var ps = new PartSetRF(hs);
                ps.SetResMap(this);
                foreach (var p in hs)
                {
                    p.simulationCrossfeedPartSet = ps;
                }
            }
        }


        private void Init(List<ProtoPartSnapshot> parts)
        {
            // TODO: for now, don't bother -- we're only going to use shipwide pressuresets
            //_graph = new FastFlowGraphS();
            //_graph.Create(parts);
        }

        private static readonly HashSet<object> _ObjectSet = new HashSet<object>();
        private void BuildFromParts()
        {
            var seenTankSets = new HashSet<IReadOnlyList<LogicalTankSet>>();

            foreach (var crossfeedParts in _graph.sets)
            {
                foreach (var part in crossfeedParts)
                    _ObjectSet.Add(part);

                foreach (var part in crossfeedParts)
                {
                    ModuleRFTank mrft = null;
                    if (part.FindModuleImplementing<ModuleRFTank>() is var mrft_)
                    {
                        mrft = mrft_;
                        // this will get the main module's tankset.
                        var tSets = _isSim ? mrft.tankSetsSim : mrft.tankSets;
                        // If we haven't seen this tankset yet, process it.
                        if (!seenTankSets.Contains(tSets))
                        {
                            seenTankSets.Add(tSets);
                            foreach (var ts in tSets)
                            {
                                foreach (var group in ts.groups)
                                {
                                    foreach (var tank in group.tanks.Values)
                                    {
                                        var data = CrossfeedResource(tank.resID);
                                        tank.LinkCache(data);
                                        data.Add(tank, part, _ObjectSet);
                                    }
                                }
                            }
                        }
                    }

                    // MRFTs can have unmanaged resources, so this can't be an else.
                    var resList = _isSim ? part.SimulationResources : part.Resources;
                    foreach (var res in resList.dict.Values)
                    {
                        if (mrft != null && mrft.IsResourceManaged(res.info.id))
                            continue;

                        var data = CrossfeedResource(res.info.id);
                        var rw = new PartResourceWrapper(res, data);
                        data.Add(rw, part, _ObjectSet);
                    }
                }
                _ObjectSet.Clear();

                foreach (var rc in _resources.Values)
                    rc.Recalc();
            }
        }

        private void BuildFromSnapshots()
        {
            foreach (var p in _protos)
            {
                int partPri = (p.partInfo.partPrefab.resourcePriorityUseParentInverseStage ? p.parent.inverseStageIndex : p.inverseStageIndex) * 10 + p.resourcePriorityOffset;
                foreach (var res in p.resources)
                {
                    var data = Resource(res.definition.id);
                    var rw = new ResourceSnapshotWrapper(res, partPri, data);
                    data.Add(rw);
                }
            }
        }
    }

    public class PartSetRF : PartSet
    {
        private ShipResourceMap _map;
        public ShipResourceMap Map => _map;

        private Part _examplePart;

        public PartSetRF(HashSet<Part> parts) : base(parts) { UnhookEvents(); SetExamplePart(); }

        public PartSetRF(Vessel v) : base(v) { UnhookEvents(); }

        public PartSetRF(PartSet set) : base(set) { UnhookEvents(); if (!vesselWide) SetExamplePart(); }

        public PartSetRF(ShipConstruct shipRef) : base(shipRef) { UnhookEvents(); }

        public PartSetRF(ShipConstruct shipRef, HashSet<Part> newParts) : base(shipRef, newParts) { UnhookEvents(); }

        private void UnhookEvents()
        {
            GameEvents.onPartResourceFlowStateChange.Remove(OnFlowStateChange);
            GameEvents.onPartResourceFlowModeChange.Remove(OnFlowModeChange);
        }

        private void SetExamplePart()
        {
            var itr = targetParts.GetEnumerator();
            itr.MoveNext();
            _examplePart = itr.Current;
        }

        public void SetResMap(ShipResourceMap cache)
        {
            _map = cache;
            simulationSet = _map.isSim;
        }

        public void SetSim(bool sim) { simulationSet = sim; }

        public override void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, bool pulling, bool simulate)
        {
            if (vesselWide)
                _map.Resource(id).GetTotals(out amount, out maxAmount, 0f, pulling);
            else
                _map.Resource(id).GetTotals(_examplePart, out amount, out maxAmount, 0f, pulling);
        }

        public override void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, bool pulling)
        {
            GetConnectedResourceTotals(id, out amount, out maxAmount, pulling, false);
        }

        public override void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, double threshold, bool pulling)
        {
            GetConnectedResourceTotals(id, out amount, out maxAmount, pulling, false);
        }

        public override void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, double threshold, bool pulling, bool simulate)
        {
            GetConnectedResourceTotals(id, out amount, out maxAmount, pulling, false);
        }

        public override void RebuildInPlace()
        {
            _map.Rebuild();
        }

        public override double RequestResource(Part part, int id, double demand, bool usePri)
        {
            return RequestResource(part, id, demand, usePri, false);
        }

        public override double RequestResource(Part part, int id, double demand, bool usePri, bool simulate)
        {
            if (vesselWide)
                return _map.Resource(id).Request(demand, 0f, usePri, simulate);
            else
                return _map.Resource(id).Request(demand, part, 0f, simulate);
        }
    }
}
