﻿using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class ModuleRFTank : PartModule
    {
        private ModuleRFTank _rootModule = null;
        public bool IsMain => _rootModule == null;
        private HashSet<ModuleRFTank> _tanks = null;
        private HashSet<Part> _parts = null;
        private bool _eventsEditor = false;
        private bool _eventsFlight = false;
        private static bool _InEvent = false;
        [KSPField(isPersistant = true)]
        public LogicalTankSet _tankSet = new LogicalTankSet();
        // TODO: handle sim reset/setup (clone)
        private LogicalTankSet _tankSetSim = new LogicalTankSet();
        public LogicalTankSet tankSet => _rootModule == null ? _tankSet : _rootModule._tankSet;
        public LogicalTankSet tankSetsSim => _rootModule == null ? _tankSetSim : _rootModule._tankSetSim;

        private void HookEventsEditor()
        {
            _eventsEditor = true;
            GameEvents.onPartAttach.Add(OnPartAttach);
            GameEvents.onPartRemove.Add(OnPartRemove);
            GameEvents.onPartResourceFlowStateChange.Add(OnFlowStateChange);
            GameEvents.onPartResourceFlowModeChange.Add(OnFlowModeChange);
        }

        private void HookEventsFlight()
        {
            GameEvents.onPartResourceFlowStateChange.Add(OnFlowStateChange);
            GameEvents.onPartResourceFlowModeChange.Add(OnFlowModeChange);
        }

        public override void OnStartFinished(StartState state)
        {
            _rootModule = null;
            _eventsEditor = false;
            _eventsFlight = false;
            
            // Just in case.
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                return;

            // Only hook attach/remove in editor; in flight we assume
            // you can't change tanks geometry...
            if ((state & StartState.Editor) != 0)
            {
                HookEventsEditor();
            }
            else // in flight, find tanks already
            {
                FindTanks();
            }
        }

        public void OnDestroy()
        {
            if (_eventsEditor || _eventsFlight)
            {
                GameEvents.onPartResourceFlowStateChange.Remove(OnFlowStateChange);
                GameEvents.onPartResourceFlowModeChange.Remove(OnFlowModeChange);
            }

            if (_eventsEditor)
            {
                GameEvents.onPartAttach.Remove(OnPartAttach);
                GameEvents.onPartRemove.Remove(OnPartRemove);
            }
            if (_eventsFlight)
            {
            }
        }

        private void OnPartAttach(GameEvents.HostTargetAction<Part, Part> data)
        {
            
        }

        private void OnPartRemove(GameEvents.HostTargetAction<Part, Part> data)
        {
            Part part = data.host;
            Part parent = data.target;
        }

        private ModuleRFTank FindValidMRFT(Part p)
        {
            if (!p.fuelCrossFeed)
                return null;

            if (part.FindAttachNodeByPart(p) is AttachNode node
                && (node.nodeType != AttachNode.NodeType.Stack
                    || (!string.IsNullOrEmpty(part.NoCrossFeedNodeKey)
                        && node.id.Contains(part.NoCrossFeedNodeKey))))
                return null;

            if (p.FindAttachNodeByPart(part) is AttachNode nodeP
                && (nodeP.nodeType != AttachNode.NodeType.Stack
                    || (!string.IsNullOrEmpty(p.NoCrossFeedNodeKey)
                        && nodeP.id.Contains(p.NoCrossFeedNodeKey))))
                return null;

            return p.FindModuleImplementing<ModuleRFTank>();
        }

        private void FindTanks()
        {
            _tanks.Clear();
            // Just us? Add and return
            if (!part.fuelCrossFeed)
            {
                _tanks.Add(this);
                return;
            }

            _rootModule = this;
            while (_rootModule.part.parent != null && FindValidMRFT(_rootModule.part.parent) is ModuleRFTank mrftP)
                _rootModule = mrftP;

            _rootModule.FillTanks(_tanks);
        }

        private void FillTanks(HashSet<ModuleRFTank> tanks)
        {
            tanks.Add(this);
            foreach (var p in part.children)
            {
                var mrft = FindValidMRFT(p);
                if (mrft != null)
                    mrft.FillTanks(tanks);
            }
        }

        private bool Manages(int resID)
        {
            return true;
        }

        private void OnFlowStateChange(GameEvents.HostedFromToAction<PartResource, bool> data)
        {
            if (_InEvent)
                return;

            _InEvent = true;
            PartResource res = data.host;
            int id = res.info.id;
            Part resPart = res.part;
            var module = _rootModule == null ? this : _rootModule;
            if (module.Manages(id) && module._parts.Contains(resPart))
            {
                foreach (var p in module._parts)
                    if (p != resPart && p.Resources.dict.TryGetValue(id, out var other))
                        other.flowState = data.to;
            }
            _InEvent = false;
        }

        private void OnFlowModeChange(GameEvents.HostedFromToAction<PartResource, PartResource.FlowMode> data)
        {
            if (_InEvent)
                return;

            PartResource res = data.host;
            int id = res.info.id;
            Part resPart = res.part;
            _InEvent = true;
            var module = _rootModule == null ? this : _rootModule;
            if (module.Manages(id) && module._parts.Contains(resPart))
            {
                foreach (var p in module._parts)
                    if (p != resPart && p.Resources.dict.TryGetValue(id, out var other))
                        other.flowMode = data.to;
            }
            _InEvent = false;
        }
    }
}
