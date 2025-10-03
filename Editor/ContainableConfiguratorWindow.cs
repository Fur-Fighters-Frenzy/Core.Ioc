using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Validosik.Core.Ioc;
using Validosik.Core.Ioc.Attributes;
using Validosik.Core.Ioc.Resolvers;
using Validosik.Core.Reflection;
using Validosik.Core.Editor.Ioc.CodeGeneration;

namespace Validosik.Core.Editor.Ioc
{
    /// <summary>
    /// Editor UI for selecting interfaces, resolvers/implementations, lifetimes; persists to JSON;
    /// draws dependency graph between implementations (union for resolvers) with cycle/missing-binding diagnostics.
    /// </summary>
    internal sealed class ContainableConfiguratorWindow : EditorWindow
    {
        // ---------- persistence DTOs ----------
        [Serializable] private sealed class PersistRow
        {
            public string InterfaceType; // AssemblyQualifiedName
            public string InterfaceGuid;
            public string LifetimeDefault; // enum as string
            public bool UseResolver;
            public string ResolverType; // AQN or empty
            public string ImplementationType; // AQN or empty
            public string LifetimeOverride; // enum as string or empty
        }

        [Serializable] private sealed class PersistContainer
        {
            public string Key;
            public List<PersistRow> Rows = new List<PersistRow>();
        }

        [Serializable] private sealed class PersistRoot
        {
            public int Version = 1;
            public List<PersistContainer> Containers = new List<PersistContainer>();
        }

        // ---------- in-memory ----------
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

        // ----------------------- state -----------------------

        private readonly List<Type> _contracts = new List<Type>();
        private readonly List<Type> _implementations = new List<Type>();
        private readonly List<Type> _resolvers = new List<Type>();
        private readonly List<ContainerSpec> _containers = new List<ContainerSpec>();

        private int _containerIndex = -1;
        private string _newContainerKey = "Game";
        private string _searchInterface = "";

        private Vector2 _scrollPicker;
        private Vector2 _scrollBindings;
        private Vector2 _scrollGraph;

        private TopologySnapshot _lastTopology;
        private string[] _lastWarnings;
        private bool _dirty;

        private const string JsonPath = "Assets/ContainableBindings.json";

        // ---------- menu ----------
        [MenuItem("Tools/Containable/Configurator")]
        public static void Open()
        {
            var w = GetWindow<ContainableConfiguratorWindow>("Containable Config");
            w.minSize = new Vector2(1100, 650);
        }

