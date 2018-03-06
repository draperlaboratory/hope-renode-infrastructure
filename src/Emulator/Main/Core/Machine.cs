//
// Copyright (c) 2010-2018 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Linq;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Utilities.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Antmicro.Migrant;
using System.Threading;
using Antmicro.Renode.Time;
using System.Text;
using Antmicro.Renode.Peripherals.CPU;
using System.Reflection;
using Antmicro.Renode.Utilities;
using Antmicro.Renode.UserInterface;
using Antmicro.Renode.EventRecording;
using System.IO;
using System.Diagnostics;

namespace Antmicro.Renode.Core
{
    public class Machine : IEmulationElement, IDisposable
    {
        public Machine()
        {
            LocalTimeSource = new SlaveTimeSource(this);
            LocalTimeSource.TimePassed += HandleTimeProgress;

            collectionSync = new object();
            pausingSync = new object();
            disposedSync = new object();
            clockSource = new BaseClockSource();
            localNames = new Dictionary<IPeripheral, string>();
            PeripheralsGroups = new PeripheralsGroupsManager(this);
            ownLifes = new HashSet<IHasOwnLife>();
            pausedState = new PausedState(this);
            SystemBus = new SystemBus(this);
            registeredPeripherals = new MultiTree<IPeripheral, IRegistrationPoint>(SystemBus);
            userStateHook = delegate
            {
            };
            userState = string.Empty;
            SetLocalName(SystemBus, SystemBusName);
        }

        public IEnumerable<IPeripheral> GetParentPeripherals(IPeripheral peripheral)
        {
            var node = registeredPeripherals.TryGetNode(peripheral);
            return node == null ? new IPeripheral[0] : node.Parents.Select(x => x.Value).Distinct();
        }

        public IEnumerable<IPeripheral> GetChildrenPeripherals(IPeripheral peripheral)
        {
            var node = registeredPeripherals.TryGetNode(peripheral);
            return node == null ? new IPeripheral[0] : node.Children.Select(x => x.Value).Distinct();
        }

        public IEnumerable<IRegistrationPoint> GetPeripheralRegistrationPoints(IPeripheral parentPeripheral, IPeripheral childPeripheral)
        {
            var parentNode = registeredPeripherals.TryGetNode(parentPeripheral);
            return parentNode == null ? new IRegistrationPoint[0] : parentNode.GetConnectionWays(childPeripheral);
        }

        public void RegisterAsAChildOf(IPeripheral peripheralParent, IPeripheral peripheralChild, IRegistrationPoint registrationPoint)
        {
            Register(peripheralChild, registrationPoint, peripheralParent);
        }

        public void UnregisterAsAChildOf(IPeripheral peripheralParent, IPeripheral peripheralChild)
        {
            lock(collectionSync)
            {
                CollectGarbageStamp();
                IPeripheralsGroup group;
                if(PeripheralsGroups.TryGetActiveGroupContaining(peripheralChild, out group))
                {
                    throw new RegistrationException(string.Format("Given peripheral is a member of '{0}' peripherals group and cannot be directly removed.", group.Name));
                }

                var parentNode = registeredPeripherals.GetNode(peripheralParent);
                parentNode.RemoveChild(peripheralChild);
                EmulationManager.Instance.CurrentEmulation.BackendManager.HideAnalyzersFor(peripheralChild);
                CollectGarbage();
            }
        }

        public void UnregisterAsAChildOf(IPeripheral peripheralParent, IRegistrationPoint registrationPoint)
        {
            lock(collectionSync)
            {
                CollectGarbageStamp();
                try
                {
                    var parentNode = registeredPeripherals.GetNode(peripheralParent);
                    IPeripheral removedPeripheral = null;
                    parentNode.RemoveChild(registrationPoint, p =>
                    {
                        IPeripheralsGroup group;
                        if(PeripheralsGroups.TryGetActiveGroupContaining(p, out group))
                        {
                            throw new RegistrationException(string.Format("Given peripheral is a member of '{0}' peripherals group and cannot be directly removed.", group.Name));
                        }
                        removedPeripheral = p;
                        return true;
                    });
                    CollectGarbage();
                    if(removedPeripheral != null && registeredPeripherals.TryGetNode(removedPeripheral) == null)
                    {
                        EmulationManager.Instance.CurrentEmulation.BackendManager.HideAnalyzersFor(removedPeripheral);
                    }
                }
                catch(RegistrationException)
                {
                    CollectGarbage();
                    throw;
                }
            }
        }

