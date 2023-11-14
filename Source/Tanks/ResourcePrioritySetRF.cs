using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class ResourcePrioritySetRF : PartSet.ResourcePrioritySet
    {
        private PressureSet<PrioritySet> _prioritySet;
        private PressureSet<VesselSet> _allResources;

        public ResourcePrioritySetRF(bool pulling)
        {
            _prioritySet = new PressureSet<PrioritySet>(pulling);
            _allResources = new PressureSet<VesselSet>(pulling);
        }

        public void Add(ResourceWrapper rw)
        {
            _prioritySet.Add(rw);
            _allResources.Add(rw);
        }

        public void Remove(ResourceWrapper rw)
        {
            _prioritySet.Remove(rw);
            _allResources.Remove(rw);
        }

        public void Clear()
        {
            _prioritySet.Clear();
            _allResources.Clear();
        }

        public double Request(double demand, bool usePri, bool simulate)
        {
            return Request(demand, 0f, usePri, simulate);
        }

        public double Request(double demand, float pressure, bool usePri, bool simulate)
        {
            if (usePri)
                return _prioritySet.Request(demand, pressure, simulate);

            return _allResources.Request(demand, pressure, simulate);
        }

        public void GetTotals(out double amount, out double maxAmount)
        {
            _allResources.GetTotals(out amount, out maxAmount, 0f);
        }

        public void GetTotals(out double amount, out double maxAmount, float pressure)
        {
            _allResources.GetTotals(out amount, out maxAmount, pressure);
        }
    }

    public class PressureSet<T> : SortedList<float, T> 
        where T : ResourceSetHolder, new()
    {
        private bool _pulling;
        public bool pulling => _pulling;

        public PressureSet(bool pulling) { _pulling = pulling; }

        public void Add(ResourceWrapper rw)
        {
            int low = 0;
            int high = Count - 1;
            float pressure = rw.Pressure;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var set = this[mid];
                if (set.pressure == pressure)
                {
                    set.Add(rw);
                    return;
                }

                if (set.pressure < pressure)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            var newSet = new T();
            newSet.SetPulling(pulling);
            newSet.pressure = pressure;
            Add(pressure, newSet);
            newSet.Add(rw);
        }

        public bool Remove(ResourceWrapper rw)
        {
            int low = 0;
            int high = Count - 1;
            float pressure = rw.Pressure;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var set = this[mid];
                if (set.pressure == pressure)
                {
                    set.Remove(rw);
                    if (set.Count == 0)
                        Remove(pressure);

                    return true;
                }

                if (set.pressure < pressure)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            return false;
        }

        public bool Contains(ResourceWrapper rw)
        {
            for (int i = Count; i-- > 0;)
                if (this[i].Contains(rw))
                    return true;

            return false;
        }

        new public void Clear()
        {
            for (int i = Count; i-- > 0;)
                this[i].Clear();

            base.Clear();
        }

        public double Request(double demand, float pressure, bool simulate)
        {
            int c = Count;
            int low = 0;
            int high = c - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var set = this[mid];
                if (set.pressure == pressure)
                    break;

                if (set.pressure < pressure)
                    low = mid + 1;
                else
                    high = mid - 1;
            }

            double remDemand = demand;
            for (int i = low; i < c && remDemand != 0; ++i)
            {
                remDemand = this[i].Request(remDemand, simulate);
            }
            return remDemand;
        }

        public void GetTotals(out double amount, out double maxAmount, float pressure)
        {
            amount = 0d;
            maxAmount = 0d;
            int c = Count;
            int low = 0;
            int high = c - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var set = this[mid];
                if (set.pressure == pressure)
                    break;

                if (set.pressure < pressure)
                    low = mid + 1;
                else
                    high = mid - 1;
            }

            for (int i = low; i < c; ++i)
            {
                var set = this[i];
                amount += set.amount;
                maxAmount += set.maxAmount;
            }
        }
    }

    public abstract class ResourceSetHolder
    {
        protected HashSet<ResourceWrapper> _inactives = new HashSet<ResourceWrapper>();
        protected HashSet<ResourceWrapper> _all = new HashSet<ResourceWrapper>();
        protected bool _pulling;

        public double amount;
        public double maxAmount;
        public double free => maxAmount - amount;
        public IReadOnlyCollection<ResourceWrapper> all => _all;
        public bool pulling => _pulling;
        // we can't use a non-empty constructor with generic types so we
        // have to add this method to be invoked post-construct
        public void SetPulling(bool pulling) { _pulling = pulling; }
        public float pressure;
        public int Count => _all.Count;

        public void AmountDeltaApplied(double delta) => amount += delta;
        public void MaxAmountDeltaApplied(double delta) => maxAmount += delta;
        public void BothDeltaApplied(double amountDelta, double maxDelta)
        {
            amount += amountDelta;
            maxAmount += maxDelta;
        }

        public void Add(ResourceWrapper rw)
        {
            rw.LinkHolder(this);

            if (rw.Flowing(_pulling))
            {
                AddToActive(rw);
            }
            else
            {
                _inactives.Add(rw);
            }
            _all.Add(rw);
        }

        public bool Remove(ResourceWrapper rw)
        {
            if (!_all.Remove(rw))
                return false;
            
            rw.UnlinkHolder(this);

            if (!_inactives.Remove(rw))
                return RemoveFromActive(rw);

            return false;
        }

        public virtual void ChangePriority(ResourceWrapper rw, int oldPri) { }

        public bool Contains(ResourceWrapper rw)
        {
            return _all.Contains(rw);
        }

        public void Clear()
        {
            ClearSets();

            _inactives.Clear();

            foreach (var rw in _all)
                rw.UnlinkHolder(this);

            _all.Clear();
        }

        public bool MakeActive(ResourceWrapper rw)
        {
            if (!_inactives.Remove(rw))
                return false;

            AddToActive(rw);
            return true;
        }

        public bool MakeInactive(ResourceWrapper rw)
        {
            if (!RemoveFromActive(rw))
                return false;

            _inactives.Add(rw);
            return true;
        }

        public abstract double Request(double demand, bool simulate);

        protected abstract void AddToActive(ResourceWrapper rw);
        protected abstract bool RemoveFromActive(ResourceWrapper rw);
        
        protected abstract void ClearSets();
    }

    public class VesselSet : ResourceSetHolder
    {
        protected ResourceSet _set;
        
        public VesselSet()
        {
            _set = new ResourceSet(this, 0);
        }

        public override double Request(double demand, bool simulate)
        {
            return _set.Request(demand, simulate);
        }

        protected override void AddToActive(ResourceWrapper rw)
        {
            _set.Add(rw);
        }

        protected override bool RemoveFromActive(ResourceWrapper rw)
        {
            return _set.Remove(rw);
        }

        protected override void ClearSets()
        {
            _set.Clear();
        }
    }

    public class PrioritySet : ResourceSetHolder
    {
        protected List<ResourceSet> _sets = new List<ResourceSet>();

        public override double Request(double demand, bool simulate)
        {
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
            if (_inactives.Contains(rw))
                return;

            RemoveFromActive(rw, oldPri);
            AddToActive(rw);
        }

        protected override void AddToActive(ResourceWrapper rw)
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
                    set.Add(rw);
                    return;
                }

                if (set.priority < pri)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            var newSet = new ResourceSet(this, pri);
            _sets.Add(newSet);
            newSet.Add(rw);
        }

        protected override bool RemoveFromActive(ResourceWrapper rw)
        {
            return RemoveFromActive(rw, rw.Priority);
        }

        protected bool RemoveFromActive(ResourceWrapper rw, int pri)
        {
            int low = 0;
            int high = _sets.Count - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var set = _sets[mid];
                if (set.priority == pri)
                {
                    set.Remove(rw);
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

        protected override void ClearSets()
        {
            foreach (var set in _sets)
                set.Clear();
        }
    }
}