        // ---------- lifecycle ----------
        private void OnEnable()
        {
            try
            {
                ScanWorld();
                LoadJsonIfExists(); // auto-load
                RebuildGraphForActive();
                _dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (!_dirty)
                {
                    return;
                }

                var save = EditorUtility.DisplayDialog("Containable Config",
                    "You have unsaved changes. Save before closing?",
                    "Save", "Don't Save");
                if (save) SaveJson();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ---------- scanning ----------
        private void ScanWorld()
        {
            _contracts.Clear();
            _implementations.Clear();
            _resolvers.Clear();

            var rh = new ReflectionHelper(null);
            rh.CollectAssembliesTypes(false);

            var buf = new List<(Type, ContainableServiceContractAttribute[])>(128);
            rh.CollectTypesWithAttribute<ContainableServiceContractAttribute>(buf, includeClasses: false,
                includeInterfaces: true, inherit: false);
            for (var i = 0; i < buf.Count; ++i)
            {
                _contracts.Add(buf[i].Item1);
            }

            var implBuf = new List<(Type, ContainableServiceImplementationAttribute[])>(256);
            rh.CollectTypesWithAttribute<ContainableServiceImplementationAttribute>(implBuf, includeClasses: true,
                includeInterfaces: false, inherit: false);
            for (var i = 0; i < implBuf.Count; ++i)
            {
                _implementations.Add(implBuf[i].Item1);
            }

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

        // ---------- GUI ----------
        private void OnGUI()
        {
            EditorGUILayout.Space();
            DrawToolbar();

            if (_containers.Count == 0)
            {
                EditorGUILayout.HelpBox("No containers. Create one.", MessageType.Info);
                return;
            }

            _containerIndex = Mathf.Clamp(_containerIndex, 0, _containers.Count - 1);
            _containerIndex = EditorGUILayout.Popup("Active Container", _containerIndex,
                _containers.Select(c => c.Key).ToArray());
            var container = _containers[_containerIndex];

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.52f));
            DrawInterfacePicker(container);
            EditorGUILayout.Space();
            DrawRows(container);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawGraphPanel();
            DrawWarningsPanel();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.LabelField("Containers", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            _newContainerKey = EditorGUILayout.TextField("New Container Key", _newContainerKey);
            if (GUILayout.Button("Add", GUILayout.Width(80)))
            {
                if (!string.IsNullOrEmpty(_newContainerKey) && !_containers.Any(c => c.Key == _newContainerKey))
                {
                    _containers.Add(new ContainerSpec { Key = _newContainerKey });
                    _containerIndex = _containers.Count - 1;
                    _lastTopology = null;
                    _lastWarnings = null;
                    _dirty = true;
                }
            }

            if (GUILayout.Button("Remove Current", GUILayout.Width(140)) && _containerIndex >= 0)
            {
                _containers.RemoveAt(_containerIndex);
                _containerIndex = Mathf.Clamp(_containerIndex - 1, -1, _containers.Count - 1);
                _lastTopology = null;
                _lastWarnings = null;
                _dirty = true;
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Rebuild Graph (Union)", GUILayout.Width(180)))
            {
                try
                {
                    RebuildGraphForActive();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (GUILayout.Button("Generate All Registries", GUILayout.Width(200)))
            {
                try
                {
                    GenerateAllRegistries();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (GUILayout.Button("Save JSON", GUILayout.Width(100)))
            {
                try
                {
                    SaveJson();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // picker
        private void DrawInterfacePicker(ContainerSpec container)
        {
            EditorGUILayout.LabelField("Add Interface", EditorStyles.boldLabel);
            _searchInterface = EditorGUILayout.TextField("Search", _searchInterface ?? "");

            _scrollPicker = EditorGUILayout.BeginScrollView(_scrollPicker, GUILayout.Height(180));

            var filtered = _contracts
                .Where(t => t.Name.IndexOf(_searchInterface ?? "", StringComparison.OrdinalIgnoreCase) >= 0
                            || (t.FullName != null &&
                                t.FullName.IndexOf(_searchInterface ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(t => !container.Rows.Any(r => r.InterfaceType == t))
                .OrderBy(t => t.Name)
                .ToArray();

            for (var i = 0; i < filtered.Length; ++i)
            {
                var t = filtered[i];
                EditorGUILayout.BeginHorizontal();
                var gc = new GUIContent(ShortType(t), t.FullName);
                EditorGUILayout.LabelField(gc, GUILayout.MinWidth(500));
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
                    _lastTopology = null;
                    _lastWarnings = null;
                    _dirty = true;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // bindings
        private void DrawRows(ContainerSpec container)
        {
            EditorGUILayout.LabelField("Bindings", EditorStyles.boldLabel);

            _scrollBindings = EditorGUILayout.BeginScrollView(_scrollBindings, GUILayout.Height(260));
            if (container.Rows.Count == 0)
            {
                EditorGUILayout.HelpBox("No bindings in this container. Add interfaces above.", MessageType.None);
                EditorGUILayout.EndScrollView();
                return;
            }

            for (var i = 0; i < container.Rows.Count; ++i)
            {
                var row = container.Rows[i];
                EditorGUILayout.BeginVertical("box");
                var head = new GUIContent($"{ShortType(row.InterfaceType)}  [{row.InterfaceGuid}]",
                    row.InterfaceType.FullName);
                EditorGUILayout.LabelField(head);

                EditorGUILayout.BeginHorizontal();

                var newUseResolver = EditorGUILayout.ToggleLeft("Use Resolver", row.UseResolver, GUILayout.Width(120));
                if (newUseResolver != row.UseResolver)
                {
                    row.UseResolver = newUseResolver;
                    row.ResolverType = null;
                    row.ImplementationType = null;
                    _lastTopology = null;
                    _lastWarnings = null;
                    _dirty = true;
                }

                if (row.UseResolver)
                {
                    var resolverCandidates = _resolvers
                        .Where(r => r.GetInterfaces().Any(it =>
                            it.IsGenericType && it.GetGenericTypeDefinition() == typeof(IContainableResolver<>)
                                             && it.GetGenericArguments()[0] == row.InterfaceType))
                        .OrderBy(t => t.Name).ToArray();

                    var idx = Array.IndexOf(resolverCandidates, row.ResolverType);
                    var names = resolverCandidates.Select(ShortType).ToArray();
                    var newIdx = EditorGUILayout.Popup(new GUIContent("Resolver"), Mathf.Max(0, idx), names);
                    if (resolverCandidates.Length > 0 && resolverCandidates[newIdx] != row.ResolverType)
                    {
                        row.ResolverType = resolverCandidates[newIdx];
                        _dirty = true;
                    }
                }
                else
                {
                    var implCandidates = _implementations
                        .Where(impl => row.InterfaceType.IsAssignableFrom(impl))
                        .OrderBy(t => t.Name).ToArray();

                    var idx = Array.IndexOf(implCandidates, row.ImplementationType);
                    var names = implCandidates.Select(ShortType).ToArray();
                    var newIdx = EditorGUILayout.Popup(new GUIContent("Implementation"), Mathf.Max(0, idx), names);
                    if (implCandidates.Length > 0 && implCandidates[newIdx] != row.ImplementationType)
                    {
                        row.ImplementationType = implCandidates[newIdx];
                        _dirty = true;
                    }
                }

                GUI.enabled = !row.UseResolver;
                var lifetime = row.LifetimeOverride ?? row.LifetimeDefault;
                var newLt = (ServiceLifetime)EditorGUILayout.EnumPopup("Lifetime", lifetime, GUILayout.Width(260));
                if (newLt != lifetime)
                {
                    row.LifetimeOverride = newLt;
                    _dirty = true;
                }

                GUI.enabled = true;

                if (GUILayout.Button("Remove", GUILayout.Width(90)))
                {
                    container.Rows.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    _lastTopology = null;
                    _lastWarnings = null;
                    _dirty = true;
                    EditorGUILayout.EndScrollView();
                    return;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        // graph panel
        private void DrawGraphPanel()
        {
            EditorGUILayout.LabelField("Initialization Graph (Implementations, Union)", EditorStyles.boldLabel);

            var rect = GUILayoutUtility.GetRect(100, 400, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, new Color(0.11f, 0.11f, 0.11f, 1f));

            if (_lastTopology == null || _lastTopology.Components.Count == 0)
            {
                GUI.Label(rect, "No graph. Click 'Rebuild Graph (Union)'.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            _scrollGraph = GUI.BeginScrollView(rect, _scrollGraph, new Rect(0, 0,
                Mathf.Max(rect.width, _lastTopology.CanvasWidth),
                Mathf.Max(rect.height, _lastTopology.CanvasHeight)));

            DrawGraphContents(_lastTopology);
            GUI.EndScrollView();
        }

        private void DrawGraphContents(TopologySnapshot snapshot)
        {
            Handles.BeginGUI();
            for (var i = 0; i < snapshot.Edges.Count; ++i)
            {
                var e = snapshot.Edges[i];
                var from = snapshot.NodeRects[e.FromId];
                var to = snapshot.NodeRects[e.ToId];

                var start = new Vector3(from.xMax, from.center.y, 0);
                var end = new Vector3(to.xMin, to.center.y, 0);
                var tanA = start + Vector3.right * 50f;
                var tanB = end + Vector3.left * 50f;

                var col = e.IsCyclicEdge ? Color.red : new Color(0.7f, 0.7f, 0.7f, 1f);
                Handles.DrawBezier(start, end, tanA, tanB, col, null, 2f);
            }

            Handles.EndGUI();

            for (var id = 0; id < snapshot.Components.Count; id++)
            {
                var rect = snapshot.NodeRects[id];
                var c = snapshot.Components[id];
                var isCycle = c.IsCyclic;

                var bg = isCycle ? new Color(0.45f, 0.15f, 0.15f, 1f) : new Color(0.18f, 0.2f, 0.23f, 1f);
                var border = isCycle ? Color.red : new Color(0.35f, 0.35f, 0.4f, 1f);

                EditorGUI.DrawRect(rect, bg);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), border);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), border);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), border);
                EditorGUI.DrawRect(new Rect(rect.xMax - 2, rect.y, 2, rect.height), border);

                var style = new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter, wordWrap = true };

                // Show implementation names; CompositeNode already prefers service types
                var gc = new GUIContent(c.DisplayName);
                GUI.Label(new Rect(rect.x + 6, rect.y + 6, rect.width - 12, rect.height - 12), gc, style);
            }
        }

        private void DrawWarningsPanel()
        {
            var warnings = _lastWarnings ?? Array.Empty<string>();
            if (warnings.Length == 0) return;

            EditorGUILayout.Space();
            foreach (var w in warnings)
                EditorGUILayout.HelpBox(w, MessageType.Error);
        }

        // ---------- actions ----------
        private void RebuildGraphForActive()
        {
            if (_containers.Count == 0 || _containerIndex < 0)
            {
                _lastTopology = null;
                _lastWarnings = null;
                return;
            }

            var container = _containers[_containerIndex];

            // Build graph between IMPLEMENTATIONS (union for resolvers)
            var (graph, missing) = BuildImplementationGraph(container);
            var topology = ServiceTopologyBuilder.Build(graph);
            _lastTopology = LayoutForGui(topology);

            var cyc = topology.HasBlockingIssues
                ? topology.Cycles.Select(c =>
                    "Cycle: " + c.Kind + " :: " + string.Join(" -> ", c.Types.Select(t => t.Name).ToArray()))
                : Enumerable.Empty<string>();

            _lastWarnings = cyc.Concat(missing).Distinct().ToArray();
            Repaint();
        }

        /// <summary>
        /// Build a ServiceGraph where nodes are implementation types and edges point to implementation types
        /// according to the current container selection. If a dependency interface has a resolver, union all candidates.
        /// Missing bindings are reported and edges are not created for them.
        /// </summary>
        private (ServiceGraph graph, List<string> missingWarnings) BuildImplementationGraph(ContainerSpec container)
        {
            var graph = new ServiceGraph();
            var missing = new List<string>();

            // helper: map interface -> row
            var byInterface = container.Rows.ToDictionary(r => r.InterfaceType, r => r);

            // helper: get candidate implementation types for an interface based on selection
            List<Type> GetCandidates(Type interfaceType)
            {
                if (!byInterface.TryGetValue(interfaceType, out var row)) // interface not bound in container
                {
                    return null;
                }

                if (row.UseResolver)
                {
                    if (row.ResolverType == null) return new List<Type>(); // no resolver selected yet
                    var inst = Activator.CreateInstance(row.ResolverType);
                    var describe = row.ResolverType.GetMethod("Describe");
                    var manifest = describe != null ? describe.Invoke(inst, null) : null;
                    var prop = manifest?.GetType().GetProperty("CandidateTypes");
                    var types = prop != null ? (IEnumerable<Type>)prop.GetValue(manifest, null) : null;
                    return types != null
                        ? types.Where(t => t != null && interfaceType.IsAssignableFrom(t)).Distinct().ToList()
                        : new List<Type>();
                }
                else
                {
                    return row.ImplementationType != null
                        ? new List<Type> { row.ImplementationType }
                        : new List<Type>();
                }
            }

            // We must add nodes for all selected implementations and resolver candidates
            var implSet = new HashSet<Type>();
            foreach (var row in container.Rows)
            {
                var candidates = row.UseResolver
                    ? GetCandidates(row.InterfaceType)
                    : (row.ImplementationType != null ? new List<Type> { row.ImplementationType } : new List<Type>());
                if (candidates == null)
                {
                    continue;
                }

                foreach (var t in candidates)
                {
                    implSet.Add(t);
                }
            }

            // For each implementation, compute deps -> candidate implementations and add edges
            foreach (var impl in implSet)
            {
                var ctor = PickConstructor(impl);
                var deps = ctor != null ? ctor.GetParameters() : Array.Empty<ParameterInfo>();
                var depImpls = new List<Type>();

                foreach (var p in deps)
                {
                    var dependencyInterfaceType = UnwrapParamType(p);
                    if (dependencyInterfaceType is not { IsInterface: true })
                    {
                        continue;
                    }

                    var candidates = GetCandidates(dependencyInterfaceType);
                    if (candidates == null)
                    {
                        missing.Add(
                            $"Missing binding: {impl.Name} depends on {dependencyInterfaceType.Name}, but {dependencyInterfaceType.Name} is not added to this container.");
                        continue;
                    }

                    if (candidates.Count == 0)
                    {
                        missing.Add(
                            $"Missing binding: {impl.Name} depends on {dependencyInterfaceType.Name}, but no implementation/resolver candidates are configured.");
                        continue;
                    }

                    // union edges to all possible impls in this container for that interface
                    foreach (var c in candidates) depImpls.Add(c);
                }

                // Attach the node with implementation-level dependencies
                // Use a synthetic ContractAttribute just to satisfy ServiceGraph API (guid/lifetime are irrelevant here)
                var fakeAttr = new ContainableServiceContractAttribute("impl", ServiceLifetime.Scoped);
                graph.Add(impl, fakeAttr, depImpls.Distinct());
            }

            return (graph, missing.Distinct().ToList());
        }

        // ctor helpers (same rules as runtime)
        private static ConstructorInfo PickConstructor(Type t)
        {
            var constructors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (constructors.Length == 1)
            {
                return constructors[0];
            }

            var withInject =
                constructors.FirstOrDefault(
                    c => c.GetCustomAttributes().Any(a => a.GetType().Name == "InjectAttribute"));
            if (withInject != null)
            {
                return withInject;
            }

            return constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        }

        private static Type UnwrapParamType(ParameterInfo info)
        {
            var pt = info.ParameterType;
            if (pt.IsGenericType)
            {
                var def = pt.GetGenericTypeDefinition();
                if (def == typeof(IEnumerable<>) || def == typeof(Lazy<>) || def == typeof(Func<>))
                    return pt.GetGenericArguments()[0];
            }

            if (pt.IsPrimitive || pt == typeof(string)) return null;
            return pt;
        }

        private void GenerateAllRegistries()
        {
            if (_containers.Count == 0)
            {
                Debug.LogWarning("[Containable] No containers to generate.");
                return;
            }

            var all =
                new List<(string containerKey, IList<(Type iface, Type impl, ServiceLifetime lt, Type resolver)>
                    bindings)>(_containers.Count);

            for (var c = 0; c < _containers.Count; ++c)
            {
                var cont = _containers[c];
                var bindings = new List<(Type iface, Type impl, ServiceLifetime lt, Type resolver)>(cont.Rows.Count);

                for (var i = 0; i < cont.Rows.Count; ++i)
                {
                    var row = cont.Rows[i];

                    bindings.Add((
                        row.InterfaceType,
                        row.UseResolver ? null : row.ImplementationType,
                        row.LifetimeOverride ?? row.LifetimeDefault,
                        row.UseResolver ? row.ResolverType : null
                    ));
                }

                all.Add((cont.Key, bindings));
            }

            const string outDir = "Assets/Generated/Containable";
            ContainerCodeGenerator.EmitAll(outDir, all);
            Debug.Log("[Containable] Generated ALL registries and index into: " + outDir);
        }

        // ---------- JSON ----------
        private void SaveJson()
        {
            var root = new PersistRoot();

            for (var c = 0; c < _containers.Count; c++)
            {
                var cont = _containers[c];
                var pc = new PersistContainer { Key = cont.Key };

                for (var i = 0; i < cont.Rows.Count; ++i)
                {
                    var r = cont.Rows[i];
                    pc.Rows.Add(new PersistRow
                    {
                        InterfaceType = r.InterfaceType.AssemblyQualifiedName,
                        InterfaceGuid = r.InterfaceGuid,
                        LifetimeDefault = r.LifetimeDefault.ToString(),
                        UseResolver = r.UseResolver,
                        ResolverType = r.ResolverType != null ? r.ResolverType.AssemblyQualifiedName : "",
                        ImplementationType = r.ImplementationType != null
                            ? r.ImplementationType.AssemblyQualifiedName
                            : "",
                        LifetimeOverride = r.LifetimeOverride.HasValue ? r.LifetimeOverride.Value.ToString() : ""
                    });
                }

                root.Containers.Add(pc);
            }

            var json = JsonUtility.ToJson(root, true);
            Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
            File.WriteAllText(JsonPath, json);
            _dirty = false;
            Debug.Log("[Containable] Saved JSON: " + JsonPath);
        }

        private void LoadJsonIfExists()
        {
            if (!File.Exists(JsonPath)) return;
            LoadJson();
        }

        private void LoadJson()
        {
            var json = File.ReadAllText(JsonPath);
            var root = JsonUtility.FromJson<PersistRoot>(json);
            _containers.Clear();

            for (var c = 0; c < root.Containers.Count; c++)
            {
                var pc = root.Containers[c];
                var cont = new ContainerSpec { Key = pc.Key };

                for (var i = 0; i < pc.Rows.Count; ++i)
                {
                    var pr = pc.Rows[i];
                    var row = new Row
                    {
                        InterfaceType = ResolveType(pr.InterfaceType),
                        InterfaceGuid = pr.InterfaceGuid,
                        LifetimeDefault = ParseEnum(pr.LifetimeDefault, ServiceLifetime.Scoped),
                        UseResolver = pr.UseResolver,
                        ResolverType = string.IsNullOrEmpty(pr.ResolverType) ? null : ResolveType(pr.ResolverType),
                        ImplementationType = string.IsNullOrEmpty(pr.ImplementationType)
                            ? null
                            : ResolveType(pr.ImplementationType),
                        LifetimeOverride = string.IsNullOrEmpty(pr.LifetimeOverride)
                            ? (ServiceLifetime?)null
                            : ParseEnum(pr.LifetimeOverride, ServiceLifetime.Scoped)
                    };
                    if (row.InterfaceType != null) cont.Rows.Add(row);
                }

                _containers.Add(cont);
            }

            _containerIndex = _containers.Count > 0 ? 0 : -1;
            _dirty = false;
            Debug.Log("[Containable] Loaded JSON: " + JsonPath);
        }

        private static Type ResolveType(string aqn)
        {
            if (string.IsNullOrEmpty(aqn)) return null;
            var t = Type.GetType(aqn);
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = a.GetType(aqn, throwOnError: false);
                    if (t != null) return t;
                }
                catch
                {
                }
            }

            return null;
        }

        private static T ParseEnum<T>(string @enum, T @default) where T : struct
            => Enum.TryParse<T>(@enum, out var value) ? value : @default;

        private static string ShortType(Type t)
        {
            if (t == null) return "<null>";
            var n = t.FullName ?? t.Name;
            if (n.Length <= 64) return n;
            return "…" + n[^63..];
        }

        // ---------- graph layout ----------
        private sealed class TopologySnapshot
        {
            public readonly List<ServiceTopologyBuilder.CompositeNode> Components =
                new List<ServiceTopologyBuilder.CompositeNode>();

            public readonly List<(int FromId, int ToId, bool IsCyclicEdge)> Edges = new List<(int, int, bool)>();
            public readonly Dictionary<int, Rect> NodeRects = new Dictionary<int, Rect>();
            public float CanvasWidth;
            public float CanvasHeight;
        }

        private TopologySnapshot LayoutForGui(ServiceTopologyBuilder.ServiceTopology topology)
        {
            var snap = new TopologySnapshot();
            var compToId = new Dictionary<ServiceTopologyBuilder.CompositeNode, int>();
            for (var i = 0; i < topology.Components.Count; ++i)
            {
                var c = topology.Components[i];
                compToId[c] = i;
                snap.Components.Add(c);
            }

            // edges
            for (var i = 0; i < topology.Edges.Count; ++i)
            {
                var e = topology.Edges[i];
                var fromId = compToId[e.From];
                var toId = compToId[e.To];
                var isCyclicEdge = e.From.IsCyclic || e.To.IsCyclic;
                snap.Edges.Add((fromId, toId, isCyclicEdge));
            }

            // layout by layers: deeper on the right
            const float nodeW = 260f;
            const float nodeH = 72f;
            const float gapX = 140f;
            const float gapY = 18f;

            for (int d = topology.Layers.Count - 1, col = 0; d >= 0; d--, col++)
            {
                var column = topology.Layers[d];
                var x = col * (nodeW + gapX);
                for (var r = 0; r < column.Count; r++)
                {
                    var comp = column[r];
                    var id = compToId[comp];
                    var y = r * (nodeH + gapY);
                    snap.NodeRects[id] = new Rect(x + 12, y + 12, nodeW, nodeH);

                    snap.CanvasWidth = Math.Max(snap.CanvasWidth, x + nodeW + 40);
                    snap.CanvasHeight = Math.Max(snap.CanvasHeight, y + nodeH + 40);
                }
            }

            // ensure something visible
            if (snap.CanvasWidth < 900) snap.CanvasWidth = 900;
            if (snap.CanvasHeight < 420) snap.CanvasHeight = 420;

            return snap;
        }
    }
}