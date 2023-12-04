using KSP.Localization;
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
        private ModuleRFTank _mainModule = null;
        public bool IsMain => _mainModule == null;
        private List<ModuleRFTank> _modules = null;
        public IReadOnlyCollection<ModuleRFTank> modules => _mainModule == null ? _modules : _mainModule._modules;

        private bool _eventsEditor = false;
        private bool _eventsFlight = false;
        private static bool _InEvent = false;

        private bool _needFindTanks;

        [KSPField]
        private bool _isCombinable;

        [KSPField(isPersistant = true)]
        private double _volume;
        public double volume => _volume;

        [KSPField(isPersistant = true)]
        private LogicalTankSetList _tankSets = new LogicalTankSetList();
        public IReadOnlyList<LogicalTankSet> tankSets => _mainModule == null ? _tankSets : _mainModule._tankSets;

        // TODO: handle sim reset/setup (clone)
        private LogicalTankSetList _tankSetsSim;
        public IReadOnlyList<LogicalTankSet> tankSetsSim => _mainModule == null ? _tankSetsSim : _mainModule._tankSetsSim;

        private void HookEventsEditor()
        {
            _eventsEditor = true;
            GameEvents.onPartRemove.Add(OnPartRemove);
            GameEvents.onPartResourceFlowStateChange.Add(OnFlowStateChange);
            GameEvents.onPartResourceFlowModeChange.Add(OnFlowModeChange);
        }

        private void HookEventsFlight()
        {
            GameEvents.onPartResourceFlowStateChange.Add(OnFlowStateChange);
            GameEvents.onPartResourceFlowModeChange.Add(OnFlowModeChange);
        }

        public override void OnAwake()
        {
            _needFindTanks = true;
        }

        public override void OnLoad(ConfigNode node)
        {
            _tankSets.Link(this);
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                _tankSets
            }
        }

        public override void OnStart(StartState state)
        {
            _tankSets.Link(this);
        }

        public override void OnStartFinished(StartState state)
        {
            _mainModule = null;
            _eventsEditor = false;
            _eventsFlight = false;
            
            // Just in case -- loading doesn't run Start, but if it did that'd be bad.
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                return;

            // Only hook attach/remove in editor; in flight we assume
            // you can't change tanks geometry...
            if ((state & StartState.Editor) != 0)
            {
                HookEventsEditor();
                if (part.attached || part.children.Count > 0)
                    FindTanks();
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

        public bool IsResourceManaged(int resID)
        {
            var ts = tankSets;
            if (ts == null)
                return false;
            for (int i = ts.Count; i-- > 0;)
                if (ts[i].tankDefinition.tankInfos.ContainsKey(resID))
                    return true;

            return false;
        }

        public void OnChildAdd(Part child)
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

            if (p.FindModuleImplementing<ModuleRFTank>() is ModuleRFTank mrft && mrft._isCombinable)
                return mrft;

            return null;
        }

        private void FindTanks()
        {
            if (!_needFindTanks)
                return;

            _needFindTanks = false;

            // Just us? Add and return
            if (!part.fuelCrossFeed || !_isCombinable)
            {
                if (_modules == null)
                    _modules = new List<ModuleRFTank>(1);
                _modules.Add(this);
                return;
            }

            _mainModule = this;
            while (_mainModule.part.parent != null && FindValidMRFT(_mainModule.part.parent) is ModuleRFTank mrftP)
                _mainModule = mrftP;

            if (_mainModule._modules == null)
                _mainModule._modules = new List<ModuleRFTank>();

            _mainModule.DFSTanks(_mainModule._modules);
            int maxCount = _mainModule._tankSets.Count;
            ModuleRFTank bestTankSetModule = _mainModule;
            foreach (var m in modules)
            {
                if (m == _mainModule)
                    continue;

                m._mainModule = _mainModule;
                m._modules = null;

                int c = m._tankSets.Count;
                if (c > maxCount)
                {
                    maxCount = c;
                    bestTankSetModule = m;
                }
            }
            if (bestTankSetModule != _mainModule)
            {
                Utilities.Swap(ref _mainModule._tankSets, ref bestTankSetModule._tankSets);
                Utilities.Swap(ref _mainModule._tankSetsSim, ref bestTankSetModule._tankSetsSim);
                _mainModule._tankSets.Link(_mainModule);
                _mainModule._tankSetsSim.Link(_mainModule);
                bestTankSetModule._tankSets.Link(bestTankSetModule);
                bestTankSetModule._tankSetsSim.Link(bestTankSetModule);
            }
        }

        private void DFSTanks(List<ModuleRFTank> tanks)
        {
            tanks.Add(this);
            _needFindTanks = false;
            foreach (var p in part.children)
            {
                var mrft = FindValidMRFT(p);
                if (mrft != null)
                    mrft.DFSTanks(tanks);
            }
        }

        private void OnFlowStateChange(GameEvents.HostedFromToAction<PartResource, bool> data)
        {
            if (_InEvent || !IsMain)
                return;

            _InEvent = true;
            PartResource res = data.host;
            int id = res.info.id;
            Part resPart = res.part;
            var module = _mainModule == null ? this : _mainModule;
            if (module._tankSets.resourceToTank.TryGetValue(id, out var tankList) && module._parts.Contains(resPart))
            {
                foreach (var lt in tankList)
                    lt.SetFlowing(data.to);
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
            var module = _mainModule == null ? this : _mainModule;
            if (module._tankSets.resourceToTank.TryGetValue(id, out var tankList) && module._parts.Contains(resPart))
            {
                var shouldFlow = data.to != PartResource.FlowMode.None;
                foreach (var lt in tankList)
                    lt.SetFlowing(shouldFlow);
            }
            _InEvent = false;
        }
    }
}