        public void UnregisterFromParent(IPeripheral peripheral)
        {
            InnerUnregisterFromParent(peripheral);
            OnMachinePeripheralsChanged(peripheral, PeripheralsChangedEventArgs.PeripheralChangeType.CompleteRemoval);
        }

        public IEnumerable<T> GetPeripheralsOfType<T>()
        {
            return GetPeripheralsOfType(typeof(T)).Cast<T>();
        }

        public IEnumerable<IPeripheral> GetPeripheralsOfType(Type t)
        {
            lock(collectionSync)
            {
                return registeredPeripherals.Values.Where(t.IsInstanceOfType).ToList();
            }
        }

        public IEnumerable<PeripheralTreeEntry> GetRegisteredPeripherals()
        {
            var result = new List<PeripheralTreeEntry>();
            lock(collectionSync)
            {
                registeredPeripherals.TraverseWithConnectionWaysParentFirst((currentNode, regPoint, parent, level) =>
                {
                    string localName;
                    TryGetLocalName(currentNode.Value, out localName);
                    result.Add(new PeripheralTreeEntry(currentNode.Value, parent, currentNode.Value.GetType(), regPoint, localName, level));
                }, 0);
            }
            return result;
        }

        public bool TryGetByName<T>(string name, out T peripheral, out string longestMatch) where T : class, IPeripheral
        {
            if(name == null)
            {
                longestMatch = string.Empty;
                peripheral = null;
                return false;
            }
            var splitPath = name.Split(new [] { '.' }, 2);
            if(splitPath.Length == 1 && name == SystemBusName)
            {
                longestMatch = name;
                peripheral = (T)(IPeripheral)SystemBus;
                return true;
            }

            if(splitPath[0] != SystemBusName)
            {
                longestMatch = string.Empty;
                peripheral = null;
                return false;
            }

            MultiTreeNode<IPeripheral, IRegistrationPoint> result;
            if(TryFindSubnodeByName(registeredPeripherals.GetNode(SystemBus), splitPath[1], out result, SystemBusName, out longestMatch))
            {
                peripheral = (T)result.Value;
                return true;
            }
            peripheral = null;
            return false;
        }

        public bool TryGetByName<T>(string name, out T peripheral) where T : class, IPeripheral
        {
            string fake;
            return TryGetByName(name, out peripheral, out fake);
        }

        public string GetLocalName(IPeripheral peripheral)
        {
            string result;
            lock(collectionSync)
            {
                if(!TryGetLocalName(peripheral, out result))
                {
                    throw new KeyNotFoundException();
                }
                return result;
            }
        }

        public bool TryGetLocalName(IPeripheral peripheral, out string name)
        {
            lock(collectionSync)
            {
                return localNames.TryGetValue(peripheral, out name);
            }
        }

        public void SetLocalName(IPeripheral peripheral, string name)
        {
            if(string.IsNullOrEmpty(name))
            {
                throw new RecoverableException("The name of the peripheral cannot be null nor empty.");
            }
            lock(collectionSync)
            {
                if(!registeredPeripherals.ContainsValue(peripheral))
                {
                    throw new RecoverableException("Cannot name peripheral which is not registered.");
                }
                if(localNames.ContainsValue(name))
                {
                    throw new RecoverableException(string.Format("Given name '{0}' is already used.", name));
                }
                localNames[peripheral] = name;
            }

            var pc = PeripheralsChanged;
            if(pc != null)
            {
                pc(this, new PeripheralsChangedEventArgs(peripheral, PeripheralsChangedEventArgs.PeripheralChangeType.NameChanged));
            }
        }

        public IEnumerable<string> GetAllNames()
        {
            var nameSegments = new AutoResizingList<string>();
            var names = new List<string>();
            lock(collectionSync)
            {
                registeredPeripherals.TraverseParentFirst((x, y) =>
                {
                    if(!localNames.ContainsKey(x))
                    {
                        // unnamed node
                        return;
                    }
                    var localName = localNames[x];
                    nameSegments[y] = localName;
                    var globalName = new StringBuilder();
                    for(var i = 0; i < y; i++)
                    {
                        globalName.Append(nameSegments[i]);
                        globalName.Append(PathSeparator);
                    }
                    globalName.Append(localName);
                    names.Add(globalName.ToString());
                }, 0);
            }
            return new ReadOnlyCollection<string>(names);
        }

        public bool TryGetAnyName(IPeripheral peripheral, out string name)
        {
            var names = GetNames(peripheral);
            if(names.Count > 0)
            {
                name = names[0];
                return true;
            }
            name = null;
            return false;
        }

