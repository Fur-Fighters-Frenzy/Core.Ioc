using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Validosik.Core.Editor.Ioc.CodeGeneration;
using Validosik.Core.Ioc;
using Validosik.Core.Ioc.Attributes;
using Validosik.Core.Ioc.Resolvers;
using Validosik.Core.Reflection;

namespace Validosik.Core.Editor.Ioc
{
    /// <summary>
    /// Editor UI: build metadata, pick interfaces, bind resolver OR implementation, override lifetime, and codegen registries.
    /// No ScriptableObjects; config is ephemeral -> codegen emits .g.cs registries.
    /// </summary>
    internal sealed class ContainableConfiguratorWindow : EditorWindow
    {
        private sealed class Row
        {
            public Type InterfaceType;
            public string InterfaceGuid;
            public ServiceLifetime LifetimeDefault;
            public bool UseResolver;
            public Type ResolverType;
            public Type ImplementationType;
            public ServiceLifetime? LifetimeOverride;
        }

        private sealed class ContainerSpec
        {
            public string Key;
            public readonly List<Row> Rows = new List<Row>();
        }

        private readonly List<Type> _contracts = new List<Type>();
        private readonly List<Type> _implementations = new List<Type>();
        private readonly List<Type> _resolvers = new List<Type>();
        private readonly List<ContainerSpec> _containers = new List<ContainerSpec>();

        private int _containerIndex = -1;
        private string _newContainerKey = "Game";
        private string _searchInterface = "";

        [MenuItem("Tools/Containable/Configurator")]
        public static void Open()
        {
            var w = GetWindow<ContainableConfiguratorWindow>("Containable Config");
            w.minSize = new Vector2(900, 600);
        }

