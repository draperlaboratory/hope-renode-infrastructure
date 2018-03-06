//
// Copyright (c) 2010-2018 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals;
using Mono.Cecil;
using System.Reflection;
using System.Collections.Generic;
using Antmicro.Renode.Plugins;

namespace Antmicro.Renode.Utilities
{
    public class TypeManager : IDisposable
    {
        static TypeManager()
        {
            Instance = new TypeManager();
            Instance.Scan(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        }

        public static TypeManager Instance { get; private set; }

        private Action<Type> autoLoadedTypeEvent;
        private readonly List<Type> autoLoadedTypes = new List<Type>();
        //AutoLoadedType will fire for each type even if the event is attached after the loading.
        private object autoLoadedTypeLocker = new object();
        public event Action<Type> AutoLoadedType
        {
            add
            {
                // this lock is needed because it happens that two 
                // threads add the event simulataneously and an exception is rised
                lock(autoLoadedTypeLocker)
                {
                    if(value != null)
                    {
                        foreach(var type in autoLoadedTypes)
                        {
                            value(type);
                        }
                        autoLoadedTypeEvent += value;
                    }
                }
            }
            remove
            {
                lock(autoLoadedTypeLocker)
                {
                    autoLoadedTypeEvent -= value;
                }
            }
        }

        public void Scan()
        {
            Scan(Directory.GetCurrentDirectory());
        }

        public bool ScanFile(string path)
        {
            lock(dictSync)
            {
                Logger.LogAs(this, LogLevel.Noisy, "Loading assembly {0}.", path);
                ClearExtensionMethodsCache();
                BuildAssemblyCache();
                if(!AnalyzeAssembly(path))
                {
                    return false;
                }
                assemblyFromAssemblyPath = null;
                Logger.LogAs(this, LogLevel.Noisy, "Assembly loaded, there are now {0} types in dictionaries.", GetTypeCount());

                return true;
            }
        }

        public void Scan(string path, bool recursive = false)
        {
            lock(dictSync)
            {
                Logger.LogAs(this, LogLevel.Noisy, "Scanning directory {0}.", path);
                var stopwatch = Stopwatch.StartNew();
                ClearExtensionMethodsCache();
                BuildAssemblyCache();
                ScanInner(path, recursive);
                assemblyFromAssemblyPath = null;
                stopwatch.Stop();
                Logger.LogAs(this, LogLevel.Noisy, "Scanning took {0}s, there are now {1} types in dictionaries.", Misc.NormalizeDecimal(stopwatch.Elapsed.TotalSeconds),
                          GetTypeCount());
            }
        }

        public IEnumerable<MethodInfo> GetExtensionMethods(Type type)
        {
            lock(dictSync)
            {
                if(extensionMethodsFromThisType.ContainsKey(type))
                {
                    return extensionMethodsFromThisType[type];
                }
                var fullName = type.FullName;
                Logger.LogAs(this, LogLevel.Noisy, "Binding extension methods for {0}.", fullName);
                var methodInfos = GetExtensionMethodsInner(type).ToArray();
                Logger.LogAs(this, LogLevel.Noisy, "{0} methods bound.", methodInfos.Length);
                // we can put it into cache now
                extensionMethodsFromThisType.Add(type, methodInfos);
                return methodInfos;
            }
        }

        public Type GetTypeByName(string name, Func<ICollection<string>, string> assemblyResolver = null)
        {
            var result = TryGetTypeByName(name, assemblyResolver);
            if(result == null)
            {
                throw new KeyNotFoundException(string.Format("Given type {0} was not found in any of the known assemblies.", name));
            }
            return result;
        }

        public Type TryGetTypeByName(string name, Func<ICollection<string>, string> assemblyResolver = null)
        {
            lock(dictSync)
            {
                AssemblyDescription assembly;
                if(assemblyFromTypeName.TryGetValue(name, out assembly))
                {
                    var typeName = string.Format("{0}, {1}", name, assembly.FullName);
                    return GetTypeWithLazyLoad(typeName, assembly.Path);
                }
                if(assembliesFromTypeName.ContainsKey(name))
                {
                    var possibleAssemblies = assembliesFromTypeName[name];
                    if(assemblyResolver == null)
                    {
                        throw new InvalidOperationException(string.Format(
                            "Type {0} could possibly be loaded from assemblies {1}, but no assembly resolver was provided.",
                            name, possibleAssemblies.Select(x => x.Path).Aggregate((x, y) => x + ", " + y)));
                    }
                    var selectedAssembly = assemblyResolver(possibleAssemblies.Select(x => x.Path).ToList());
                    var selectedAssemblyDescription = possibleAssemblies.FirstOrDefault(x => x.Path == selectedAssembly);
                    if(selectedAssemblyDescription == null)
                    {
                        throw new InvalidOperationException(string.Format(
                            "Assembly resolver returned path {0} which is not one of the proposed paths {1}.",
                            selectedAssembly, possibleAssemblies.Select(x => x.Path).Aggregate((x, y) => x + ", " + y)));
                    }
                    var typeName = string.Format("{0}, {1}", name, selectedAssemblyDescription);
                    // once conflict is resolved, we can move this type to assemblyFromTypeName
                    assembliesFromTypeName.Remove(name);
                    assemblyFromTypeName.Add(name, selectedAssemblyDescription);
                    return GetTypeWithLazyLoad(typeName, selectedAssembly);
                }
                return null;
            }
        }

        public IEnumerable<TypeDescriptor> GetAvailablePeripherals(Type attachableTo = null)
        {
            if(attachableTo == null)
            {
                return foundPeripherals.Where(td => td.IsClass && !td.IsAbstract && td.Methods.Any(m => m.IsConstructor && m.IsPublic)).Select(x => new TypeDescriptor(x));
            }

            var ifaces = attachableTo.GetInterfaces()
                .Where(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(Antmicro.Renode.Core.Structure.IPeripheralRegister<,>))
                .Select(i => i.GetGenericArguments()[0]).Distinct();

            return foundPeripherals
               .Where(td =>
                    td.IsClass &&
                    !td.IsAbstract &&
                    td.Methods.Any(m => m.IsConstructor && m.IsPublic) &&
                    ifaces.Any(iface => ImplementsInterface(td, iface)))
                .Select(x => new TypeDescriptor(x));
        }

        public void Dispose()
        {
            PluginManager.Dispose();
        }

        public Type[] AutoLoadedTypes { get { return autoLoadedTypes.ToArray (); } }
        public IEnumerable<PluginDescriptor> AvailablePlugins { get { return foundPlugins.ToArray(); } }
        public PluginManager PluginManager { get; set; }

        private bool ImplementsInterface(TypeDefinition type, Type @interface)
        {
            if(type.GetFullNameOfMember() == @interface.FullName)
            {
                return true;
            }

            return (type.BaseType != null && ImplementsInterface(type.BaseType.Resolve(), @interface)) || type.Interfaces.Any(i => ImplementsInterface(i.Resolve(), @interface));
        }

        private TypeManager()
        {
            assembliesFromTypeName = new Dictionary<string, List<AssemblyDescription>>();
            assemblyFromTypeName = new Dictionary<string, AssemblyDescription>();
            assemblyFromAssemblyName = new Dictionary<string, AssemblyDescription>();
            extensionMethodsFromThisType = new Dictionary<Type, MethodInfo[]>();
            extensionMethodsTraceFromTypeFullName = new Dictionary<string, HashSet<MethodDescription>>();
            knownDirectories = new HashSet<string>();
            dictSync = new object();
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            foundPeripherals = new List<TypeDefinition>();
            foundPlugins = new List<PluginDescriptor>();
            PluginManager = new PluginManager();
        }

        private Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            lock(dictSync)
            {
                AssemblyDescription description;
                var simpleName = ExtractSimpleName(args.Name);
                if(assemblyFromAssemblyName.TryGetValue(simpleName, out description))
                {
                    if(args.Name == description.FullName)
                    {
                        Logger.LogAs(this, LogLevel.Noisy, "Assembly '{0}' resolved by exact match from '{1}'.", args.Name, description.Path);
                    }
                    else
                    {
                        Logger.LogAs(this, LogLevel.Noisy, "Assembly '{0}' resolved by simple name '{1}' from '{2}'.", args.Name, simpleName, description.Path);
                    }
                    return Assembly.LoadFrom(description.Path);

                }
                return null;
            }
        }