        public string GetAnyNameOrTypeName(IPeripheral peripheral)
        {
            string name;
            if(!TryGetAnyName(peripheral, out name))
            {
                var managedThread = peripheral as IManagedThread;
                return managedThread != null ? managedThread.ToString() : peripheral.GetType().Name;
            }
            return name;
        }

        public bool IsRegistered(IPeripheral peripheral)
        {
            lock(collectionSync)
            {
                return registeredPeripherals.ContainsValue(peripheral);
            }
        }

        public IDisposable ObtainPausedState()
        {
            return pausedState.Enter();
        }

        public void Start()
        {
            lock(pausingSync)
            {
                switch(state)
                {
                case State.Started:
                    return;
                case State.Paused:
                    Resume();
                    return;
                }
                machineStartedAt = CustomDateTime.Now;
                foreach(var ownLife in ownLifes.OrderBy(x => x is ICPU ? 1 : 0))
                {
                    this.NoisyLog("Starting {0}.", GetNameForOwnLife(ownLife));
                    ownLife.Start();
                }
                LocalTimeSource.Resume();
                this.Log(LogLevel.Info, "Machine started.");
                state = State.Started;
                var machineStarted = StateChanged;
                if(machineStarted != null)
                {
                    machineStarted(this, new MachineStateChangedEventArgs(MachineStateChangedEventArgs.State.Started));
                }
            }
        }

        public void Pause()
        {
            lock(pausingSync)
            {
                switch(state)
                {
                case State.Paused:
                    return;
                case State.NotStarted:
                    goto case State.Paused;
                }
                LocalTimeSource.Pause();
                foreach(var ownLife in ownLifes.OrderBy(x => x is ICPU ? 0 : 1))
                {
                    var ownLifeName = GetNameForOwnLife(ownLife);
                    this.NoisyLog("Pausing {0}.", ownLifeName);
                    ownLife.Pause();
                    this.NoisyLog("{0} paused.", ownLifeName);
                }
                state = State.Paused;
                var machinePaused = StateChanged;
                if(machinePaused != null)
                {
                    machinePaused(this, new MachineStateChangedEventArgs(MachineStateChangedEventArgs.State.Paused));
                }
                this.Log(LogLevel.Info, "Machine paused.");
            }
        }

        public void Reset()
        {
            lock(pausingSync)
            {
                if(state == State.NotStarted)
                {
                    this.DebugLog("Reset request: doing nothing, because system is not started.");
                    return;
                }
                using(ObtainPausedState())
                {
                    foreach(var resetable in registeredPeripherals.Distinct())
                    {
                        if(resetable == this)
                        {
                            continue;
                        }
                        resetable.Reset();
                    }
                    var machineReset = MachineReset;
                    if(machineReset != null)
                    {
                        machineReset(this);
                    }
                }
            }
        }

        public void Dispose()
        {
            lock(disposedSync)
            {
                if(alreadyDisposed)
                {
                    return;
                }
                alreadyDisposed = true;
            }
            Pause();
            if(recorder != null)
            {
                recorder.Dispose();
            }
            if(player != null)
            {
                player.Dispose();
                LocalTimeSource.SyncHook -= player.Play;
            }

            // ordering below is due to the fact that the CPU can use other peripherals, e.g. Memory so it should be disposed last
            foreach(var peripheral in GetPeripheralsOfType<IDisposable>().OrderBy(x => x is ICPU ? 0 : 1))
            {
                this.DebugLog("Disposing {0}.", GetAnyNameOrTypeName((IPeripheral)peripheral));
                peripheral.Dispose();
            }
            LocalTimeSource.Dispose();
            this.Log(LogLevel.Info, "Disposed.");
            var disposed = StateChanged;
            if(disposed != null)
            {
                disposed(this, new MachineStateChangedEventArgs(MachineStateChangedEventArgs.State.Disposed));
            }

            EmulationManager.Instance.CurrentEmulation.BackendManager.HideAnalyzersFor(this);
        }

        public IManagedThread ObtainManagedThread(Action action, int frequency)
        {
            var ce = new ClockEntry(1, ClockEntry.FrequencyToRatio(this, frequency), action, false);
            ClockSource.AddClockEntry(ce);
            return new ManagedThreadWrappingClockEntry(ClockSource, action);
        }

        private class ManagedThreadWrappingClockEntry : IManagedThread
        {
            public ManagedThreadWrappingClockEntry(IClockSource cs, Action action)
            {
                clockSource = cs;
                this.action = action;
            }

            public void Dispose()
            {
                clockSource.RemoveClockEntry(action);
            }

