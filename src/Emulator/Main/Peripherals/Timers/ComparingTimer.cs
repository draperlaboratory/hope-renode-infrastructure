//
// Copyright (c) 2010-2018 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Time;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class ComparingTimer : ITimer, IPeripheral
    {
        public ComparingTimer(IClockSource clockSource, long frequency, ulong limit = ulong.MaxValue, Direction direction = Direction.Ascending,
            bool enabled = false, WorkMode workMode = WorkMode.OneShot, bool eventEnabled = false, ulong compare = ulong.MaxValue)
        {
            if(compare > limit)
            {
                throw new ConstructionException(string.Format(CompareHigherThanLimitMessage, compare, limit));
            }
            this.clockSource = clockSource;

            initialDirection = direction;
            initialFrequency = frequency;
            initialLimit = limit;
            initialCompare = compare;
            initialEnabled = enabled;
            initialWorkMode = workMode;
            initialEventEnabled = eventEnabled;
            InternalReset();
        }

        public bool Enabled
        {
            get
            {
                return clockSource.GetClockEntry(CompareReachedInternal).Enabled;
            }
            set
            {
                clockSource.ExchangeClockEntryWith(CompareReachedInternal, oldEntry => oldEntry.With(enabled: value));
            }
        }

        public bool EventEnabled { get; set; }

        public ulong Value
        {
            get
            {
                var currentValue = 0UL;
                clockSource.GetClockEntryInLockContext(CompareReachedInternal, entry =>
                {
                    currentValue = valueAccumulatedSoFar + entry.Value;
                });
                return currentValue;
            }
            set
            {
                clockSource.ExchangeClockEntryWith(CompareReachedInternal, entry =>
                {
                    valueAccumulatedSoFar = value;
                    Compare = compareValue;
                    return entry.With(value: 0);
                });
            }
        }

        public ulong Compare
        {
            get
            {
                return compareValue;
            }
            set
            {
                if(value > initialLimit)
                {
                    throw new InvalidOperationException(CompareHigherThanLimitMessage.FormatWith(value, initialLimit));
                }
                clockSource.ExchangeClockEntryWith(CompareReachedInternal, entry =>
                {
                    compareValue = value;
                    var nextEventIn = Math.Min(compareValue - valueAccumulatedSoFar, initialLimit - valueAccumulatedSoFar);
                    valueAccumulatedSoFar += entry.Value;
                    return entry.With(period: nextEventIn - entry.Value, value: 0);
                });
            }
        }

        public virtual void Reset()
        {
            InternalReset();
        }

        public event Action CompareReached;

        protected virtual void OnCompareReached()
        {
            if(!EventEnabled)
            {
                return;
            }

            CompareReached?.Invoke();
        }

        private void CompareReachedInternal()
        {
            // since we use OneShot, timer's value is already 0 and it is disabled now
            // first we add old limit to accumulated value:
            valueAccumulatedSoFar += clockSource.GetClockEntry(CompareReachedInternal).Period;
            if(valueAccumulatedSoFar >= initialLimit && compareValue != initialLimit)
            {
                // compare value wasn't actually reached, the timer reached its limit
                // we don't trigger an event in such case
                valueAccumulatedSoFar = 0;
                clockSource.ExchangeClockEntryWith(CompareReachedInternal, entry => entry.With(period: compareValue, enabled: true));
                return;
            }
            // real compare event - then we reenable the timer with the next event marked by limit
            // which will probably be soon corrected by software
            clockSource.ExchangeClockEntryWith(CompareReachedInternal, entry => entry.With(period: initialLimit - valueAccumulatedSoFar, enabled: true));
            if(valueAccumulatedSoFar >= initialLimit)
            {
                valueAccumulatedSoFar = 0;
            }
            OnCompareReached();
        }

        private void InternalReset()
        {
            var clockEntry = new ClockEntry(initialCompare, ClockEntry.FrequencyToRatio(this, initialFrequency), CompareReachedInternal, initialEnabled, initialDirection, initialWorkMode)
            { Value = initialDirection == Direction.Ascending ? 0 : initialLimit };
            clockSource.ExchangeClockEntryWith(CompareReachedInternal, entry => clockEntry, () => clockEntry);
            valueAccumulatedSoFar = 0;
            compareValue = initialCompare;
            EventEnabled = initialEventEnabled;
        }

        private ulong valueAccumulatedSoFar;
        private ulong compareValue;

        private readonly Direction initialDirection;
        private readonly long initialFrequency;
        private readonly IClockSource clockSource;
        private readonly ulong initialLimit;
        private readonly WorkMode initialWorkMode;
        private readonly ulong initialCompare;
        private readonly bool initialEnabled;
        private readonly bool initialEventEnabled;

        private const string CompareHigherThanLimitMessage = "Compare value ({0}) cannot be higher than limit ({1}).";
    }
}

