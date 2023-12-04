using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public abstract class ResourceBase
    {
        /// <summary>
        /// When used on a ResourceWrapper:
        /// Must either call SetAmount or call PushAmountDelta itself
        /// </summary>
        public abstract double amount { get; set; }

        /// <summary>
        /// When used on a ResourceWrapper:
        /// Must call PushMaxAmount
        /// </summary>
        public abstract double maxAmount { get; set; }

        /// <summary>
        /// When used on a ResourceWrapper:
        /// Must either call SetAmount or call PushAmountDelta itself
        /// </summary>
        public abstract double free { get; set; }

        public virtual float Pressure => 0f;
        public virtual double Request(double demand, bool simulate) { return demand; }
    }

    public abstract class ResourceWrapper : ResourceBase
    {
        /// <summary>
        /// For small numbers of sets, this is probably faster than a hashset
        /// </summary>
        private List<ResourceSet> _sets = new List<ResourceSet>();

        protected ShipResourceMap.ResourceData _resourceData;

        public abstract int Priority { get; }
        public abstract int resID { get; }

        /// <summary>
        /// Must call PushAmountDelta with the amount delta
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="simulate"></param>
        public abstract void SetAmount(double amount, bool simulate);
        public override double Request(double demand, bool simulate)
        {
            demand = -demand;
            double oldAmt = amount;
            SetAmount(oldAmt + demand, simulate);
            return oldAmt - amount;
        }
        public abstract bool Flowing();
        public abstract void ResetSim();
        public abstract double Transfer(double amt, bool simulate);

        public ResourceWrapper(ShipResourceMap.ResourceData rc) { _resourceData = rc; }
        
        public void LinkSet(ResourceSet set) { _sets.Add(set); }
        public void UnlinkSet(ResourceSet set) { _sets.Remove(set); }

        public void OnFlowStateChange(bool from)
        {
            if (from)
            {
                // Checks to be sure we were already inactive
                // (so if we weren't actually flowing before,
                // this is safe)
                _resourceData.MakeInactive(this);
            }
            else
            {
                if (Flowing())
                    _resourceData.MakeActive(this);
            }
        }

        public void OnFlowModeChange(PartResource.FlowMode from, PartResource.FlowMode to)
        {
            if ((from & PartResource.FlowMode.Both) != 0)
            {
                if (to == PartResource.FlowMode.None)
                {
                    // Checks to be sure we were already inactive
                    // (so if we weren't actually flowing before,
                    // this is safe)
                    _resourceData.MakeInactive(this);
                }
            }
            else if (from == PartResource.FlowMode.None && Flowing())
            {
                _resourceData.MakeActive(this);
            }
        }

        public void OnPriorityChange(int oldPri)
        {
            _resourceData.ChangePriority(this, oldPri);
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
        public PartResourceWrapper(PartResource r, ShipResourceMap.ResourceData rc) : base(rc) { _res = r; }
        public override int Priority => _res.part.GetResourcePriority();
        public override int resID => _res.info.id;

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

        public override bool Flowing()
        {
            return _res.flowState && _res.flowMode != PartResource.FlowMode.None;
        }

        public override void ResetSim()
        {
            _res.part.ResetSimulationResources();
        }
    }

    public class ResourceSnapshotWrapper : ResourceWrapper
    {
        private ProtoPartResourceSnapshot _res;
        private int _priority;
        public ResourceSnapshotWrapper(ProtoPartResourceSnapshot r, int priority, ShipResourceMap.ResourceData rc) : base(rc) { _res = r; _priority = priority; }
        public override int Priority => _priority;
        public override int resID => _res.definition.id;

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
        }

        public override double Transfer(double amt, bool simulate)
        {
            double oldAmt = _res.amount;
            _res.amount = UtilMath.Clamp(oldAmt + amt, 0d, _res.maxAmount);
            double delta = _res.amount - oldAmt;
            PushAmountDelta(delta);
            return -delta;
        }

        public override bool Flowing()
        {
            return _res.flowState;
        }

        public override void ResetSim()
        {
        }
    }
}