        private void ScanInner(string path, bool recursive)
        {
            // TODO: case insensitive
            foreach(var assembly in Directory.GetFiles(path, "*.dll").Union(Directory.GetFiles(path, "*.exe")))
            {
                AnalyzeAssembly(assembly);
            }
            if(recursive)
            {
                foreach(var subdir in Directory.GetDirectories(path))
                {
                    ScanInner(subdir, recursive);
                }
            }
        }

        private static string ExtractSimpleName(string name)
        {
            return name.Split(',')[0];
        }

        private IEnumerable<MethodInfo> GetExtensionMethodsInner(Type type)
        {
            var fullName = type.FullName;
            IEnumerable<MethodInfo> methodInfos;
            if(!extensionMethodsTraceFromTypeFullName.ContainsKey(fullName))
            {
                methodInfos = new MethodInfo[0];
            }
            else
            {
                var methodDescriptions = extensionMethodsTraceFromTypeFullName[fullName];
                var result = new MethodInfo[methodDescriptions.Count];
                var i = -1;
                foreach(var methodDescription in methodDescriptions)
                {
                    i++;
                    var describedType = GetTypeByName(methodDescription.TypeFullName);
                    if(!methodDescription.IsOverloaded)
                    {
                        // method's name is unique
                        result[i] = describedType.GetMethod(methodDescription.Name);
                    }
                    else
                    {
                        var methodsInClass = describedType.GetMethods();
                        var matchedMethod = methodsInClass.Single(x => x.Name == methodDescription.Name && GetMethodSignature(x) == methodDescription.Signature);
                        result[i] = matchedMethod;
                    }
                }
                methodInfos = result;
            }
            // we also obtain EM for base type and interfaces
            if(type.BaseType != null)
            {
                methodInfos = methodInfos.Union(GetExtensionMethodsInner(type.BaseType));
            }
            foreach(var iface in type.GetInterfaces())
            {
                methodInfos = methodInfos.Union(GetExtensionMethodsInner(iface));
            }
            methodInfos = methodInfos.ToArray();
            return methodInfos;
        }

