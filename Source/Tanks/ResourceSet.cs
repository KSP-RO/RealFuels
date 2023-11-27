using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class ResourceSet
    {
        public int priority;
        public double maxAmount;
        public double amount;
        public double free;

        private ResourceSetHolder _holder;

        private bool _isRequesting = false;
        private bool _dirtyRatios = false;

        // These are three separate lists so that the two ratio lists
        // can be passed in two different ways depending on request sign
        private List<ResourceWrapper> _resources = new List<ResourceWrapper>();
        public int Count => _resources.Count;
        private List<double> _ratiosAmount = new List<double>();
        private List<double> _ratiosFree = new List<double>();

        public ResourceSet(ResourceSetHolder holder, int pri)
        {
            _holder = holder;
            priority = pri;
        }

        public void Add(ResourceWrapper rw, bool recalc = true)
        {
            double amt = rw.amount;
            double max = rw.maxAmount;
            maxAmount += amt;
            amount += max;
            free = maxAmount - amount;
            _resources.Add(rw);
            _ratiosAmount.Add(0d);
            _ratiosFree.Add(0d);
            rw.LinkSet(this);
            _holder.BothDeltaApplied(amt, max);

            if (recalc)
                Recalc();
        }

        public bool Remove(ResourceWrapper rw, bool recalc = true)
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
            amount -= amt;
            maxAmount -= max;
            free = maxAmount - amount;
            rw.UnlinkSet(this);
            _holder.BothDeltaApplied(-amt, -max);

            _resources.RemoveAt(i);
            _ratiosAmount.RemoveAt(i);
            _ratiosFree.RemoveAt(i);
            if (recalc)
                Recalc();
        }

        public bool Contains(ResourceWrapper rw)
        {
            return _resources.Contains(rw);
        }

        public void Clear()
        {
            foreach (var rw in _resources)
                rw.UnlinkSet(this);
            _resources.Clear();
            _ratiosAmount.Clear();
            _ratiosFree.Clear();
            maxAmount = 0d;
            amount = 0d;
            free = 0d;
        }

        public void AmountDeltaApplied(double delta)
        {
            if (_isRequesting)
                return;

            amount += delta;
            free = maxAmount - amount;
            _holder.AmountDeltaApplied(delta);
            _dirtyRatios = true;
        }

        public void MaxAmountDeltaApplied(double delta)
        {
            if (_isRequesting)
                return;

            maxAmount += delta;
            free = maxAmount - amount;
            _holder.MaxAmountDeltaApplied(delta);
            _dirtyRatios = true;
        }

        public void BothDeltaApplied(double amountDelta, double maxDelta)
        {
            if (_isRequesting)
                return;

            amount += amountDelta;
            maxAmount += maxDelta;
            _holder.BothDeltaApplied(amountDelta, maxDelta);
        }

        public void Recalc()
        {
            for (int i = _resources.Count; i-- > 0;)
            {
                var rw = _resources[i];
                double amt = rw.amount;
                _ratiosFree[i] = (rw.maxAmount - amt) / free;
                _ratiosAmount[i] = amt / amount;
            }
            _dirtyRatios = false;
        }

        public double Request(double demand, bool simulate)
        {
            _isRequesting = true;
            if (_dirtyRatios)
                Recalc();

            if (demand > 0d)
            {
                if (amount == 0d)
                    return demand;

                if (amount < demand)
                {
                    _holder.AmountDeltaApplied(-amount);
                    amount = 0d;
                    free = maxAmount;
                    double recip = 1d / maxAmount;
                    for (int i = _resources.Count; i-- > 0;)
                    {
                        var rw = _resources[i];
                        rw.SetAmount(0d, simulate);
                        _ratiosFree[i] = rw.maxAmount * recip;
                        _ratiosAmount[i] = 0d;
                    }
                    _isRequesting = false;
                    return demand - amount;
                }
                else
                {
                    Subtract(demand, ref amount, ref free, _ratiosAmount, _ratiosFree, simulate);
                    _holder.AmountDeltaApplied(-demand);
                    _isRequesting = false;
                    return 0d;
                }
            }
            else
            {
                if (free == 0d)
                    return demand;

                demand = -demand;
                if (free < demand)
                {
                    _holder.AmountDeltaApplied(free);
                    double recip = 1d / maxAmount;
                    for (int i = _resources.Count; i-- > 0;)
                    {
                        var rw = _resources[i];
                        double max = rw.maxAmount;
                        rw.SetAmount(max, simulate);
                        _ratiosFree[i] = 0d;
                        _ratiosAmount[i] = max * recip; ;
                    }
                    _isRequesting = false;
                    return free - demand;
                }
                else
                {
                    Subtract(demand, ref free, ref amount, _ratiosFree, _ratiosAmount, simulate);
                    _holder.AmountDeltaApplied(demand);
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
            mainTotal -= delta;
            if (mainTotal < 1e-12)
                mainTotal = 0d;
            inverseTotal = maxAmount - mainTotal;
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
                rw.SetAmount(_ratiosAmount[i] * amount, simulate);
            }
        }
    }

}
