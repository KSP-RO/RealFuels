using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    // Stub. Replace with FuelTank later.
    public class Tank
    {
        public double amount;
        public double maxAmount;

        private LogicalTank _logicalTank = null;

        private PartResource _res;
        public PartResource res => _res;
        public Part part => _res.part;

        public void Link(LogicalTank lt) { _logicalTank = lt; }

        public void ChangeVolume(double newVolume, bool scaleAmount)
        {
            double oldMax = maxAmount;
            maxAmount = newVolume;
            _logicalTank.OnTankVolumeChanged(newVolume - oldMax, scaleAmount);
        }

        public double Transfer(double amt, bool simulate)
        {
            return _res.part.TransferResource(_res, amt, _res.part, simulate);
        }
    }

    public class TankAndRatio
    {
        public Tank tank;
        public double ratio;
    }

    public class LogicalTank : ResourceWrapper, IConfigNode
    {
        private List<TankAndRatio> _tanks = new List<TankAndRatio>();
        public IReadOnlyList<TankAndRatio> Tanks => _tanks;

        private LogicalTankGroup _group;

        [Persistent]
        private double _amount = 0d;
        [Persistent]
        private double _free = 0d;
        [Persistent]
        private double _maxAmount = 0d;

        [Persistent]
        private bool _flowing = true;

        private PartResourceDefinition _resDef;
        public PartResourceDefinition def => _resDef;
        [Persistent]
        private string _resName;
        public override int resID => _resDef.id;

        public LogicalTank(LogicalTankGroup group) : base(null) { _group = group; }
        public void LinkCache(ShipResourceMap.ResourceData rc) { _resourceData = rc; }

        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
            _resDef = PartResourceLibrary.Instance.GetDefinition(_resName);
        }

        public void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
        }

        public override int Priority => _tanks[0].tank.part.GetResourcePriority();
        public override float Pressure => _group.pressure;

        public override double amount
        {
            get
            {
                return _amount;
            }
            set
            {
                SetAmount(value, false);
            }
        }

        public override double free
        {
            get
            {
                return _free;
            }
            set
            {
                // Note we don't have to clamp (or set _free!)
                // because this will.
                SetAmount(_maxAmount - value, false);
            }
        }

        public override double maxAmount
        {
            get
            {
                return _maxAmount;
            }
            set
            {
                SetMaxAmount(value, true);
            }
        }

        public LogicalTank() : base(null) { }

        public LogicalTank(List<Tank> tanks) : base(null)
        {
            foreach (var t in tanks)
            {
                t.Link(this);
                _tanks.Add(new TankAndRatio() { tank = t });
                _amount += t.amount;
                _maxAmount += t.maxAmount;
            }

            _free = _maxAmount - _amount;

            RecalcRatios();
        }

        public void PostLoadLink(List<Tank> tanks)
        {
            double oldMax = _maxAmount;
            _maxAmount = 0d; // for ratio calcs
            foreach (var t in tanks)
            {
                t.Link(this);
                _tanks.Add(new TankAndRatio() { tank = t });
                _maxAmount += t.maxAmount;
            }

            RecalcRatios();

            _maxAmount = oldMax;
            SetAmount(_amount, true); // don't raise events
        }

        public override bool Flowing()
        {
            return _flowing;
        }

        public bool SetFlowing(bool newVal)
        {
            if (newVal == _flowing)
                return _flowing;

            bool oldVal = _flowing;
            _flowing = newVal;
            var _newMode = newVal ? PartResource.FlowMode.Both : PartResource.FlowMode.None;
            foreach (var t in _tanks)
            {
                var res = t.tank.res;
                res._flowState = newVal;
                res._flowMode = _newMode;
            }
            OnFlowStateChange(oldVal);
            return oldVal;
        }

        public override void ResetSim()
        {
            foreach (var t in _tanks)
                t.tank.part.ResetSimulationResources();
        }

        public override double Transfer(double amt, bool simulate)
        {
            double oldAmt = _amount;
            _amount = UtilMath.Clamp(_amount + amt, 0d, _maxAmount);
            foreach (var t in _tanks)
            {
                t.tank.Transfer(amt * t.ratio, simulate);
            }
            return _amount - oldAmt;
        }

        public bool AddTank(Tank t)
        {
            foreach (var pair in _tanks)
                if (pair.tank == t)
                    return false;

            t.Link(this);
            _tanks.Add(new TankAndRatio() { tank = t });
            _amount += t.amount;
            _maxAmount += t.maxAmount;
            RecalcRatios();
            SetAmount(_amount, false); // redistribute
            return true;
        }

        public bool RemoveTank(Tank t)
        {
            int i = _tanks.Count;
            if (i == 1)
                return false;

            for (; i-- > 0;)
                if (_tanks[i].tank == t)
                    break;

            if (i < 0)
                return false;

            _tanks.RemoveAt(i);
            _maxAmount -= t.maxAmount;
            _amount -= t.amount;
            RecalcRatios();
            SetAmount(_amount, false); // should be a no-op
            return true;
        }

        public void Combine(LogicalTank lt)
        {
            _maxAmount += lt._maxAmount;
            _amount += lt._amount;
            _tanks.AddRange(lt._tanks);
            RecalcRatios();
            SetAmount(_amount, false);
        }

        private void RecalcRatios()
        {
            double mult = _maxAmount > 0d ? 1d / _maxAmount : 1d;

            foreach (var t in _tanks)
            {
                t.ratio = t.tank.maxAmount * mult;
            }
        }

        public override void SetAmount(double amount, bool simulate)
        {
            if (_amount == amount)
                return;

            double oldAmt = _amount;
            _amount = UtilMath.Clamp(amount, 0d, _maxAmount);
            PushAmountDelta(_amount - oldAmt);

            bool nonFull = _amount < _maxAmount;
            _free = _maxAmount - _amount;
            var evt = GetAmountChangeEvent(oldAmt);

            foreach (var pair in _tanks)
            {
                pair.tank.amount = nonFull ? _amount * pair.ratio : pair.tank.maxAmount;
                if (simulate || evt == null)
                    continue;

                evt.Fire(pair.tank.res);
            }
        }

        public void SetMaxAmount(double value, bool scaleAmount)
        {
            if (value == _maxAmount)
                return;

            double oldMax = _maxAmount;
            _maxAmount = value;
            if (_maxAmount < 0d)
                _maxAmount = 0d;

            if (!scaleAmount)
            {
                PushMaxAmountDelta(_maxAmount - oldMax);
                return;
            }
            double oldAmt = _amount;
            _amount = (oldMax > 0d ? value / oldMax : 0d) * _amount;
            foreach (var pair in _tanks)
            {
                pair.tank.amount = _amount * pair.ratio;
            }
            PushBothDelta(_amount - oldAmt, _maxAmount - oldMax);
        }

        public void OnTankVolumeChanged(double delta, bool scaleAmount)
        {
            SetMaxAmount(_maxAmount + delta, scaleAmount);
        }
    }
}