        private Type GetTypeWithLazyLoad(string fullName, string path)
        {
            var type = Type.GetType(fullName);
            if(type == null)
            {
                Assembly.LoadFrom(path);
                type = Type.GetType(fullName, true);
                Logger.LogAs(this, LogLevel.Noisy, "Loaded assembly {0} ({1} triggered).", path, type.FullName);
            }
            return type;
        }

        private void BuildAssemblyCache()
        {
            assemblyFromAssemblyPath = new Dictionary<string, AssemblyDescription>();
            foreach(var assembly in assemblyFromTypeName.Select(x => x.Value).Union(assembliesFromTypeName.SelectMany(x => x.Value)).Distinct())
            {
                assemblyFromAssemblyPath.Add(assembly.Path, assembly);
            }
            Logger.LogAs(this, LogLevel.Noisy, "Assembly cache with {0} distinct assemblies built.", assemblyFromAssemblyPath.Count);
        }

        private bool ExtractExtensionMethods(TypeDefinition type)
        {
            // type is enclosing type
            if(!type.IsClass)
            {
                return false;
            }
            var methodAdded = false;
            foreach(var method in type.Methods)
            {
                if(method.IsStatic && method.IsPublic && method.CustomAttributes.Any(x => x.AttributeType.GetFullNameOfMember() == typeof(System.Runtime.CompilerServices.ExtensionAttribute).FullName))
                {
                    // so this is extension method
                    // let's check the type of the first parameter
                    var paramReference = method.Parameters[0];
                    var paramType = paramReference.ParameterType.Resolve();
                    if(paramType == null)
                    {
                        if(paramReference.ParameterType.IsGenericParameter || paramReference.ParameterType.GetElementType().IsGenericParameter)
                        {
                            // we do not handle generic extension methods now
                            continue;
                        }
                        Logger.LogAs(this, LogLevel.Warning, "Could not resolve parameter type {0} for method {1} in class {2}.",
                                 paramReference.ParameterType.GetFullNameOfMember(), method.GetFullNameOfMember(), type.GetFullNameOfMember());
                        continue;
                    }
                    if(IsInterestingType(paramType) ||
                        (paramType.GetFullNameOfMember() == typeof(object).FullName
                        && method.CustomAttributes.Any(x => x.AttributeType.GetFullNameOfMember() == typeof(ExtensionOnObjectAttribute).FullName)))
                    {
                        methodAdded = true;
                        // that's the interesting extension method
                        var methodDescription = new MethodDescription(type.GetFullNameOfMember(), method.Name, GetMethodSignature(method), true);
                        if(extensionMethodsTraceFromTypeFullName.ContainsKey(paramType.GetFullNameOfMember()))
                        {
                            extensionMethodsTraceFromTypeFullName[paramType.GetFullNameOfMember()].Add(methodDescription);
                        }
                        else
                        {
                            extensionMethodsTraceFromTypeFullName.Add(paramType.GetFullNameOfMember(), new HashSet<MethodDescription> { methodDescription });
                        }
                    }
                }
            }
            return methodAdded;
        }

