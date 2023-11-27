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

        private CollectionDictionary<int, LogicalTank, List<LogicalTank>> _resourceToTank = new CollectionDictionary<int, LogicalTank, List<LogicalTank>>();
        // This can maybe be IReadOnlyDictionary
        public CollectionDictionary<int, LogicalTank, List<LogicalTank>> resourceToTank => _resourceToTank;

        [Persistent]
        private double _volume;
        public double volume => _volume;

        private double _volumeUsed;
        public double volumeAvailable => _volume - _volumeUsed;

        [Persistent]
        private string _tankDefName;
        private TankDef _tankDefinition;
        public TankDef tankDefinition => _tankDefinition;

        public override void Load(ConfigNode node)
        {
            base.Load(node);
            _tankDefinition = TankDef.GetDefinition(_tankDefName);
            _volumeUsed = 0d;
            foreach (var tg in _groups)
            {
                foreach (var kvp in tg.tanks)
                {
                    _volumeUsed += kvp.Value.maxAmount;
                    _resourceToTank.Add(kvp.Key, kvp.Value);
                }
            }
        }

        public override void Save(ConfigNode node)
        {
            _tankDefName = _tankDefinition.Name;
            base.Save(node);
        }
    }
}
