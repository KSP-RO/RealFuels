using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public abstract class ResourceSetBase : ResourceBase
    {
        public float pressure;
        public override float Pressure => pressure;
        protected double _amount;
        public override double amount { get => _amount; set => _amount = value; }
        protected double _maxAmount;
        public override double maxAmount { get => _maxAmount; set => _maxAmount = value; }
        protected double _free;
        public override double free { get => _free; set => _free = value; }
        public abstract int Count { get; }

        public abstract void Add(ResourceWrapper rw, bool recalc);
        public abstract bool Remove(ResourceWrapper rw, bool recalc);
        public abstract bool Contains(ResourceWrapper rw);
        public abstract void Clear();
        public abstract void AmountDeltaApplied(double delta);
        public abstract void MaxAmountDeltaApplied(double delta);
        public abstract void BothDeltaApplied(double amountDelta, double maxDelta);
        public virtual void ChangePriority(ResourceWrapper rw, int oldPri) { }
        public abstract void Recalc();
    }

    public class ResourceSet : ResourceSetBase
    {
        protected bool _isRequesting = false;
        protected bool _dirtyRatios = false;

        // These are three separate lists so that the two ratio lists
        // can be passed in two different ways depending on request sign
        private List<ResourceWrapper> _resources = new List<ResourceWrapper>();
        public override int Count => _resources.Count;
        private List<double> _ratiosAmount = new List<double>();
        private List<double> _ratiosFree = new List<double>();

        public override void Add(ResourceWrapper rw, bool recalc)
        {
            double amt = rw.amount;
            double max = rw.maxAmount;
            _maxAmount += max;
            _amount += amt;
            _free = _maxAmount - _amount;
            _resources.Add(rw);
            _ratiosAmount.Add(0d);
            _ratiosFree.Add(0d);
            rw.LinkSet(this);

            if (recalc)
                Recalc();
        }

        public override bool Remove(ResourceWrapper rw, bool recalc)
        {
            for (int i = _resources.Count; i-- > 0;)
            {
                if (_resources[i] == rw)
                {
                    Remove(i, recalc);
                    return true;
                }
            }
            return false;
        }

        public void Remove(int i, bool recalc = true)
        {
            var rw = _resources[i];
            double amt = rw.amount;
            double max = rw.maxAmount;
            _amount -= amt;
            _maxAmount -= max;
            _free = _maxAmount - _amount;
            rw.UnlinkSet(this);

            _resources.RemoveAt(i);
            _ratiosAmount.RemoveAt(i);
            _ratiosFree.RemoveAt(i);
            if (recalc)
                Recalc();
        }

        public override bool Contains(ResourceWrapper rw)
        {
            return _resources.Contains(rw);
        }

        public override void Clear()
        {
            foreach (var rw in _resources)
                rw.UnlinkSet(this);
            _resources.Clear();
            _ratiosAmount.Clear();
            _ratiosFree.Clear();
            _maxAmount = 0d;
            _amount = 0d;
            _free = 0d;
        }

        public override void AmountDeltaApplied(double delta)
        {
            if (_isRequesting)
                return;

            _amount += delta;
            _free = _maxAmount - _amount;
            _dirtyRatios = true;
        }

        public override void MaxAmountDeltaApplied(double delta)
        {
            if (_isRequesting)
                return;

            _maxAmount += delta;
            _free = _maxAmount - _amount;
            _dirtyRatios = true;
        }

        public override void BothDeltaApplied(double amountDelta, double maxDelta)
        {
            if (_isRequesting)
                return;

            _amount += amountDelta;
            _maxAmount += maxDelta;
            _dirtyRatios = true;
        }

        public override void ChangePriority(ResourceWrapper rw, int oldPri) { }

        public override void Recalc()
        {
            for (int i = _resources.Count; i-- > 0;)
            {
                var rw = _resources[i];
                double amt = rw.amount;
                _ratiosFree[i] = (rw.maxAmount - amt) / _free;
                _ratiosAmount[i] = amt / _amount;
            }
            _dirtyRatios = false;
        }

        public override double Request(double demand, bool simulate)
        {
            if (demand > 0d)
            {
                if (_amount == 0d)
                    return demand;

                _isRequesting = true;

                if (_amount < demand)
                {
                    _amount = 0d;
                    _free = _maxAmount;
                    double recip = 1d / _maxAmount;
                    for (int i = _resources.Count; i-- > 0;)
                    {
                        var rw = _resources[i];
                        rw.SetAmount(0d, simulate);
                        _ratiosFree[i] = rw.maxAmount * recip;
                        _ratiosAmount[i] = 0d;
                    }
                    _isRequesting = false;
                    return demand - _amount;
                }
                else
                {
                    Subtract(demand, ref _amount, ref _free, _ratiosAmount, _ratiosFree, simulate);
                    _isRequesting = false;
                    return 0d;
                }
            }
            else
            {
                if (_free == 0d)
                    return demand;

                _isRequesting = true;

                demand = -demand;
                if (_free < demand)
                {
                    double recip = 1d / _maxAmount;
                    for (int i = _resources.Count; i-- > 0;)
                    {
                        var rw = _resources[i];
                        double max = rw.maxAmount;
                        rw.SetAmount(max, simulate);
                        _ratiosFree[i] = 0d;
                        _ratiosAmount[i] = max * recip; ;
                    }
                    _isRequesting = false;
                    return _free - demand;
                }
                else
                {
                    Subtract(demand, ref _free, ref _amount, _ratiosFree, _ratiosAmount, simulate);
                    _isRequesting = false;
                    return 0d;
                }
            }
        }

        /// <summary>
        /// The math is the same whether we're taking subtracting from
        /// amount or subtracting from free. So we pass main and inverse
        /// numbers, and main and inverse ratios
        /// </summary>
        /// <param name="delta"></param>
        /// <param name="mainTotal"></param>
        /// <param name="inverseTotal"></param>
        /// <param name="ratiosMain"></param>
        /// <param name="ratiosInverse"></param>
        /// <param name="simulate"></param>
        private void Subtract(double delta, ref double mainTotal, ref double inverseTotal, List<double> ratiosMain, List<double> ratiosInverse, bool simulate)
        {
            if (_dirtyRatios)
                Recalc();

            mainTotal -= delta;
            if (mainTotal < 1e-12)
                mainTotal = 0d;
            inverseTotal = _maxAmount - mainTotal;
            // we know know amount and free are correct.

            double invRecip = 1d / inverseTotal;
            for (int i = _resources.Count; i-- > 0;)
            {
                var rw = _resources[i];
                // calculate the new main from the main ratio
                // (the main ratio stays unchanged during subtraction)
                double rwMain = mainTotal * ratiosMain[i];
                // Now that we have the new local main, use it and the global
                // main and inverse to calculate the new inverse ratio
                ratiosInverse[i] = (rw.maxAmount - rwMain) * invRecip;
                // Now that we know both ratios are correct, we can set amount
                // directly from global amount
                rw.SetAmount(_ratiosAmount[i] * _amount, simulate);
            }
        }
    }

    public class ResourceSetWithPri : ResourceSet
    {
        public int priority;
        private PrioritySet _priSet;

        public ResourceSetWithPri(PrioritySet priSet, int pri)
        {
            _priSet = priSet;
            priority = pri;
        }

        public override void Add(ResourceWrapper rw, bool recalc = true)
        {
            _priSet.BothDeltaApplied(rw.amount, rw.maxAmount);
            base.Add(rw, recalc);
        }

        public override bool Remove(ResourceWrapper rw, bool recalc = true)
        {
            if (!base.Remove(rw, recalc))
                return false;

            _priSet.BothDeltaApplied(-rw.amount, -rw.maxAmount);
            return true;
        }

        public override void AmountDeltaApplied(double delta)
        {
            if (_isRequesting)
                return;

            base.AmountDeltaApplied(delta);
            _priSet.AmountDeltaApplied(delta);
        }

        public override void MaxAmountDeltaApplied(double delta)
        {
            if (_isRequesting)
                return;

            base.MaxAmountDeltaApplied(delta);
            _priSet.MaxAmountDeltaApplied(delta);
        }

        public override void BothDeltaApplied(double amountDelta, double maxDelta)
        {
            if (_isRequesting)
                return;

            base.BothDeltaApplied(amountDelta, maxDelta);
            _priSet.BothDeltaApplied(amountDelta, maxDelta);
        }

        public override double Request(double demand, bool simulate)
        {
            double oldAmt = _amount;
            double remDemand = base.Request(demand, simulate);
            if (oldAmt != _amount)
                _priSet.AmountDeltaApplied(_amount - oldAmt);

            return remDemand;
        }
    }
}