        private static bool IsReferenced(Assembly referencingAssembly, string checkedAssemblyName)
        {
            var alreadyVisited = new HashSet<Assembly>();
            var queue = new Queue<Assembly>();
            queue.Enqueue(referencingAssembly);
            while(queue.Count > 0)
            {
                var current = queue.Dequeue();
                if(current.FullName == checkedAssemblyName)
                {
                    return true;
                }
                if(alreadyVisited.Contains(current))
                {
                    continue;
                }
                alreadyVisited.Add(current);
                foreach(var reference in current.GetReferencedAssemblies())
                {
                    try
                    {
                        queue.Enqueue(Assembly.Load(reference));
                    }
                    catch(FileNotFoundException)
                    {
                        // if we could not load references assembly, do nothing
                    }
                }
            }
            return false;
        }

        private bool AnalyzeAssembly(string path)
        {
            Logger.LogAs(this, LogLevel.Noisy, "Analyzing assembly {0}.", path);
            if(assemblyFromAssemblyName.Values.Any(x => x.Path == path))
            {
                Logger.LogAs(this, LogLevel.Warning, "Assembly {0} was already analyzed.", path);
                return true;
            }
            AssemblyDefinition assembly;
            try
            {
                assembly = AssemblyDefinition.ReadAssembly(path);
            }
            catch(DirectoryNotFoundException)
            {
                Logger.LogAs(this, LogLevel.Warning, "Could not find file {0} to analyze.", path);
                return false;
            }
            catch(FileNotFoundException)
            {
                Logger.LogAs(this, LogLevel.Warning, "Could not find file {0} to analyze.", path);
                return false;
            }
            catch(BadImageFormatException)
            {
                Logger.LogAs(this, LogLevel.Warning, "File {0} could not be analyzed due to invalid format.", path);
                return false;
            }
            var assemblyName = assembly.FullName;
            if(!assemblyFromAssemblyName.ContainsKey(assemblyName))
            {
                assemblyFromAssemblyName.Add(assemblyName, GetAssemblyDescription(assemblyName, path));
            }
            else
            {
                if(path == assemblyFromAssemblyName[assemblyName].Path)
                {
                    return true;
                }
                var description = assemblyFromAssemblyName[assemblyName];
                Logger.LogAs(this, LogLevel.Warning, "Assembly {0} is hidden by one located in {1} (same simple name {2}).",
                         path, description.Path, assemblyName);
            }
            var types = new List<TypeDefinition>();
            foreach(var module in assembly.Modules)
            {
                // we add the assembly's directory to the resolve directory - also all known directories
                knownDirectories.Add(Path.GetDirectoryName(path));
                var defaultAssemblyResolver = ((DefaultAssemblyResolver)module.AssemblyResolver);
                foreach(var directory in knownDirectories)
                {
                    defaultAssemblyResolver.AddSearchDirectory(directory);
                }
                foreach(var type in module.GetTypes())
                {
                    types.Add(type);
                }
            }

            var hidePluginsFromThisAssembly = false;

            // It happens that `entryAssembly` is null, e.g., when running tests inside MD.
            // In such case we don't care about hiding plugins, so we just skip this mechanism (as this is the simples solution to the NRE problem).
            var entryAssembly = Assembly.GetEntryAssembly();
            if(entryAssembly != null && IsReferenced(entryAssembly, assembly.FullName))
            {
                Logger.LogAs(this, LogLevel.Noisy, "Plugins from this assembly {0} will be hidden as it is explicitly referenced.", assembly.FullName);
                hidePluginsFromThisAssembly = true;
            }

            foreach(var type in types)
            {
                if(type.Interfaces.Any(i => i.Resolve().GetFullNameOfMember() == typeof(IPeripheral).FullName))
                {
                    Logger.LogAs(this, LogLevel.Noisy, "Peripheral type {0} found.", type.Resolve().GetFullNameOfMember());
                    foundPeripherals.Add(type);
                }

                if(type.CustomAttributes.Any(x => x.AttributeType.Resolve().GetFullNameOfMember() == typeof(PluginAttribute).FullName))
                {
                    Logger.LogAs(this, LogLevel.Noisy, "Plugin type {0} found.", type.Resolve().GetFullNameOfMember());
                    try
                    {
                        foundPlugins.Add(new PluginDescriptor(type, hidePluginsFromThisAssembly));
                    }
                    catch(Exception e)
                    {
                        //This may happend due to, e.g., version parsing error. The plugin is ignored.
                        Logger.LogAs(this, LogLevel.Error, "Plugin type {0} loading error: {1}.", type.GetFullNameOfMember(), e.Message);
                    }
                }

                if(IsAutoLoadType(type))
                {
                    var typeName = string.Format("{0}, {1}", type.GetFullNameOfMember(), assembly.FullName);
                    var loadedType = GetTypeWithLazyLoad(typeName, path);
                    lock(autoLoadedTypeLocker)
                    {
                        autoLoadedTypes.Add(loadedType);
                    }
                    var autoLoadedType = autoLoadedTypeEvent;
                    if(autoLoadedType != null)
                    {
                        autoLoadedType(loadedType);
                    }
                    continue;
                }
                if(!ExtractExtensionMethods(type) && !IsInterestingType(type))
                {
                    continue;
                }
                // type is interesting, we'll put it into our dictionaries
                // after conflicts checking
                var fullName = type.GetFullNameOfMember();
                var newAssemblyDescription = GetAssemblyDescription(assembly.FullName, path);
                Logger.LogAs(this, LogLevel.Noisy, "Type {0} added.", fullName);
                if(assembliesFromTypeName.ContainsKey(fullName))
                {
                    assembliesFromTypeName[fullName].Add(newAssemblyDescription);
                    continue;
                }
                if(assemblyFromTypeName.ContainsKey(fullName))
                {
                    var description = assemblyFromTypeName[fullName];
                    assemblyFromTypeName.Remove(fullName);
                    assembliesFromTypeName.Add(fullName, new List<AssemblyDescription> { description, newAssemblyDescription });
                    continue;
                }
                assemblyFromTypeName.Add(fullName, newAssemblyDescription);
            }

            return true;
        }

