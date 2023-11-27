using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class PressureSet<T> : List<T> 
        where T : ResourceSetHolder, new()
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

        new public void Clear()
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

        private int GetIndex(float pressure)
        {
            if (pressure == 0f)
                return 0;

            int low = 0;
            int high = Count - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var set = this[mid];
                if (set.pressure == pressure)
                {
                    return mid;
                }

                if (set.pressure < pressure)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            return ~low;
        }
    }
}