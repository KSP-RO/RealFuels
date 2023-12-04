using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class LogicalTankGroup : ConfigNodePersistenceBase
    {
        public double volume;
        private PersistentDictionaryValueTypeKey<int, LogicalTank> _tanks = new PersistentDictionaryValueTypeKey<int, LogicalTank>();
        public IReadOnlyDictionary<int, LogicalTank> tanks => _tanks;
        public int pressurantID;
        public float pressure;
        public int priority;
    }

    public class TankInfo : ConfigNodePersistenceBase
    {
        [Persistent]
        private string _resourceName;
        public string resourceName
        {
            get { return _resourceName; }
            set
            {
                _resourceName = value;
                _resID = value.GetHashCode();
            }
        }

        private int _resID;
        public int resID => _resID;

        public override void Load(ConfigNode node)
        {
            base.Load(node);
            _resID = _resourceName.GetHashCode();
        }
    }

    public class TankDef : ConfigNodePersistenceBase
    {
        private static readonly Dictionary<string, TankDef> _Definitions = new Dictionary<string, TankDef>();
        public static TankDef GetDefinition(string name) => _Definitions.ValueOrDefault(name);

        [Persistent]
        private string name;
        public string Name => name;

        [Persistent]
        private PersistentDictionaryNodeStringHashKeyed<TankInfo> _tankInfos = new PersistentDictionaryNodeStringHashKeyed<TankInfo>();
        public IReadOnlyDictionary<int, TankInfo> tankInfos => _tankInfos;
    }

    public class LogicalTankSet : ConfigNodePersistenceBase
    {
        [Persistent]
        private PersistentList<LogicalTankGroup> _groups = new PersistentList<LogicalTankGroup>();
        public IReadOnlyList<LogicalTankGroup> groups => _groups;

        [Persistent]
        private double _volume;
        public double volume => _volume;
        public void SetVolume(double vol) { _volume = vol; }

        private double _volumeUsed;
        public void SetVolumeUsed(double vol) { _volumeUsed = vol; }
        public double volumeAvailable => _volume - _volumeUsed;

        [Persistent]
        private bool _isSim;
        public bool isSim => _isSim;

        [Persistent]
        private string _tankDefName;
        private TankDef _tankDefinition;
        public TankDef tankDefinition => _tankDefinition;

        private LogicalTankSetList _list;

        public LogicalTankSet() { }

        public LogicalTankSet(LogicalTankSetList list, bool isSim)
        {
            _isSim = isSim;
            _list = list;
        }

        public override void Load(ConfigNode node)
        {
            base.Load(node);
            _tankDefinition = TankDef.GetDefinition(_tankDefName);
        }

        public override void Save(ConfigNode node)
        {
            _tankDefName = _tankDefinition.Name;
            base.Save(node);
        }

        public void Link(LogicalTankSetList list)
        {
            _list = list;
        }

        public LogicalTankSet MakeSimCopy()
        {
            return new LogicalTankSet(_list, true);
        }
    }

    public class LogicalTankSetList : PersistentList<LogicalTankSet>
    {
        private CollectionDictionary<int, LogicalTank, List<LogicalTank>> _resourceToTank = new CollectionDictionary<int, LogicalTank, List<LogicalTank>>();
        // This can maybe be IReadOnlyDictionary
        public IReadOnlyCollectionDictionary<int, LogicalTank, List<LogicalTank>> resourceToTank => _resourceToTank;

        // Doesn't need to be saved because sim sets are never saved
        private bool _isSim = false;
        public bool isSim => _isSim;

        [Persistent]
        private bool _isPrimary = true;
        public void SetPrimary(bool p)
        {
            _isPrimary = p;
            if (!p)
                Clear();
        }

        private ModuleRFTank _mainModule;
        public ModuleRFTank mainModule => _mainModule;

        public LogicalTankSetList() { }
        public LogicalTankSetList(ModuleRFTank module, bool isSim)
        {
            _isSim = isSim;
            _mainModule = module;
        }

        public new void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
            base.Load(node);
            PostLoad();
        }

        public void PostLoad()
        {
            _resourceToTank.Clear();
            foreach (var lts in this)
            {
                double vol = 0d;
                foreach (var tg in lts.groups)
                {
                    foreach (var kvp in tg.tanks)
                    {
                        vol += kvp.Value.maxAmount;
                        _resourceToTank.Add(kvp.Key, kvp.Value);
                    }
                }
                lts.SetVolumeUsed(vol);
            }
        }

        public void Apply()
        {
            double volRecip = 0d;
            foreach (var lts in this)
                volRecip += lts.volume;
            volRecip = volRecip > 0d ? 1d / volRecip : 1d;

            foreach (var kvp in _resourceToTank)
            {
                double maxTot = 0d;
                double amtTot = 0d;
                foreach (var lt in kvp.Value)
                {
                    amtTot += lt.amount;
                    maxTot += lt.maxAmount;
                }

                foreach (var m in _mainModule.modules)
                {
                    var res = _isSim ? m.part.Resources : m.part.SimulationResources;
                    if (!res.dict.TryGetValue(kvp.Key, out var pr))
                    {
                        pr = new PartResource(m.part, _isSim);
                        pr.SetInfo(PartResourceLibrary.Instance.GetDefinition(kvp.Key));
                        pr.isTweakable = pr.info.isTweakable;
                        pr.isVisible = pr.info.isVisible;
                        pr.flowState = true;
                        pr.flowMode = PartResource.FlowMode.Both;
                    }
                    double portion = m.volume * volRecip;
                    pr.amount = portion * amtTot;
                    pr.maxAmount = portion * maxTot;
                }
            }
        }

        public new void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
            base.Save(node);
        }

        public void Link(ModuleRFTank module) { _mainModule = module; }

        public LogicalTankSetList MakeSimCopy()
        {
            var simList = new LogicalTankSetList(_mainModule, true);
            foreach (var lts in this)
            {
                var ltsSim = lts.MakeSimCopy();
                ltsSim.Link(simList);
                simList.Add(ltsSim);
            }
            simList.PostLoad();
            return simList;
        }
    }
}