        private static bool IsAutoLoadType(TypeDefinition type)
        {
            var isAutoLoad = type.Interfaces.Select(x => x.GetFullNameOfMember()).Contains(typeof(IAutoLoadType).FullName);
            if(isAutoLoad)
            {
                return true;
            }
            var resolved = ResolveBaseType(type);
            if(resolved == null)
            {
                return false;
            }
            return IsAutoLoadType(resolved);
        }

        static TypeDefinition ResolveBaseType(TypeDefinition type)
        {
            if(type.BaseType == null)
            {
                return null;
            }
            TypeDefinition resolved;
            try
            {
                resolved = type.BaseType.Resolve();
            }
            catch(AssemblyResolutionException)
            {
                resolved = null;
            }

            return resolved;
        }

        private bool IsInterestingType(TypeDefinition type)
        {
            if(type.CustomAttributes.Any(x => x.AttributeType.Resolve().Interfaces.Select(y => y.GetFullNameOfMember()).Contains(typeof(IInterestingType).FullName)))
            {
                return true;
            }
            if(type.IsInterface && type.GetFullNameOfMember() == typeof(IInterestingType).FullName)
            {
                return true;
            }
            foreach(var iface in type.Interfaces)
            {
                if(iface.GetFullNameOfMember() == typeof(IInterestingType).FullName)
                {
                    return true;
                }
            }
            var resolved = ResolveBaseType(type);
            if(resolved == null)
            {
                return false;
            }
            return IsInterestingType(resolved);
        }

