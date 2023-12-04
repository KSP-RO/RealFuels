﻿using KSP.Localization;
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
    // KSP.UI.Screens.ResourceDisplay.CreateResourceList - uses RebuildInPlace as well as creating set.
    // ShipConstruct.UpdateResourceSets

    // prefix/postfix:
    // OnPartResourceFlowStateChange, OnPartResourceFlowModeChange, OnPartPriorityChange

    public class ShipResourceMap
    {
        public class ResourceData
        {
            // Crossfeed set lookups (also stores actual pressuresets)
            private Dictionary<Part, PressureSet<PrioritySet>> _partToSet = new Dictionary<Part, PressureSet<PrioritySet>>();
            private Dictionary<ResourceWrapper, PressureSet<PrioritySet>> _rwToPS = new Dictionary<ResourceWrapper, PressureSet<PrioritySet>>();
            private List<PressureSet<PrioritySet>> _crossfeedSets = new List<PressureSet<PrioritySet>>();

            // ship-wide pressuresets
            private PressureSet<PrioritySet> _shipWidePri = new PressureSet<PrioritySet>();
            private PressureSet<ResourceSet> _shipWide = new PressureSet<ResourceSet>();

            // inactive RWs
            private List<ResourceWrapper> _inactives = new List<ResourceWrapper>();

            // No-Flow resources
            // Note that there doesn't need to be a separate List to store these sets
            // because the part<->PartPressureSet relationship is one-to-one
            private Dictionary<Part, PartPressureSet> _partToPartPS = new Dictionary<Part, PartPressureSet>();
            private Dictionary<ResourceWrapper, PartPressureSet> _rwToPartPS = new Dictionary<ResourceWrapper, PartPressureSet>();

            public PressureSet<PrioritySet> GetOrCreateCrossfeedSet(Part part, HashSet<Part> crossfeedSet)
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

            public PartPressureSet GetOrCreatePartSet(Part part)
            {
                if (_partToPartPS.TryGetValue(part, out var ps))
                    return ps;

                ps = new PartPressureSet();

                return ps;
            }

            /// <summary>
            /// For use when constructing the ShipResourceMap
            /// </summary>
            /// <param name="rw"></param>
            /// <param name="part"></param>
            /// <param name="crossfeedSet"></param>
            public void Add(ResourceWrapper rw, Part part, HashSet<Part> crossfeedSet)
            {
                // Get or create the PS (links part plus all crossfeed parts)
                // but *also* add the rw to a lookup to this new set.
                var ps = GetOrCreateCrossfeedSet(part, crossfeedSet);
                _rwToPS[rw] = ps;

                // same for part PS
                var pps = GetOrCreatePartSet(part);
                _rwToPartPS[rw] = pps;

                if (rw.Flowing())
                {
                    _shipWide.Add(rw, false);
                    _shipWidePri.Add(rw, false);
                    ps.Add(rw, false);
                    pps.Add(rw);
                }
                else
                {
                    _inactives.Add(rw);
                }
            }

            /// <summary>
            /// For use when reconfiguring ModuleRFTanks
            /// </summary>
            /// <param name="rw"></param>
            /// <param name="part"></param>
            /// <returns></returns>
            public bool Add(ResourceWrapper rw, Part part)
            {
                if (!_partToSet.TryGetValue(part, out var ps) || !_partToPartPS.TryGetValue(part, out var pps))
                    return false;

                _rwToPS[rw] = ps;
                _rwToPartPS[rw] = pps;
                if (rw.Flowing())
                {
                    _shipWide.Add(rw, true);
                    _shipWidePri.Add(rw, true);
                    ps.Add(rw, true);
                    pps.Add(rw);
                }
                else
                {
                    _inactives.Add(rw);
                }
                return true;
            }

            public bool Remove(ResourceWrapper rw)
            {
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

            public void Recalc()
            {
                foreach (var ps in _crossfeedSets)
                    ps.Recalc();

                _shipWide.Recalc();
                _shipWidePri.Recalc();
            }

            public bool MakeActive(ResourceWrapper rw)
            {
                int idx = _inactives.IndexOf(rw);
                if (idx < 0)
                    return false;

                _inactives.RemoveAt(idx);
                _shipWide.Add(rw, true);
                _shipWidePri.Add(rw, true);
                // safe to not trygetvalue (I hope) because we know it was added to inactives
                _rwToPS[rw].Add(rw, true);
                _rwToPartPS[rw].Add(rw);
                return true;
            }

            public bool MakeInactive(ResourceWrapper rw)
            {
                if (_inactives.Contains(rw))
                    return false;

                if (!_shipWide.Remove(rw, true) || !_shipWidePri.Remove(rw, true))
                    return false;

                // safe to not trygetvalue (I hope) because we know it was added to shipwide.
                _rwToPS[rw].Remove(rw, true);
                _inactives.Add(rw);

                return true;
            }

            public void ChangePriority(ResourceWrapper rw, int oldPri)
            {
                _shipWidePri.ChangePriority(rw, oldPri);
                if (_rwToPS.TryGetValue(rw, out var ps))
                    ps.ChangePriority(rw, oldPri);
            }

            public void GetTotals(Part part, out double amount, out double maxAmount, float pressure, bool pulling, ResourceFlowMode mode)
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

            public void GetTotals(out double amount, out double maxAmount, float pressure, bool pulling)
            {
                _shipWide.GetTotals(out amount, out maxAmount, pressure);
                if (!pulling)
                    amount = maxAmount - amount;
            }

            public void GetTotals(Part part, out double amount, out double maxAmount, float pressure, bool pulling)
            {
                amount = 0d;
                maxAmount = 0d;
                if (!_partToSet.TryGetValue(part, out var ps))
                    return;

                ps.GetTotals(out amount, out maxAmount, pressure);
                if (!pulling)
                    amount = maxAmount - amount;
            }

            public void GetTotalsNoFlow(Part part, out double amount, out double maxAmount, float pressure, bool pulling)
            {
                amount = 0d;
                maxAmount = 0d;
                if (!_partToSet.TryGetValue(part, out var ps))
                    return;

                ps.GetTotals(out amount, out maxAmount, pressure);
                if (!pulling)
                    amount = maxAmount - amount;
            }

            public double Request(double demand, Part part, float pressure, ResourceFlowMode mode, bool simulate)
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

            public double Request(double demand, float pressure, bool usePri, bool simulate)
            {
                if (usePri)
                    return demand - _shipWidePri.Request(demand, pressure, simulate);

                return demand - _shipWide.Request(demand, pressure, simulate);
            }

            public double Request(double demand, Part part, float pressure, bool simulate)
            {
                if (!_partToSet.TryGetValue(part, out var ps))
                    return demand;

                return demand - ps.Request(demand, pressure, simulate);
            }

            public double RequestNoFlow(double demand, Part part, float pressure, bool simulate)
            {
                if (!_partToPartPS.TryGetValue(part, out var pps))
                    return demand;

                return demand - pps.Request(demand, pressure, simulate);
            }
        }

        private bool _isSim;
        public bool isSim => _isSim;
        private List<Part> _parts;
        private FastFlowGraph _graph;

        private Dictionary<int, ResourceData> _resources = new Dictionary<int, ResourceData>();
        public ResourceData Resource(int id)
        {
            if (!_resources.TryGetValue(id, out var cache))
            {
                cache = new ResourceData();
                _resources.Add(id, cache);
            }

            return cache;
        }

        private Dictionary<PartResource, ResourceWrapper> _resToWrapper = new Dictionary<PartResource, ResourceWrapper>();
        private Vessel _vessel;
        private ShipConstruct _ship;

        public ShipResourceMap(List<Part> parts, bool isSim)
        {
            _isSim = isSim;
            _parts = parts;

            _graph = new FastFlowGraph(parts);
        }

        public ShipResourceMap(Vessel v) : this(v.parts, false)
        {
            _vessel = v;
            v.crossfeedSets.Clear();
            v.simulationCrossfeedSets.Clear();

            var vs = new PartSetRF(v);
            vs.SetResCache(this);
            v.resourcePartSet = vs;

            var simCache = new ShipResourceMap(v.parts, true);
            vs = new PartSetRF(v);
            vs.SetResCache(simCache);
            v.simulationResourcePartSet = vs;

            foreach (var hs in _graph.sets)
            {
                var ps = new PartSetRF(hs);
                ps.SetResCache(this);
                var simPS = new PartSetRF(hs);
                simPS.SetResCache(simCache);
                v.crossfeedSets.Add(ps);
                v.simulationCrossfeedSets.Add(simPS);
                foreach (var p in hs)
                {
                    p.crossfeedPartSet = ps;
                    p.simulationCrossfeedPartSet = simPS;
                }
            }
        }

        public ShipResourceMap(ShipConstruct ship) : this(ship.parts, true)
        {
            _ship = ship;

            var vs = new PartSetRF(ship);
            vs.SetResCache(this);
            ship.resourcePartSet = vs;

            foreach (var hs in _graph.sets)
            {
                var ps = new PartSetRF(hs);
                ps.SetResCache(this);
                foreach (var p in hs)
                {
                    p.simulationCrossfeedPartSet = ps;
                }
            }
        }

        public void Rebuild()
        {
            _resources.Clear();
            _resToWrapper.Clear();
            Build();

        }

        private void Build()
        {
            var seenTankSets = new HashSet<IReadOnlyList<LogicalTankSet>>();

            foreach (var crossfeedParts in _graph.sets)
            {
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
                                        var cache = Resource(tank.resID);
                                        tank.LinkCache(cache);
                                        cache.Add(tank, part, crossfeedParts);
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

                        var cache = Resource(res.info.id);
                        var rw = new PartResourceWrapper(res, cache);
                        _resToWrapper[res] = rw;
                        cache.Add(rw, part, crossfeedParts);
                    }
                }

                foreach (var rc in _resources.Values)
                    rc.Recalc();
            }
        }
    }

    public class PartSetRF : PartSet
    {
        private ShipResourceMap _cache;
        public ShipResourceMap Cache => _cache;

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

        public void SetResCache(ShipResourceMap cache)
        {
            _cache = cache;
            simulationSet = _cache.isSim;
        }

        public void SetSim(bool sim) { simulationSet = sim; }

        public override void GetConnectedResourceTotals(int id, out double amount, out double maxAmount, bool pulling, bool simulate)
        {
            if (vesselWide)
                _cache.Resource(id).GetTotals(out amount, out maxAmount, 0f, pulling);
            else
                _cache.Resource(id).GetTotals(_examplePart, out amount, out maxAmount, 0f, pulling);
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
            _cache.Rebuild();
        }

        public override double RequestResource(Part part, int id, double demand, bool usePri)
        {
            return RequestResource(part, id, demand, usePri, false);
        }

        public override double RequestResource(Part part, int id, double demand, bool usePri, bool simulate)
        {
            var res = _cache.Resource(id);
            if (vesselWide)
                return res.Request(demand, 0f, usePri, simulate);
            else
                return res.Request(demand, part, 0f, simulate);
        }
    }
}
