//
// Copyright (c) 2010-2018 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Antmicro.Renode.Debugging;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Time
{
    /// <summary>
    /// Provides common base for <see cref="ITimeSource"> implementations.
    /// </summary>
    public abstract class TimeSourceBase : IdentifiableObject, ITimeSource, IDisposable
    {
        /// <summary>
        /// Creates new instance of time source.
        /// </summary>
        public TimeSourceBase()
        {
            blockingEvent = new ManualResetEvent(true);
            delayedActions = new SortedSet<DelayedTask>();
            handles = new HandlesCollection();
            sleeper = new Sleeper();
            stopwatch = Stopwatch.StartNew();
            recentlyUnblockedSlaves = new HashSet<TimeHandle>();

            hostTicksElapsed = new TimeVariantValue(10);
            virtualTicksElapsed = new TimeVariantValue(10);

            sync = new PrioritySynchronizer();

            Quantum = DefaultQuantum;
            Performance = 1;

            this.Trace();
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public virtual void Dispose()
        {
            stopwatch.Stop();
            sleeper.Cancel();
            using(sync.HighPriority)
            {
                foreach(var slave in handles.All)
                {
                    slave.Dispose();
                }
            }
        }

        /// <summary>
        /// Starts this time source and activates all associated slaves.
        /// </summary>
        /// <returns>False it the handle has already been started.</returns>
        protected bool Start()
        {
            using(sync.HighPriority)
            {
                if(isStarted)
                {
                    this.Trace("Already started");
                    return false;
                }
                isStarted = true;
                ActivateSlavesSourceSide();
                return true;
            }
        }


        /// <summary>
        /// Stops this time source and deactivates all associated slaves.
        /// </summary>
        protected void Stop()
        {
            using(sync.HighPriority)
            {
                if(!isStarted)
                {
                    this.Trace("Not started");
                    return;
                }
                DeactivateSlavesSourceSide();

                // we must wait for unblocked slaves to finish their work
                sync.WaitWhile(() => recentlyUnblockedSlaves.Count > 0, "Waiting for unblocked slaves");
                isStarted = false;
                blockingEvent.Set();
            }
        }

        /// <summary>
        /// Queues an action to execute in the nearest synced state.
        /// </summary>
        /// <param name="executeImmediately">Flag indicating if the action should be executed immediately when executed in already synced context or should it wait for the next synced state.</param>
        public void ExecuteInNearestSyncedState(Action<TimeStamp> what, bool executeImmediately = false)
        {
            lock(delayedActions)
            {
                if(isInSyncPhase && executeImmediately)
                {
                    what(new TimeStamp(ElapsedVirtualTime, Domain));
                }
                else
                {
                    delayedActions.Add(new DelayedTask(what, new TimeStamp()));
                }
            }
        }

        /// <summary>
        /// Queues an action to execute in the nearest synced state after <paramref name="when"> time point.
        /// </summary>
        /// <remarks>
        /// If the <see cref="when"> time stamp comes from other time domain it will be executed in the nearest synced state.
        /// </remarks>
        public void ExecuteInSyncedState(Action<TimeStamp> what, TimeStamp when)
        {
            lock(delayedActions)
            {
                delayedActions.Add(new DelayedTask(what, when.Domain != Domain ? new TimeStamp() : when));
            }
        }

        /// <see cref="ITimeSource.RegisterSink">
        public void RegisterSink(ITimeSink sink)
        {
            //lock(handles)
            using(sync.HighPriority)
            {
                handles.Add(new TimeHandle(this, sink) { SourceSideActive = isStarted });
            }
        }

        public IEnumerable<ITimeSink> Sinks { get { using(sync.HighPriority) { return handles.Select(x => x.TimeSink); } } }

        /// <see cref="ITimeSource.UnblockHandle"/>
        public bool UnblockHandle(TimeHandle handle)
        {
            handle.Trace("About to unblock this handle");

            if(isPaused)
            {
                this.Trace("Time source is paused - returning false");
                return false;
            }

            bool result;
            using(sync.HighPriority)
            {
                DebugHelper.Assert(handles.All.Contains(handle), "Unblocking a handle that is not registered");

                sync.WaitWhile(() => recentlyUnblockedSlaves.Contains(handle), "Wait until the previous unblocking status is read");

                handle.Trace($"Unblocking status: {(!isStarted ? "aborting" : "unblocking")}");
                result = isStarted;

                if(isStarted)
                {
                    recentlyUnblockedSlaves.Add(handle);
                    this.Trace($"UnblockHandle: Number of unblocked slaves set to: {recentlyUnblockedSlaves.Count}");
                    blockingEvent.Set();
                }
            }

            return result;
        }

        /// <see cref="ITimeSource.ReportHandleActive">
        public void ReportHandleActive()
        {
            blockingEvent.Set();
        }

        /// <see cref="ITimeSource.ReportTimeProgress">
        public void ReportTimeProgress(TimeHandle h, TimeInterval diff)
        {
            if(diff.Ticks == 0)
            {
                return;
            }

            var currentCommonElapsedTime = handles.CommonElapsedTime;
            if(currentCommonElapsedTime > previousElapsedVirtualTime)
            {
                var timeDiff = currentCommonElapsedTime - previousElapsedVirtualTime;
                this.Trace($"Reporting time passed: {timeDiff}");
                TimePassed?.Invoke(timeDiff);
                previousElapsedVirtualTime = currentCommonElapsedTime;
            }
        }

        public override string ToString()
        {
            return string.Format("Elapsed Virtual Time: {0}\nCurrentLoad: {1}\nCumulativeLoad: {2}\nState: {3}\nAdvanceImmediately: {4}\nPerformance: {5}\nQuantum: {6}",
                ElapsedVirtualTime,
                CurrentLoad,
                CumulativeLoad,
                State,
                AdvanceImmediately,
                Performance,
                Quantum);
        }

        /// <see cref="ITimeSource.Domain">
        public abstract ITimeDomain Domain { get; }

        // TODO: this name does not give a lot to a user - maybe we should rename it?
        /// <summary>
        /// Gets or sets flag indicating if the time flow should be slowed down to reflect real time or be as fast as possible.
        /// </summary>
        /// <remarks>
        /// Setting this flag to True has the same effect as setting <see cref="Performance"> to a very high value.
        /// </remarks>
        public bool AdvanceImmediately { get; set; }

        /// <summary>
        /// Gets current state of this time source.
        /// </summary>
        public TimeSourceState State { get; private set; }

        // TODO: do not allow to set Quantum of 0
        /// <see cref="ITimeSource.Quantum">
        public TimeInterval Quantum { get; set; }

        /// <summary>
        /// Gets or sets a scaling value for the ratio of virtual to real time flow.
        /// </summary>
        /// <remarks>
        /// Value 1 means that virtual time should pass at the same pace as real time.
        /// Value 0.5 means that each second of virtual time will take two seconds of the real time.
        /// Value 2 means that each second of virtual time will take half a second of the real time.
        /// CPU performance puts a limit to an effective value of this parameter (see <see cref="CurrentLoad">).
        /// This value can be temporarily overridden by setting <see cref="AdvanceImmediately">.
        /// </remarks>
        public double Performance { get; set; }

        /// <summary>
        /// Gets the value representing current load, i.e., value indicating how much time the emulation spends sleeping in order to match the expected <see cref="Performance">.
        /// </summary>
        /// <remarks>
        /// Value 1 means that there is no sleeping, i.e., it is not possible to execute faster. Value > 1 means that the execution is slower than expected. Value < 1 means that increasing <see cref="Performance"> will lead to faster execution.
        /// This value is calculated as an average of 10 samples.
        /// </remarks>
        public double CurrentLoad { get { lock(hostTicksElapsed) { return hostTicksElapsed.AverageValue * 1.0 / virtualTicksElapsed.AverageValue; } } }

        /// <summary>
        /// Gets the value representing load (see <see cref="CurrentLoad">) calculated from all samples.
        /// </summary>
        public double CumulativeLoad { get { lock(hostTicksElapsed) { return hostTicksElapsed.CumulativeValue * 1.0 / virtualTicksElapsed.CumulativeValue; } } }

        /// <summary>
        /// Gets the amount of virtual time elapsed from the perspective of this time source.
        /// </summary>
        /// <remarks>
        /// This is a minimum value of all associated <see cref="TimeHandle.TotalElapsedTime">.
        /// </remarks>
        public TimeInterval ElapsedVirtualTime { get { return TimeInterval.FromTicks(virtualTicksElapsed.CumulativeValue); } }

        /// <summary>
        /// Gets the virtual time point of the nearest synchronization of all associated <see cref="ITimeHandle">.
        /// </summary>
        public TimeInterval NearestSyncPoint { get; protected set; }

        /// <summary>
        /// Gets the number of synchronizations points reached so far.
        /// </summary>
        public long NumberOfSyncPoints { get; private set; }

        /// <summary>
        /// Action to be executed on every synchronization point.
        /// </summary>
        public event Action<TimeInterval> SyncHook;

        /// <summary>
        /// An event called when the time source is blocked by at least one of the sinks.
        /// </summary>
        public event Action BlockHook;

        public event Action<TimeInterval> TimePassed;

        /// <summary>
        /// Execute one iteration of time-granting loop.
        /// </summary>
        /// <remarks>
        /// The steps are as follows:
        /// (1) remove and forget all slave handles that requested detaching
        /// (2) check if there are any blocked slaves; if so DO NOT grant a time interval
        /// (2.1) if there are no blocked slaves grant a new time interval to every slave
        /// (3) wait for all slaves that are relevant in this execution (it can be either all slaves or just blocked ones) until they report back
        /// (4) (optional) sleep if the virtual time passed faster than a real one; this step is executed if <see cref="AdvanceImmediately"> is not set and <see cref="Performance"> is low enough
        /// (5) update elapsed virtual time
        /// (6) execute sync hook and delayed actions if any
        /// </remarks>
        /// <param name="virtualTimeElapsed">Contains the amount of virtual time that passed during execution of this method. It is the minimal value reported by a slave (i.e, some slaves can report higher/lower values).</param>
        /// <returns>
        /// True if sync point has just been reached or False if the execution has been blocked.
        /// </returns>
        protected bool InnerExecute(out TimeInterval virtualTimeElapsed)
        {
            DebugHelper.Assert(NearestSyncPoint.Ticks >= ElapsedVirtualTime.Ticks, "Nearest sync point set in the past");

            isBlocked = false;
            var quantum = NearestSyncPoint - ElapsedVirtualTime;
            this.Trace($"Starting a loop with #{quantum.Ticks} ticks");

            virtualTimeElapsed = TimeInterval.Empty;
            State = TimeSourceState.ReportingElapsedTime;
            using(sync.LowPriority)
            {
                if((isPaused || !isStarted) && recentlyUnblockedSlaves.Count == 0)
                {
                    // the time source is not started and it has not acknowledged any unblocks - it means no one is currently working
                    DebugHelper.Assert(handles.All.All(x => !x.SourceSideActive), "No source side active slaves were expected at this point.");

                    State = TimeSourceState.Idle;
                    EnterBlockedState();
                    return false;
                }

                handles.LatchAllAndCollectGarbage();
                var shouldGrantTime = handles.AreAllReadyForNewGrant;

                this.Trace($"Iteration start: slaves left {handles.ActiveCount}; will we try to grant time? {shouldGrantTime}");
                elapsedAtLastGrant = stopwatch.Elapsed;
                if(handles.ActiveCount > 0)
                {
                    if(shouldGrantTime && quantum != TimeInterval.Empty)
                    {
                        this.Trace($"Granting {quantum.Ticks} ticks");
                        // inform all slaves about elapsed time
                        foreach(var slave in handles)
                        {
                            slave.GrantTimeInterval(quantum);
                        }
                    }

                    // in case we did not grant any time due to quantum being empty, we must not call wait as well
                    if(!(shouldGrantTime && quantum == TimeInterval.Empty))
                    {
                        this.Trace("Waiting for slaves");
                        // wait for everyone to report back
                        State = TimeSourceState.WaitingForReportBack;
                        TimeInterval? minInterval = null;
                        foreach(var slave in handles.WithLinkedListNode)
                        {
                            var result = slave.Value.WaitUntilDone(out var usedInterval);
                            if(!result.IsDone)
                            {
                                EnterBlockedState();
                            }

                            handles.UpdateHandle(slave);

                            if(result.IsUnblockedRecently)
                            {
                                Antmicro.Renode.Debugging.DebugHelper.Assert(recentlyUnblockedSlaves.Contains(slave.Value), $"Expected slave to be in {nameof(recentlyUnblockedSlaves)} collection.");
                                recentlyUnblockedSlaves.Remove(slave.Value);
                                this.Trace($"Number of unblocked slaves set to {recentlyUnblockedSlaves.Count}");
                                if(recentlyUnblockedSlaves.Count == 0)
                                {
                                    sync.Pulse();
                                }
                            }
                            if(minInterval == null || minInterval > slave.Value.TotalElapsedTime)
                            {
                                minInterval = slave.Value.TotalElapsedTime;
                            }
                        }

                        if(minInterval != null)
                        {
                            virtualTimeElapsed = minInterval.Value - ElapsedVirtualTime;
                        }
                    }
                }
                else
                {
                    this.Trace($"There are no slaves, updating VTE by {quantum.Ticks}");
                    // if there are no slaves just make the time pass
                    virtualTimeElapsed = quantum;

                    TimePassed?.Invoke(quantum);
                }

                handles.UnlatchAll();

                State = TimeSourceState.Sleeping;
                var elapsedThisTime = stopwatch.Elapsed - elapsedAtLastGrant;
                if(!AdvanceImmediately)
                {
                    var scaledVirtualTicksElapsed = virtualTimeElapsed.WithScaledTicks(1 / Performance).ToTimeSpan() - elapsedThisTime;
                    sleeper.Sleep(scaledVirtualTicksElapsed);
                }

                lock(hostTicksElapsed)
                {
                    this.Trace($"Updating virtual time by {virtualTimeElapsed.InMicroseconds} us");
                    this.virtualTicksElapsed.Update(virtualTimeElapsed.InMicroseconds);
                    this.hostTicksElapsed.Update(elapsedThisTime.InMicroseconds());
                }
            }

            if(!isBlocked)
            {
                ExecuteSyncPhase();
            }
            else
            {
                BlockHook?.Invoke();
            }

            State = TimeSourceState.Idle;

            this.Trace($"The end of {nameof(InnerExecute)} with result={!isBlocked}");
            return !isBlocked;
        }

        /// <summary>
        /// Activates all slaves from source side perspective, i.e., tells them that there will be time granted in the nearest future.
        /// </summary>
        protected void ActivateSlavesSourceSide(bool state = true)
        {
            using(sync.HighPriority)
            {
                foreach(var slave in handles.All)
                {
                    slave.SourceSideActive = state;
                }
            }
        }

        /// <summary>
        /// Deactivates all slaves from  source side perspective, i.e., tells them that there will be no grants in the nearest future.
        /// </summary>
        protected void DeactivateSlavesSourceSide()
        {
            ActivateSlavesSourceSide(false);
        }

        /// <summary>
        /// Suspends an execution of the calling thread if blocking event is set.
        /// </summary>
        /// <remarks>
        /// This is just to improve performance of the emulation - avoid spinning when any of the sinks is blocking.
        /// </remarks>
        protected void WaitIfBlocked()
        {
            // this 'if' statement and 'canBeBlocked' variable are here for performance only
            // calling `WaitOne` in every iteration can cost a lot of time;
            // waiting on 'blockingEvent' is not required for the time framework to work properly,
            // but decreases cpu usage when any handle is known to be blocking
            if(isBlocked)
            {
                // value of 'isBlocked' will be reevaluated in 'ExecuteInner' method
                blockingEvent.WaitOne(100);
                // this parameter here is kind of a hack:
                // in theory we could use an overload without timeout,
                // but there is a bug and sometimes it blocks forever;
                // this is just a simple workaround
            }
        }

        /// <summary>
        /// Sets blocking event to true.
        /// </summary>
        private void EnterBlockedState()
        {
            isBlocked = true;
            // we cannot reset the event if there are some unblocked slaves as it would overwrite `Set` executed by them
            if(recentlyUnblockedSlaves.Count == 0)
            {
                blockingEvent.Reset();
            }
        }

        /// <summary>
        /// Executes sync phase actions in a safe state.
        /// </summary>
        private void ExecuteSyncPhase()
        {
            this.Trace($"Before syncpoint, EVT={ElapsedVirtualTime.Ticks}, NSP={NearestSyncPoint.Ticks}");
            // if no slave returned blocking state, sync point should be reached
            DebugHelper.Assert(ElapsedVirtualTime == NearestSyncPoint);
            this.Trace($"We are at the sync point #{NumberOfSyncPoints}");

            State = TimeSourceState.ExecutingSyncHook;

            DelayedTask[] tasksAsArray;
            lock(delayedActions)
            {
                isInSyncPhase = true;
                SyncHook?.Invoke(ElapsedVirtualTime);

                State = TimeSourceState.ExecutingDelayedActions;
                var timeNow = new TimeStamp(ElapsedVirtualTime, Domain);
                var tasksToExecute = delayedActions.GetViewBetween(DelayedTask.Zero, new DelayedTask(null, timeNow));
                tasksAsArray = tasksToExecute.ToArray();
                tasksToExecute.Clear();

                foreach(var task in tasksAsArray)
                {
                    task.What(timeNow);
                }
                isInSyncPhase = false;
            }
            NumberOfSyncPoints++;
        }

        protected bool isStarted;
        protected bool isPaused;

        protected readonly HandlesCollection handles;
        protected readonly Stopwatch stopwatch;

        [Antmicro.Migrant.Constructor(true)]
        private ManualResetEvent blockingEvent;

        private TimeSpan elapsedAtLastGrant;
        protected HashSet<TimeHandle> recentlyUnblockedSlaves;
        private bool isBlocked;
        private bool isInSyncPhase;

        private TimeInterval previousElapsedVirtualTime;
        private readonly TimeVariantValue virtualTicksElapsed;
        private readonly TimeVariantValue hostTicksElapsed;
        private readonly SortedSet<DelayedTask> delayedActions;
        private readonly Sleeper sleeper;
        // we use special object for locking as it was observed that idle dispatcher thread can starve other threads when using simple lock(object)
        private readonly PrioritySynchronizer sync;

        private static readonly TimeInterval DefaultQuantum = TimeInterval.FromMilliseconds(10);

        /// <summary>
        /// Represents a time-variant value.
        /// </summary>
        private class TimeVariantValue
        {
            public TimeVariantValue(int size)
            {
                buffer = new ulong[size];
            }

            /// <summary>
            /// Updates the <see cref="RawValue">.
            /// </summary>
            public void Update(ulong value)
            {
                RawValue = value;
                CumulativeValue += value;

                partialSum += value;
                partialSum -= buffer[position];
                buffer[position] = value;
                position = (position + 1) % buffer.Length;
            }

            public ulong RawValue { get; private set; }

            /// <summary>
            /// Returns average of <see cref="RawValues"> over the last <see cref="size"> samples.
            /// </summary>
            public ulong AverageValue { get { return  (ulong)(partialSum / (ulong)buffer.Length); } }

            /// <summary>
            /// Returns total sum of all <see cref="RawValues"> so far.
            /// </summary>
            public ulong CumulativeValue { get; private set; }

            private readonly ulong[] buffer;
            private int position;
            private ulong partialSum;
        }

        /// <summary>
        /// Represents a task that is scheduled for execution in the future.
        /// </summary>
        private struct DelayedTask : IComparable<DelayedTask>
        {
            static DelayedTask()
            {
                Zero = new DelayedTask();
            }

            public DelayedTask(Action<TimeStamp> what, TimeStamp when) : this()
            {
                What = what;
                When = when;
                id = Interlocked.Increment(ref Id);
            }

            public int CompareTo(DelayedTask other)
            {
                var result = When.TimeElapsed.CompareTo(other.When.TimeElapsed);
                return result != 0 ? result : id.CompareTo(other.id);
            }

            public Action<TimeStamp> What { get; private set; }

            public TimeStamp When { get; private set; }

            public static DelayedTask Zero { get; private set; }

            private readonly int id;
            private static int Id;
        }

        /// <summary>
        /// Allows locking without starvation.
        /// </summary>
        private class PrioritySynchronizer : IdentifiableObject, IDisposable
        {
            public PrioritySynchronizer()
            {
                innerLock = new object();
            }

            /// <summary>
            /// Used to obtain lock with low priority.
            /// </summary>
            /// <remarks>
            /// Any thread already waiting on the lock with high priority is guaranteed to obtain it prior to this one.
            /// There are no guarantees for many threads with the same priority.
            /// </remarks>
            public PrioritySynchronizer LowPriority
            {
                get
                {
                    // here we assume that `highPriorityRequestPending` will be reset soon,
                    // so there is no point of using more complicated synchronization methods
                    while(highPriorityRequestPendingCounter > 0);
                    Monitor.Enter(innerLock);

                    return this;
                }
            }

            /// <summary>
            /// Used to obtain lock with high priority.
            /// </summary>
            /// <remarks>
            /// It is guaranteed that the thread wanting to lock with high priority will not wait indefinitely if all other threads lock with low priority.
            /// There are no guarantees for many threads with the same priority.
            /// </remarks>
            public PrioritySynchronizer HighPriority
            {
                get
                {
                    Interlocked.Increment(ref highPriorityRequestPendingCounter);
                    Monitor.Enter(innerLock);
                    Interlocked.Decrement(ref highPriorityRequestPendingCounter);
                    return this;
                }
            }

            public void Dispose()
            {
                Monitor.Exit(innerLock);
            }

            public void WaitWhile(Func<bool> condition, string reason)
            {
                innerLock.WaitWhile(condition, reason);
            }

            public void Pulse()
            {
                Monitor.PulseAll(innerLock);
            }

            private readonly object innerLock;
            private volatile int highPriorityRequestPendingCounter;
        }
    }
}