        private AssemblyDescription GetAssemblyDescription(string fullName, string path)
        {
            // maybe we already have one like that (interning)
            if(assemblyFromAssemblyPath.ContainsKey(path))
            {
                return assemblyFromAssemblyPath[path];
            }
            var description = new AssemblyDescription(fullName, path);
            assemblyFromAssemblyPath.Add(path, description);
            return description;
        }

        private void ClearExtensionMethodsCache()
        {
            extensionMethodsFromThisType.Clear(); // to be consistent with string dictionary
        }

        private static string GetMethodSignature(MethodDefinition definition)
        {
            return definition.Parameters.Select(x => x.ParameterType.GetFullNameOfMember()).Aggregate((x, y) => x + "," + y);
        }

        private static string GetMethodSignature(MethodInfo info)
        {
            return info.GetParameters().Select(x => GetSimpleFullTypeName(x.ParameterType)).Aggregate((x, y) => x + "," + y);
        }

        private static string GetSimpleFullTypeName(Type type)
        {
            if(!type.IsGenericType)
            {
                return type.FullName;
            }
            var result = string.Format("{0}<{1}>", type.GetGenericTypeDefinition().FullName,
                                       type.GetGenericArguments().Select(x => GetSimpleFullTypeName(x)).Aggregate((x, y) => x + "," + y));
            return result;
        }

        private int GetTypeCount()
        {
            lock(dictSync)
            {
                return assembliesFromTypeName.Count + assemblyFromTypeName.Count;
            }
        }

        private readonly Dictionary<string, AssemblyDescription> assemblyFromTypeName;
        private readonly Dictionary<string, AssemblyDescription> assemblyFromAssemblyName;
        private readonly Dictionary<string, List<AssemblyDescription>> assembliesFromTypeName;
        private readonly Dictionary<string, HashSet<MethodDescription>> extensionMethodsTraceFromTypeFullName;
        private readonly Dictionary<Type, MethodInfo[]> extensionMethodsFromThisType;
        private Dictionary<string, AssemblyDescription> assemblyFromAssemblyPath;
        private readonly object dictSync;
        private readonly HashSet<string> knownDirectories;

        private readonly List<TypeDefinition> foundPeripherals;
        private readonly List<PluginDescriptor> foundPlugins;

        private class AssemblyDescription
        {
            public readonly string Path;

            public readonly string FullName;

            public AssemblyDescription(string fullName, string path)
            {
                FullName = fullName;
                Path = path;
            }

            public override bool Equals(object obj)
            {
                var other = obj as AssemblyDescription;
                if(other == null)
                {
                    return false;
                }
                return other.Path == Path && other.FullName == FullName;
            }

            public override int GetHashCode()
            {
                return Path.GetHashCode();
            }
        }

        private struct MethodDescription
        {
            public readonly string TypeFullName;
            public readonly string Name;
            public readonly string Signature;
            public readonly bool IsOverloaded;

            public MethodDescription(string typeFullName, string name, string signature, bool overloaded)
            {
                TypeFullName = typeFullName;
                Name = name;
                Signature = signature;
                IsOverloaded = overloaded;
            }
        }
    }
}