            public void Start()
            {
                clockSource.ExchangeClockEntryWith(action, x => x.With(enabled: true));
            }

            public void Stop()
            {
                clockSource.ExchangeClockEntryWith(action, x => x.With(enabled: false));
            }

            private readonly IClockSource clockSource;
            private readonly Action action;
        }

        private BaseClockSource clockSource;
        public IClockSource ClockSource { get { return clockSource; } }

        [UiAccessible]
        public string[,] GetClockSourceInfo()
        {
            var entries = ClockSource.GetAllClockEntries();

            var table = new Table().AddRow("Owner", "Enabled", "Frequency", "Limit", "Event frequency", "Event period");
            table.AddRows(entries, x =>
            {
                var owner = x.Handler.Target;
                var ownerAsPeripheral = owner as IPeripheral;
                return ownerAsPeripheral != null ? GetAnyNameOrTypeName(ownerAsPeripheral) : owner.GetType().Name;
            },
                x => x.Enabled.ToString(),
                x => Misc.NormalizeDecimal(x.Frequency) + "Hz",
                x => x.Period.ToString(),
                x => Misc.NormalizeDecimal(x.Frequency / x.Period) + "Hz",
                x => Misc.NormalizeDecimal(1.0 / (x.Frequency / x.Period)) + "s"
            );
            return table.ToArray();
        }

        public DateTime GetRealTimeClockBase()
        {
            switch(RealTimeClockMode)
            {
            case RealTimeClockMode.VirtualTime:
                return new DateTime(1970, 1, 1) + ElapsedVirtualTime.TimeElapsed.ToTimeSpan();
            case RealTimeClockMode.VirtualTimeWithHostBeginning:
                return machineStartedAt + ElapsedVirtualTime.TimeElapsed.ToTimeSpan();
            default:
                throw new ArgumentOutOfRangeException();
            }
        }

        public void AttachGPIO(IPeripheral source, int sourceNumber, IGPIOReceiver destination, int destinationNumber, int? localReceiverNumber = null)
        {
            var sourceByNumber = source as INumberedGPIOOutput;
            IGPIO igpio;
            if(sourceByNumber == null)
            {
                throw new RecoverableException("Source peripheral cannot be connected by number.");
            }
            if(!sourceByNumber.Connections.TryGetValue(sourceNumber, out igpio))
            {
                throw new RecoverableException(string.Format("Source peripheral has no GPIO number: {0}", source));
            }
            var actualDestination = GetActualReceiver(destination, localReceiverNumber);
            igpio.Connect(actualDestination, destinationNumber);
        }

        public void AttachGPIO(IPeripheral source, IGPIOReceiver destination, int destinationNumber, int? localReceiverNumber = null)
        {
            var connectors = source.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => typeof(GPIO).IsAssignableFrom(x.PropertyType)).ToArray();
            var actualDestination = GetActualReceiver(destination, localReceiverNumber);
            DoAttachGPIO(source, connectors, actualDestination, destinationNumber);
        }

