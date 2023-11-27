using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class PrioritySet : ResourceSetBase
    {
        private List<ResourceSetWithPri> _sets = new List<ResourceSetWithPri>();
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
            var newSet = new ResourceSetWithPri(this, pri);
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

        public override bool Contains(ResourceWrapper rw)
        {
            foreach (var rs in _sets)
                if (rs.Contains(rw))
                    return true;

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
                _sets[i].Recalc();
        }
    }
}