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

    public abstract class ResourceWrapper
    {
        /// <summary>
        /// For small numbers of sets, this is probably faster than a hashset
        /// </summary>
        private List<ResourceSet> _sets = new List<ResourceSet>();

        private List<ResourceSetHolder> _holders = new List<ResourceSetHolder>();

        /// <summary>
        /// Must either call SetAmount or call PushAmountDelta itself
        /// </summary>
        public virtual double amount { get; set; }

        /// <summary>
        /// Must call PushMaxAmount
        /// </summary>
        public virtual double maxAmount { get; set; }

        /// <summary>
        /// Must either call SetAmount or call PushAmountDelta itself
        /// </summary>
        public virtual double free { get; set; }
        /// <summary>
        /// Must call PushAmountDelta with the amount delta
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="simulate"></param>
        public abstract void SetAmount(double amount, bool simulate);
        public abstract bool Flowing(bool pulling);
        public abstract int Priority { get; }
        public virtual float Pressure => 0f;
        public abstract void ResetSim();
        public abstract double Transfer(double amt, bool simulate);
        
        public void LinkSet(ResourceSet set) { _sets.Add(set); }
        public void UnlinkSet(ResourceSet set) { _sets.Remove(set); }
        public void LinkHolder(ResourceSetHolder holder) { _holders.Add(holder); }
        public void UnlinkHolder(ResourceSetHolder holder) { _holders.Remove(holder); }

        public void OnFlowStateChange(bool from)
        {
            if (from)
            {
                for (int i = _holders.Count; i-- > 0;)
                    _holders[i].MakeInactive(this);
            }
            else
            {
                for (int i = _holders.Count; i-- > 0;)
                {
                    var h = _holders[i];
                    if (Flowing(h.pulling))
                        h.MakeActive(this);
                }
            }
        }

        public void OnFlowModeChange(PartResource.FlowMode from, PartResource.FlowMode to)
        {
            for (int i = _holders.Count; i-- > 0;)
            {
                var h = _holders[i];
                PartResource.FlowMode test = h.pulling ? PartResource.FlowMode.Out : PartResource.FlowMode.In;
                if ((from & test) != 0)
                {
                    // Note: we may already be inactive due to
                    // flow state. But this is a safe operation.
                    if ((to & test) == 0)
                        h.MakeInactive(this);
                }
                else
                {
                    if (Flowing(h.pulling))
                        h.MakeActive(this);
                }
            }
        }

        public void OnPriorityChange(int oldPri)
        {
            for (int i = _holders.Count; i-- > 0;)
                _holders[i].ChangePriority(this, oldPri);
        }

        protected void PushAmountDelta(double delta)
        {
            for (int i = _sets.Count; i-- > 0;)
                _sets[i].AmountDeltaApplied(delta);
        }
        protected void PushMaxAmountDelta(double delta)
        {
            for (int i = _sets.Count; i-- > 0;)
                _sets[i].MaxAmountDeltaApplied(delta);
        }
        protected void PushBothDelta(double amountDelta, double maxDelta)
        {
            for (int i = _sets.Count; i-- > 0;)
                _sets[i].BothDeltaApplied(amountDelta, maxDelta);
        }

        protected EventData<PartResource> GetAmountChangeEvent(double oldAmt)
        {
            double amt = amount;
            double max = maxAmount;
            if (amt < max)
            {
                if (oldAmt == max)
                {
                    if (amt == 0d)
                        return GameEvents.onPartResourceFullEmpty;
                    else
                        return GameEvents.onPartResourceFullNonempty;
                }
                else
                {
                    if (oldAmt == 0d)
                    {
                        if (amt > 0d)
                            return GameEvents.onPartResourceEmptyNonempty;
                    }
                    else if (amt == 0d)
                    {
                        return GameEvents.onPartResourceNonemptyEmpty;
                    }
                }
            }
            else
            {
                if (oldAmt > 0d)
                {
                    if (oldAmt < max)
                        return GameEvents.onPartResourceNonemptyFull;
                }
                else
                {
                    return GameEvents.onPartResourceEmptyFull;
                }
            }

            return null;
        }
    }

    public class PartResourceWrapper : ResourceWrapper
    {
        private PartResource _res;
        public PartResourceWrapper(PartResource r) { _res = r; }
        public override int Priority => _res.part.GetResourcePriority();

        public override double amount
        {
            get => _res.amount;
            set
            {
                double oldAmount = _res.amount;
                _res.amount = value;
                PushAmountDelta(value - oldAmount);
            }
        }
        public override double free
        {
            get => _res.maxAmount - _res.amount;
            set
            {
                double oldAmount = _res.amount;
                _res.amount = _res.maxAmount - value;
                PushAmountDelta(_res.amount - oldAmount);
            }
        }
        public override double maxAmount
        {
            get => _res.maxAmount;
            set
            {
                double oldMax = _res.maxAmount;
                _res.maxAmount = value;
                PushMaxAmountDelta(value - oldMax);
            }
        }

        public override void SetAmount(double amount, bool simulate)
        {
            if (amount == _res.amount)
                return;

            double oldAmt = _res.amount;
            _res.amount = UtilMath.Clamp(amount, 0d, _res.maxAmount);
            PushAmountDelta(_res.amount - oldAmt);

            if (simulate)
                return;

            var evt = GetAmountChangeEvent(oldAmt);
            if (evt != null)
                evt.Fire(_res);
        }

        public override double Transfer(double amt, bool simulate)
        {
            return _res.part.TransferResource(_res, amt, _res.part, simulate);
        }

        public override bool Flowing(bool pulling)
        {
            return _res.Flowing(pulling);
        }

        public override void ResetSim()
        {
            _res.part.ResetSimulationResources();
        }
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

        private PartResourceDefinition _resDef;
        public PartResourceDefinition def => _resDef;
        [Persistent]
        private string _resName;

        public LogicalTank(LogicalTankGroup group) { _group = group; }

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

        public LogicalTank() { }

        public LogicalTank(List<Tank> tanks)
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

        public override bool Flowing(bool pulling)
        {
            if (_tanks.Count == 0)
                return false;
            return _tanks[0].tank.res.Flowing(pulling);
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
