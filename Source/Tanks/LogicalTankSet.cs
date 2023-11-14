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

    public class TankDef
    {
        private static readonly Dictionary<string, TankDef> _Definitions = new Dictionary<string, TankDef>();
        public static TankDef GetDefinition(string name) => _Definitions.ValueOrDefault(name);

        [Persistent]
        private string _name;
        public string name => _name;
    }

    public class LogicalTankSet : ConfigNodePersistenceBase
    {
        [Persistent]
        private PersistentList<LogicalTankGroup> _groups = new PersistentList<LogicalTankGroup>();
        public IReadOnlyList<LogicalTankGroup> groups => _groups;

        private Dictionary<int, List<LogicalTank>> _resourceToTank = new Dictionary<int, List<LogicalTank>>();
        public IReadOnlyDictionary<int, List<LogicalTank>> resourceToTank => _resourceToTank;

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
                foreach (var kvp in tg.tanks)
                    _volumeUsed += kvp.Value.maxAmount;
        }

        public override void Save(ConfigNode node)
        {
            _tankDefName = _tankDefinition.name;
            base.Save(node);
        }
    }
}
