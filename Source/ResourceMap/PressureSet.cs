using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class PressureSetBase<T> : List<T>
        where T : ResourceBase
    {
        protected int GetIndex(float pressure)
        {
            if (pressure == 0f)
                return 0;

            int low = 0;
            int high = Count - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var res = this[mid];
                float p = res.Pressure;
                if (p == pressure)
                {
                    return mid;
                }

                if (p < pressure)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            return ~low;
        }

        public double Request(double demand, float pressure, bool simulate)
        {
            int c = Count;

            int idx = GetIndex(pressure);

            // Handle no exact match
            if (idx < 0)
            {
                idx = ~idx;
                // if we don't have anything >= desired pressure
                // then idx will be at Count
                if (idx >= c)
                    return demand;
            }

            double remDemand = demand;
            for (int i = idx; i < c && remDemand != 0; ++i)
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
            int idx = GetIndex(pressure);

            // Handle no exact match
            if (idx < 0)
            {
                idx = ~idx;
                // if we don't have anything >= desired pressure
                // then idx will be at Count
                if (idx >= c)
                    return;
            }

            for (int i = ~idx; i < c; ++i)
            {
                var set = this[i];
                amount += set.amount;
                maxAmount += set.maxAmount;
            }
        }
    }

    public class PressureSet<T> : PressureSetBase<T>
        where T : ResourceSetBase, new()
    {
        public void Add(ResourceWrapper rw, bool recalc)
        {
            float pressure = rw.Pressure;
            int idx = GetIndex(pressure);
            if (idx >= 0)
            {
                this[idx].Add(rw, recalc);
                return;
            }
            
            var newSet = new T();
            newSet.pressure = pressure;
            Insert(~idx, newSet);
            newSet.Add(rw, recalc);
        }

        public bool Remove(ResourceWrapper rw, bool recalc)
        {
            float pressure = rw.Pressure;
            int idx = GetIndex(pressure);
            if (idx < 0)
                return false;

            var set = this[idx];
            set.Remove(rw, recalc);
            if (set.Count == 0)
                RemoveAt(idx);

            return true;
        }

        public void ChangePriority(ResourceWrapper rw, int oldPri)
        {
            float pressure = rw.Pressure;
            int idx = GetIndex(pressure);
            if (idx < 0)
                return;

            this[idx].ChangePriority(rw, oldPri);
        }

        public bool Contains(ResourceWrapper rw)
        {
            foreach (var set in this)
                if (set.Contains(rw))
                    return true;

            return false;
        }

        public new void Clear()
        {
            for (int i = Count; i-- > 0;)
                this[i].Clear();

            base.Clear();
        }

        public void Recalc()
        {
            for (int i = Count; i-- > 0;)
                this[i].Recalc();
        }
    }

    // This is some level of copy-pasta. Oh well.

    public class PartPressureSet : PressureSetBase<ResourceWrapper>
    {
        public new bool Add(ResourceWrapper rw)
        {
            float pressure = rw.Pressure;
            int idx = GetIndex(pressure);
            if (idx >= 0)
            {
                return false;
            }

            Insert(~idx, rw);
            return true;
        }

        public new bool Remove(ResourceWrapper rw)
        {
            float pressure = rw.Pressure;
            int idx = GetIndex(pressure);
            if (idx < 0)
                return false;

            RemoveAt(idx);
            return true;
        }
    }
}