        private void OnEnable()
        {
            try
            {
                ScanWorld();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void ScanWorld()
        {
            _contracts.Clear();
            _implementations.Clear();
            _resolvers.Clear();

            using var rh = new ReflectionHelper(null);
            rh.CollectAssembliesTypes(false);

            // interfaces with ContainableServiceContractAttribute
            var buf = new List<(Type, ContainableServiceContractAttribute[])>(128);
            rh.CollectTypesWithAttribute<ContainableServiceContractAttribute>(buf, includeClasses: false,
                includeInterfaces: true, inherit: false);
            for (var i = 0; i < buf.Count; ++i) _contracts.Add(buf[i].Item1);

            // implementations with ContainableServiceImplAttribute (classes)
            var implBuf = new List<(Type, ContainableServiceImplementationAttribute[])>(256);
            rh.CollectTypesWithAttribute<ContainableServiceImplementationAttribute>(implBuf, includeClasses: true,
                includeInterfaces: false, inherit: false);
            for (var i = 0; i < implBuf.Count; ++i) _implementations.Add(implBuf[i].Item1);

            // resolvers: any class implementing IContainableResolver<T>
            foreach (var t in rh.Types)
            {
                if (t == null || t.IsAbstract || t.IsInterface) continue;
                var interfaces = t.GetInterfaces();
                for (var i = 0; i < interfaces.Length; ++i)
                {
                    var itf = interfaces[i];
                    if (!itf.IsGenericType || itf.GetGenericTypeDefinition() != typeof(IContainableResolver<>))
                    {
                        continue;
                    }

                    _resolvers.Add(t);
                    break;
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Containers", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _newContainerKey = EditorGUILayout.TextField("New Container Key", _newContainerKey);
            if (GUILayout.Button("Add", GUILayout.Width(80)))
            {
                if (!string.IsNullOrEmpty(_newContainerKey) && !_containers.Any(c => c.Key == _newContainerKey))
                {
                    _containers.Add(new ContainerSpec { Key = _newContainerKey });
                    _containerIndex = _containers.Count - 1;
                }
            }

            if (GUILayout.Button("Remove Current", GUILayout.Width(140)) && _containerIndex >= 0)
            {
                _containers.RemoveAt(_containerIndex);
                _containerIndex = Mathf.Clamp(_containerIndex - 1, -1, _containers.Count - 1);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (_containers.Count == 0)
            {
                EditorGUILayout.HelpBox("No containers. Create one.", MessageType.Info);
                return;
            }

            var keys = _containers.Select(c => c.Key).ToArray();
            var idx = Mathf.Clamp(_containerIndex, 0, _containers.Count - 1);
            _containerIndex = EditorGUILayout.Popup("Active Container", idx, keys);
            var container = _containers[_containerIndex];

            EditorGUILayout.Space();
            DrawInterfacePicker(container);

            EditorGUILayout.Space();
            DrawRows(container);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild Graph (Union)"))
            {
                try
                {
                    RebuildGraph(container);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (GUILayout.Button("Generate Registry (.g.cs)"))
            {
                try
                {
                    GenerateRegistry(container);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawInterfacePicker(ContainerSpec container)
        {
            EditorGUILayout.LabelField("Add Interface", EditorStyles.boldLabel);

            _searchInterface = EditorGUILayout.TextField("Search", _searchInterface ?? "");
            var filtered = _contracts
                .Where(t => t.Name.IndexOf(_searchInterface ?? "", StringComparison.OrdinalIgnoreCase) >= 0
                            || (t.FullName != null &&
                                t.FullName.IndexOf(_searchInterface ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(t => !container.Rows.Any(r => r.InterfaceType == t))
                .OrderBy(t => t.Name)
                .Take(20)
                .ToArray();

            foreach (var t in filtered)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(t.FullName, GUILayout.MinWidth(500));
                if (GUILayout.Button("Add", GUILayout.Width(80)))
                {
                    var ca = (ContainableServiceContractAttribute)Attribute.GetCustomAttribute(t,
                        typeof(ContainableServiceContractAttribute));
                    container.Rows.Add(new Row
                    {
                        InterfaceType = t,
                        InterfaceGuid = ca != null ? ca.Guid : "",
                        LifetimeDefault = ca?.DefaultLifetime ?? ServiceLifetime.Scoped,
                        UseResolver = false
                    });
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawRows(ContainerSpec container)
        {
            if (container.Rows.Count == 0)
            {
                EditorGUILayout.HelpBox("No bindings in this container. Add interfaces above.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Bindings", EditorStyles.boldLabel);

            for (var i = 0; i < container.Rows.Count; i++)
            {
                var row = container.Rows[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(row.InterfaceType.FullName + "  [" + row.InterfaceGuid + "]");
                EditorGUILayout.BeginHorizontal();

                // Toggle: Resolver vs Implementation
                var newUseResolver = EditorGUILayout.ToggleLeft("Use Resolver", row.UseResolver, GUILayout.Width(120));
                if (newUseResolver != row.UseResolver)
                {
                    row.UseResolver = newUseResolver;
                    row.ResolverType = null;
                    row.ImplementationType = null;
                }

                // Resolver dropdown (generic target must be this interface)
                if (row.UseResolver)
                {
                    var resolverCandidates = _resolvers
                        .Where(r => r.GetInterfaces().Any(it =>
                            it.IsGenericType && it.GetGenericTypeDefinition() == typeof(IContainableResolver<>)
                                             && it.GetGenericArguments()[0] == row.InterfaceType))
                        .OrderBy(t => t.Name).ToArray();

                    var idx = Array.IndexOf(resolverCandidates, row.ResolverType);
                    var newIdx = EditorGUILayout.Popup("Resolver", Mathf.Max(0, idx),
                        resolverCandidates.Select(t => t.Name).ToArray());
                    if (resolverCandidates.Length > 0) row.ResolverType = resolverCandidates[newIdx];
                }
                else
                {
                    // Implementation dropdown (class with impl-attr and implements interface)
                    var implCandidates = _implementations
                        .Where(impl => row.InterfaceType.IsAssignableFrom(impl))
                        .OrderBy(t => t.Name).ToArray();

                    var idx = Array.IndexOf(implCandidates, row.ImplementationType);
                    var newIdx = EditorGUILayout.Popup("Implementation", Mathf.Max(0, idx),
                        implCandidates.Select(t => t.Name).ToArray());
                    if (implCandidates.Length > 0) row.ImplementationType = implCandidates[newIdx];
                }

                // Lifetime
                var lifetime = row.LifetimeOverride.HasValue ? row.LifetimeOverride.Value : row.LifetimeDefault;
                var newLt = (ServiceLifetime)EditorGUILayout.EnumPopup("Lifetime", lifetime, GUILayout.Width(260));
                if (newLt != lifetime) row.LifetimeOverride = newLt;

                if (GUILayout.Button("Remove", GUILayout.Width(90)))
                {
                    container.Rows.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
        }

        private void RebuildGraph(ContainerSpec container)
        {
            // Build a union graph based on current selections.
            // Resolver branch: take Describe().CandidateTypes as possible implementations and include all for union.
            var graph = new ServiceGraph();
            var scanner = new ServiceScanner();

            // Build a temporary list of (Type impl, ContainableServiceContractAttribute contract) to feed scanner.
            var temp = new List<(Type, ContainableServiceContractAttribute[])>();

            for (var i = 0; i < container.Rows.Count; i++)
            {
                var row = container.Rows[i];
                var contractAttr = (ContainableServiceContractAttribute)Attribute.GetCustomAttribute(row.InterfaceType,
                    typeof(ContainableServiceContractAttribute));

                if (row.UseResolver && row.ResolverType != null)
                {
                    // Union over resolver candidates
                    var inst = Activator.CreateInstance(row.ResolverType) as object;
                    var describe = row.ResolverType.GetMethod("Describe");
                    var manifest = describe != null ? describe.Invoke(inst, null) : null;

                    var candidatesProp = manifest?.GetType().GetProperty("CandidateTypes");
                    var candidates = candidatesProp != null
                        ? (IEnumerable<Type>)candidatesProp.GetValue(manifest, null)
                        : null;

                    if (candidates != null)
                    {
                        foreach (var impl in candidates)
                        {
                            if (impl == null) continue;
                            if (!row.InterfaceType.IsAssignableFrom(impl)) continue;
                            temp.Add((impl, new[] { contractAttr }));
                        }
                    }
                }
                else
                {
                    if (row.ImplementationType != null)
                    {
                        temp.Add((row.ImplementationType, new[] { contractAttr }));
                    }
                }
            }

            var built = scanner.Scan(temp.ToArray());
            var topology = ServiceTopologyBuilder.Build(built);

            // Basic report to console; you likely will map it back into the window later.
            if (topology.HasBlockingIssues)
            {
                foreach (var c in topology.Cycles)
                {
                    Debug.LogError("[Containable] Cycle: " + c.Kind + " :: " +
                                   string.Join(" -> ", c.Types.Select(t => t.Name).ToArray()));
                }
            }
            else
            {
                Debug.Log("[Containable] Graph OK. Layers: " + topology.Layers.Count);
            }
        }

        private void GenerateRegistry(ContainerSpec container)
        {
            var bindings = new List<(Type iface, Type impl, ServiceLifetime lt)>(container.Rows.Count);

            for (var i = 0; i < container.Rows.Count; i++)
            {
                var row = container.Rows[i];
                var lifetime = row.LifetimeOverride.HasValue ? row.LifetimeOverride.Value : row.LifetimeDefault;

                if (row.UseResolver)
                {
                    if (row.ResolverType == null)
                        throw new InvalidOperationException("Resolver is not selected for interface " +
                                                            row.InterfaceType.FullName);

                    // Emit a special implementation stub that says “resolve via resolver at runtime”
                    // Simpler approach: pick union's first candidate for codegen is WRONG.
                    // Safer approach: generate a resolver-binding entry with impl == null and handle it in container.
                    // To keep v1 simple, we require explicit Implementation for codegen. Resolver-only rows are skipped.
                    Debug.LogWarning("Resolver binding skipped in codegen for interface " + row.InterfaceType.FullName +
                                     " (explicit implementation required for v1).");
                    continue;
                }
                else
                {
                    if (row.ImplementationType == null)
                        throw new InvalidOperationException("Implementation is not selected for interface " +
                                                            row.InterfaceType.FullName);

                    bindings.Add((row.InterfaceType, row.ImplementationType, lifetime));
                }
            }

            const string outDir = "Assets/Generated/Containable";
            ContainerCodeGenerator.EmitRegistry(container.Key, bindings, outDir);
            Debug.Log("[Containable] Generated: " + outDir);
        }
    }
}