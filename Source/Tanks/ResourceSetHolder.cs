using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public abstract class ResourceSetHolder
    {
        public abstract double amount { get; set; }
        public abstract double maxAmount { get; set; }
        public abstract double free { get; }
        public float pressure;
        public abstract int Count { get; }

        // In the default case (The flat set) amount/maxamount link
        // directly to the ResourceSet underneath, so there's nothing
        // to update at our level. We override these for the PrioritySet
        // case.
        public virtual void AmountDeltaApplied(double delta) { }
        public virtual void MaxAmountDeltaApplied(double delta) { }
        public virtual void BothDeltaApplied(double amountDelta, double maxDelta) { }

        public abstract void Add(ResourceWrapper rw, bool recalc);

        public abstract bool Remove(ResourceWrapper rw, bool recalc);

        public virtual void ChangePriority(ResourceWrapper rw, int oldPri) { }

        public abstract void Clear();

        public abstract double Request(double demand, bool simulate);

        public abstract void Recalc();
    }

    public class FlatSet : ResourceSetHolder
    {
        protected ResourceSet _set;

        public override double amount
        { 
            get => _set.amount;
            set => _set.amount = value;
        }

        public override double maxAmount
        {
            get => _set.maxAmount;
            set => _set.maxAmount = value;
        }

        public override double free => _set.free;

        public override int Count => _set.Count;

        public FlatSet()
        {
            _set = new ResourceSet(this, 0);
        }

        public override double Request(double demand, bool simulate)
        {
            return _set.Request(demand, simulate);
        }

        public override void Add(ResourceWrapper rw, bool recalc)
        {
            _set.Add(rw, recalc);
        }

        public override bool Remove(ResourceWrapper rw, bool recalc)
        {
            return _set.Remove(rw, recalc);
        }

        public override void Clear()
        {
            _set.Clear();
        }

        public override void Recalc()
        {
            _set.RecalcRatios();
        }
    }

    public class PrioritySet : ResourceSetHolder
    {
        private double _amount;
        public override double amount
        {
            get { return _amount; }
            set { _amount = value; }
        }

        private double _maxAmount;
        public override double maxAmount
        {
            get { return _maxAmount; }
            set { _maxAmount = value; }
        }

        public override double free => _maxAmount - _amount;

        private List<ResourceSet> _sets = new List<ResourceSet>();
        public override int Count => _sets.Count;

        public override void AmountDeltaApplied(double delta) => _amount += delta;
        public override void MaxAmountDeltaApplied(double delta) => _maxAmount += delta;
        public override void BothDeltaApplied(double amountDelta, double maxDelta)
        {
            _amount += amountDelta;
            _maxAmount += maxDelta;
        }

        public override double Request(double demand, bool simulate)
        {
            // if we can't meet any demand at all, return
            if (demand > 0 ? (_amount == 0d) : (_amount == _maxAmount))
                return demand;

            double remDemand = demand;
            
            for (int i = _sets.Count; i-- > 0;)
            {
                remDemand = _sets[i].Request(demand, simulate);
                if (remDemand == 0d)
                    break;
            }

            return remDemand;
        }

        public override void ChangePriority(ResourceWrapper rw, int oldPri)
        {
            RemoveInternal(rw, oldPri, true);
            Add(rw, true);
        }

        public override void Add(ResourceWrapper rw, bool recalc)
        {
            int low = 0;
            int high = _sets.Count - 1;
            int pri = rw.Priority;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var set = _sets[mid];
                if (set.priority == pri)
                {
                    set.Add(rw, recalc);
                    return;
                }

                if (set.priority < pri)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            var newSet = new ResourceSet(this, pri);
            _sets.Insert(low, newSet);
            newSet.Add(rw, recalc);
        }

        public override bool Remove(ResourceWrapper rw, bool recalc)
        {
            return RemoveInternal(rw, rw.Priority, recalc);
        }

        protected bool RemoveInternal(ResourceWrapper rw, int pri, bool recalc)
        {
            int low = 0;
            int high = _sets.Count - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var set = _sets[mid];
                if (set.priority == pri)
                {
                    set.Remove(rw, recalc);
                    if (set.Count == 0)
                        _sets.RemoveAt(mid);

                    return true;
                }

                if (set.priority < pri)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            return false;
        }

        public override void Clear()
        {
            foreach (var set in _sets)
                set.Clear();
            
            _sets.Clear();
        }

        public override void Recalc()
        {
            for (int i = _sets.Count; i-- > 0;)
                _sets[i].RecalcRatios();
        }
    }
}