        public void AttachGPIO(IPeripheral source, string connectorName, IGPIOReceiver destination, int destinationNumber, int? localReceiverNumber = null)
        {
            var connectors = source.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.Name == connectorName && typeof(GPIO).IsAssignableFrom(x.PropertyType)).ToArray();
            var actualDestination = GetActualReceiver(destination, localReceiverNumber);
            DoAttachGPIO(source, connectors, actualDestination, destinationNumber);
        }

        public void HandleTimeDomainEvent<T>(Action<T> handler, T handlerArgument, TimeStamp eventTime, Action postAction = null)
        {
            LocalTimeSource.ExecuteInSyncedState(ts =>
            {
                HandleTimeDomainEvent(handler, handlerArgument, ts.Domain == LocalTimeSource.Domain);
                postAction?.Invoke();
            }, eventTime);
        }

        public void HandleTimeDomainEvent<T1, T2>(Action<T1, T2> handler, T1 handlerArgument1, T2 handlerArgument2, TimeStamp eventTime, Action postAction = null)
        {
            LocalTimeSource.ExecuteInSyncedState(ts =>
            {
                HandleTimeDomainEvent(handler, handlerArgument1, handlerArgument2, ts.Domain == LocalTimeSource.Domain);
                postAction?.Invoke();
            }, eventTime);
        }

        public void HandleTimeDomainEvent<T>(Action<T> handler, T handlerArgument, bool timeDomainInternalEvent)
        {
            ReportForeignEventInner(
                recorder == null ? (Action<TimeInterval, bool>)null : (timestamp, eventNotFromDomain) => recorder.Record(handlerArgument, handler, timestamp, eventNotFromDomain),
                () => handler(handlerArgument), timeDomainInternalEvent);
        }

        public void HandleTimeDomainEvent<T1, T2>(Action<T1, T2> handler, T1 handlerArgument1, T2 handlerArgument2, bool timeDomainInternalEvent)
        {
            ReportForeignEventInner(
                recorder == null ? (Action<TimeInterval, bool>)null : (timestamp, eventNotFromDomain) => recorder.Record(handlerArgument1, handlerArgument2, handler, timestamp, eventNotFromDomain),
                () => handler(handlerArgument1, handlerArgument2), timeDomainInternalEvent);
        }

        public void RecordTo(string fileName, RecordingBehaviour recordingBehaviour)
        {
            recorder = new Recorder(File.Create(fileName), this, recordingBehaviour);
        }

        public void PlayFrom(string fileName)
        {
            player = new Player(File.OpenRead(fileName), this);
            LocalTimeSource.SyncHook += player.Play;
        }

        public void AddUserStateHook(Func<string, bool> predicate, Action<string> hook)
        {
            userStateHook += currentState =>
            {
                if(predicate(currentState))
                {
                    hook(currentState);
                }
            };
        }

        public override string ToString()
        {
            return EmulationManager.Instance.CurrentEmulation[this];
        }

        public IPeripheral this[string name]
        {
            get
            {
                return GetByName(name);
            }
        }

        public string UserState
        {
            get
            {
                return userState;
            }
            set
            {
                userState = value;
                userStateHook(userState);
            }
        }

        public SystemBus SystemBus { get; private set; }

        public IPeripheralsGroupsManager PeripheralsGroups { get; private set; }

        public Platform Platform { get; set; }

        public bool IsPaused
        {
            get
            {
                // locking on pausingSync can couse deadlock (when mach.Start() and AllMachineStarted are called together)
                var stateCopy = state;
                return stateCopy == State.Paused || stateCopy == State.NotStarted;
            }
        }

        public TimeStamp ElapsedVirtualTime
        {
            get
            {
                return new TimeStamp(LocalTimeSource.ElapsedVirtualTime, LocalTimeSource.Domain);
            }
        }

        public SlaveTimeSource LocalTimeSource { get; private set; }

        public RealTimeClockMode RealTimeClockMode { get; set; }

        [field: Transient]
        public event Action<Machine, MachineStateChangedEventArgs> StateChanged;
        [field: Transient]
        public event Action<Machine, PeripheralsChangedEventArgs> PeripheralsChanged;
        [field: Transient]
        public event Action<Machine> MachineReset;

        public const char PathSeparator = '.';
        public const string SystemBusName = "sysbus";
        public const string UnnamedPeripheral = "[no-name]";
        public const string MachineKeyword = "machine";

        private void InnerUnregisterFromParent(IPeripheral peripheral)
        {
            using(ObtainPausedState())
            {
                lock(collectionSync)
                {
                    var parents = GetParents(peripheral);
                    if(parents.Count > 1)
                    {
                        throw new RegistrationException(string.Format("Given peripheral is connected to more than one different parent, at least '{0}' and '{1}'.",
                            parents.Select(x => GetAnyNameOrTypeName(x)).Take(2).ToArray()));
                    }

                    IPeripheralsGroup group;
                    if(PeripheralsGroups.TryGetActiveGroupContaining(peripheral, out group))
                    {
                        throw new RegistrationException(string.Format("Given peripheral is a member of '{0}' peripherals group and cannot be directly removed.", group.Name));
                    }

                    var parent = parents.FirstOrDefault();
                    if(parent == null)
                    {
                        throw new RecoverableException(string.Format("Cannot unregister peripheral {0} since it does not have any parent.", peripheral));
                    }
                    ((dynamic)parent).Unregister((dynamic)peripheral);
                    EmulationManager.Instance.CurrentEmulation.BackendManager.HideAnalyzersFor(peripheral);
                }
            }
        }

        private void Register(IPeripheral peripheral, IRegistrationPoint registrationPoint, IPeripheral parent)
        {
            using(ObtainPausedState())
            {
                Action executeAfterLock = null;
                lock(collectionSync)
                {
                    var parentNode = registeredPeripherals.GetNode(parent);
                    parentNode.AddChild(peripheral, registrationPoint);
                    var ownLife = peripheral as IHasOwnLife;
                    if(ownLife != null)
                    {
                        ownLifes.Add(ownLife);
                        if(state == State.Paused)
                        {
                            executeAfterLock = delegate
                            {
                                ownLife.Start();
                                ownLife.Pause();
                            };
                        }
                    }
                }
                if(executeAfterLock != null)
                {
                    executeAfterLock();
                }

                if(peripheral is ITimeSink timeSink)
                {
                    LocalTimeSource.RegisterSink(timeSink);
                }
            }

            OnMachinePeripheralsChanged(peripheral, PeripheralsChangedEventArgs.PeripheralChangeType.Addition);
            EmulationManager.Instance.CurrentEmulation.BackendManager.TryCreateBackend(peripheral);
        }

        private void OnMachinePeripheralsChanged(IPeripheral peripheral, PeripheralsChangedEventArgs.PeripheralChangeType operation)
        {
            var mpc = PeripheralsChanged;
            if(mpc != null)
            {
                mpc(this, new PeripheralsChangedEventArgs(peripheral, operation));
            }
        }

        private bool TryFindSubnodeByName(MultiTreeNode<IPeripheral, IRegistrationPoint> from, string path, out MultiTreeNode<IPeripheral, IRegistrationPoint> subnode,
            string currentMatching, out string longestMatching)
        {
            lock(collectionSync)
            {
                var subpath = path.Split(new [] { PathSeparator }, 2);
                subnode = null;
                longestMatching = currentMatching;
                foreach(var currentChild in from.Children)
                {
                    string name;
                    if(!TryGetLocalName(currentChild.Value, out name))
                    {
                        continue;
                    }

                    if(name == subpath[0])
                    {
                        subnode = currentChild;
                        if(subpath.Length == 1)
                        {
                            return true;
                        }
                        return TryFindSubnodeByName(currentChild, subpath[1], out subnode, Subname(currentMatching, subpath[0]), out longestMatching);
                    }
                }
                return false;
            }
        }

        private IPeripheral GetByName(string path)
        {
            IPeripheral result;
            string longestMatching;
            if(!TryGetByName(path, out result, out longestMatching))
            {
                throw new InvalidOperationException(string.Format(
                    "Could not find node '{0}', the longest matching was '{1}'.", path, longestMatching));
            }
            return result;
        }

        private HashSet<IPeripheral> GetParents(IPeripheral child)
        {
            var parents = new HashSet<IPeripheral>();
            registeredPeripherals.TraverseChildrenFirst((parent, children, level) =>
            {
                if(children.Any(x => x.Value.Equals(child)))
                {
                    parents.Add(parent.Value);
                }
            }, 0);
            return parents;
        }

        private ReadOnlyCollection<string> GetNames(IPeripheral peripheral)
        {
            lock(collectionSync)
            {
                var paths = new List<string>();
                if(peripheral == SystemBus)
                {
                    paths.Add(SystemBusName);
                }
                else
                {
                    FindPaths(SystemBusName, peripheral, registeredPeripherals.GetNode(SystemBus), paths);
                }
                return new ReadOnlyCollection<string>(paths);
            }
        }

        private void FindPaths(string nameSoFar, IPeripheral peripheralToFind, MultiTreeNode<IPeripheral, IRegistrationPoint> currentNode, List<string> paths)
        {
            foreach(var child in currentNode.Children)
            {
                var currentPeripheral = child.Value;
                string localName;
                if(!TryGetLocalName(currentPeripheral, out localName))
                {
                    continue;
                }
                var name = Subname(nameSoFar, localName);
                if(currentPeripheral == peripheralToFind)
                {
                    paths.Add(name);
                    return; // shouldn't be attached to itself
                }
                FindPaths(name, peripheralToFind, child, paths);
            }
        }

        private static string Subname(string parent, string child)
        {
            return string.Format("{0}{1}{2}", parent, string.IsNullOrEmpty(parent) ? string.Empty : PathSeparator.ToString(), child);
        }

        private string GetNameForOwnLife(IHasOwnLife ownLife)
        {
            var peripheral = ownLife as IPeripheral;
            if(peripheral != null)
            {
                return GetAnyNameOrTypeName(peripheral);
            }
            return ownLife.ToString();
        }

        private static void DoAttachGPIO(IPeripheral source, PropertyInfo[] gpios, IGPIOReceiver destination, int destinationNumber)
        {
            if(gpios.Length == 0)
            {
                throw new RecoverableException("No GPIO connector found.");
            }
            if(gpios.Length > 1)
            {
                throw new RecoverableException("Ambiguous GPIO connector. Available connectors are: {0}."
                    .FormatWith(gpios.Select(x => x.Name).Aggregate((x, y) => x + ", " + y)));
            }
            (gpios[0].GetValue(source, null) as GPIO).Connect(destination, destinationNumber);
        }

        private static IGPIOReceiver GetActualReceiver(IGPIOReceiver receiver, int? localReceiverNumber)
        {
            var localReceiver = receiver as ILocalGPIOReceiver;
            if(localReceiverNumber.HasValue)
            {
                if(localReceiver != null)
                {
                    return localReceiver.GetLocalReceiver(localReceiverNumber.Value);
                }
                throw new RecoverableException("The specified receiver does not support localReceiverNumber.");
            }
            return receiver;
        }

        private void ReportForeignEventInner(Action<TimeInterval, bool> recordMethod, Action handlerMethod, bool timeDomainInternalEvent)
        {
            LocalTimeSource.ExecuteInNearestSyncedState(ts =>
            {
                recordMethod?.Invoke(ts.TimeElapsed, timeDomainInternalEvent);
                handlerMethod();
            }, true);
        }

        private void CollectGarbageStamp()
        {
            currentStampLevel++;
            if(currentStampLevel != 1)
            {
                return;
            }
            currentStamp = new List<IPeripheral>();
            registeredPeripherals.TraverseParentFirst((peripheral, level) => currentStamp.Add(peripheral), 0);
        }

        private void CollectGarbage()
        {
            currentStampLevel--;
            if(currentStampLevel != 0)
            {
                return;
            }
            var toDelete = currentStamp.Where(x => !IsRegistered(x)).ToArray();
            DetachIncomingInterrupts(toDelete);
            DetachOutgoingInterrupts(toDelete);
            foreach(var value in toDelete)
            {
                ((PeripheralsGroupsManager)PeripheralsGroups).RemoveFromAllGroups(value);
                var ownLife = value as IHasOwnLife;
                if(ownLife != null)
                {
                    ownLifes.Remove(ownLife);
                }
                EmulationManager.Instance.CurrentEmulation.Connector.DisconnectFromAll(value);

                localNames.Remove(value);
                var disposable = value as IDisposable;
                if(disposable != null)
                {
                    disposable.Dispose();
                }
            }
            currentStamp = null;
        }

        private void DetachIncomingInterrupts(IPeripheral[] detachedPeripherals)
        {
            foreach(var detachedPeripheral in detachedPeripherals)
            {
                // find all peripherials' GPIOs and check which one is connected to detachedPeripherial
                foreach(var peripheral in registeredPeripherals.Children.Select(x => x.Value).Distinct())
                {
                    foreach(var gpio in peripheral.GetGPIOs().Select(x => x.Item2))
                    {
                        if(gpio.Endpoint != null && gpio.Endpoint.Receiver == detachedPeripheral)
                        {
                            gpio.Disconnect();
                        }
                    }
                }
            }
        }

        private static void DetachOutgoingInterrupts(IEnumerable<IPeripheral> peripherals)
        {
            foreach(var peripheral in peripherals)
            {
                foreach(var gpio in peripheral.GetGPIOs().Select(x => x.Item2))
                {
                    gpio.Disconnect();
                }
            }
        }

        private void Resume()
        {
            lock(pausingSync)
            {
                LocalTimeSource.Resume();
                foreach(var ownLife in ownLifes.OrderBy(x => x is ICPU ? 1 : 0))
                {
                    this.NoisyLog("Resuming {0}.", GetNameForOwnLife(ownLife));
                    ownLife.Resume();
                }
                this.Log(LogLevel.Info, "Machine resumed.");
                state = State.Started;
                var machineStarted = StateChanged;
                if(machineStarted != null)
                {
                    machineStarted(this, new MachineStateChangedEventArgs(MachineStateChangedEventArgs.State.Started));
                }
            }
        }

        private void HandleTimeProgress(TimeInterval diff)
        {
            clockSource.Advance(diff);
        }

        private string userState;
        private Action<string> userStateHook;
        private bool alreadyDisposed;
        private State state;
        private PausedState pausedState;
        private List<IPeripheral> currentStamp;
        private int currentStampLevel;
        private Recorder recorder;
        private Player player;
        private DateTime machineStartedAt;
        private readonly MultiTree<IPeripheral, IRegistrationPoint> registeredPeripherals;
        private readonly Dictionary<IPeripheral, string> localNames;
        private readonly HashSet<IHasOwnLife> ownLifes;
        private readonly object collectionSync;
        private readonly object pausingSync;
        private readonly object disposedSync;

        private enum State
        {
            NotStarted,
            Started,
            Paused
        }

        private sealed class PausedState : IDisposable
        {
            public PausedState(Machine machine)
            {
                this.machine = machine;
                sync = new object();
            }

            public PausedState Enter()
            {
                LevelUp();
                return this;
            }

            public void Exit()
            {
                LevelDown();
            }

            public void Dispose()
            {
                Exit();
            }

            private void LevelUp()
            {
                lock(sync)
                {
                    if(currentLevel == 0)
                    {
                        if(machine.IsPaused)
                        {
                            wasPaused = true;
                        }
                        else
                        {
                            wasPaused = false;
                            machine.Pause();
                        }
                    }
                    currentLevel++;
                }
            }

            private void LevelDown()
            {
                lock(sync)
                {
                    if(currentLevel == 1)
                    {
                        if(!wasPaused)
                        {
                            machine.Start();
                        }
                    }
                    if(currentLevel == 0)
                    {
                        throw new InvalidOperationException("LevelDown without prior LevelUp");
                    }
                    currentLevel--;
                }
            }

            private int currentLevel;
            private bool wasPaused;
            private readonly Machine machine;
            private readonly object sync;
        }

        private sealed class PeripheralsGroupsManager : IPeripheralsGroupsManager
        {
            public PeripheralsGroupsManager(Machine machine)
            {
                this.machine = machine;
                groups = new List<PeripheralsGroup>();
            }

            public IPeripheralsGroup GetOrCreate(string name, IEnumerable<IPeripheral> peripherals)
            {
                IPeripheralsGroup existingResult = null;
                var result = (PeripheralsGroup)existingResult;
                if(!TryGetByName(name, out existingResult))
                {
                    result = new PeripheralsGroup(name, machine);
                    groups.Add(result);
                }

                foreach(var p in peripherals)
                {
                    result.Add(p);
                }

                return result;
            }

            public IPeripheralsGroup GetOrCreate(string name)
            {
                IPeripheralsGroup result;
                if(!TryGetByName(name, out result))
                {
                    result = new PeripheralsGroup(name, machine);
                    groups.Add((PeripheralsGroup)result);
                }

                return result;
            }

            public void RemoveFromAllGroups(IPeripheral value)
            {
                foreach(var group in ActiveGroups)
                {
                    ((List<IPeripheral>)group.Peripherals).Remove(value);
                }
            }

            public bool TryGetActiveGroupContaining(IPeripheral peripheral, out IPeripheralsGroup group)
            {
                group = ActiveGroups.SingleOrDefault(x => ((PeripheralsGroup)x).Contains(peripheral));
                return group != null;
            }

            public bool TryGetAnyGroupContaining(IPeripheral peripheral, out IPeripheralsGroup group)
            {
                group = groups.SingleOrDefault(x => x.Contains(peripheral));
                return group != null;
            }

            public bool TryGetByName(string name, out IPeripheralsGroup group)
            {
                group = ActiveGroups.SingleOrDefault(x => x.Name == name);
                return group != null;
            }

            public IEnumerable<IPeripheralsGroup> ActiveGroups
            {
                get
                {
                    return groups.Where(x => x.IsActive);
                }
            }

            private readonly List<PeripheralsGroup> groups;
            private readonly Machine machine;

            private sealed class PeripheralsGroup : IPeripheralsGroup
            {
                public PeripheralsGroup(string name, Machine machine)
                {
                    Machine = machine;
                    Name = name;
                    IsActive = true;
                    Peripherals = new List<IPeripheral>();
                }

                public void Add(IPeripheral peripheral)
                {
                    if(!Machine.IsRegistered(peripheral))
                    {
                        throw new RegistrationException("Peripheral must be registered prior to adding to the group");
                    }
                    ((List<IPeripheral>)Peripherals).Add(peripheral);
                }

                public bool Contains(IPeripheral peripheral)
                {
                    return Peripherals.Contains(peripheral);
                }

                public void Remove(IPeripheral peripheral)
                {
                    ((List<IPeripheral>)Peripherals).Remove(peripheral);
                }

                public void Unregister()
                {
                    IsActive = false;
                    using(Machine.ObtainPausedState())
                    {
                        foreach(var p in Peripherals.ToList())
                        {
                            Machine.UnregisterFromParent(p);
                        }
                    }
                    ((PeripheralsGroupsManager)Machine.PeripheralsGroups).groups.Remove(this);
                }

                public string Name { get; private set; }

                public bool IsActive { get; private set; }

                public Machine Machine { get; private set; }

                public IEnumerable<IPeripheral> Peripherals { get; private set; }
            }
        }
    }
}

