using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace JaimeCamacho.VAT.Editor
{
    public class VATsToolWindow : EditorWindow
    {
        private const float k_DefaultFrameSampleStep = 0.05f;
        private const string k_KernelName = "CSMain";
        private const string k_DefaultComputeShaderName = "MeshInfoTextureGen";
        private const string k_DefaultComputeShaderPath = "Packages/com.jaimecamacho.vat/Editor/Tools/AnimationTextureBaker/MeshInfoTextureGen.compute";

        private ComputeShader infoTexGen;
        private GameObject targetObject;
        private string outputPath = "Assets/BakedAnimationTex";
        private Vector2 scrollPosition;
        private string statusMessage = string.Empty;
        private MessageType statusMessageType = MessageType.Info;

        private const string k_PaintRootName = "VATPaintRoot";
        private static readonly Color k_BrushFillColor = new Color(0f, 0.5f, 1f, 0.25f);
        private static readonly Color k_BrushOutlineColor = Color.cyan;

        private enum ToolTab
        {
            VatBaker,
            VatPainter
        }

        private static readonly GUIContent[] k_ToolTabLabels =
        {
            new GUIContent("VAT Baker"),
            new GUIContent("VAT Painter")
        };

        private int activeTabIndex;

        [Serializable]
        private class PaintGroup
        {
            public string groupName = "New Group";
            public string id;
            public List<MeshFilter> meshFilters = new List<MeshFilter>();
            public List<Material> vatMaterials = new List<Material>();
            public bool isExpanded = true;

            public PaintGroup()
            {
                id = Guid.NewGuid().ToString("N");
            }
        }

        private readonly List<PaintGroup> paintGroups = new List<PaintGroup>();
        private readonly Dictionary<PaintGroup, List<Transform>> paintGroupParents = new Dictionary<PaintGroup, List<Transform>>();
        private readonly List<PaintGroup> reusablePaintGroups = new List<PaintGroup>();
        private readonly List<int> reusableMeshFilterIndices = new List<int>();
        private readonly List<int> reusableMaterialIndices = new List<int>();

        private Transform painterFocusTarget;
        private GameObject painterSurface;
        private MeshCollider painterSurfaceCollider;
        private bool painterPaintingMode;
        private GameObject painterRoot;
        private float painterBrushRadius = 2f;
        private int painterBrushDensity = 5;
        private float painterMinDistance = 0.5f;

        [MenuItem("Tools/JaimeCamachoDev/VATsTool")]
        [MenuItem("Assets/JaimeCamachoDev/VATsTool")]
        public static void ShowWindow()
        {
            var window = GetWindow<VATsToolWindow>("VATs Tool");
            window.minSize = new Vector2(420f, 320f);
        }

        private void OnEnable()
        {
            TryAssignDefaultComputeShader();
        }

        private void OnFocus()
        {
            TryAssignDefaultComputeShader();
        }

        private void OnDisable()
        {
            if (painterPaintingMode)
            {
                TogglePaintingMode(false);
            }
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            int newTabIndex = GUILayout.Toolbar(activeTabIndex, k_ToolTabLabels);
            if (EditorGUI.EndChangeCheck())
            {
                activeTabIndex = newTabIndex;
                scrollPosition = Vector2.zero;
            }

            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            switch ((ToolTab)activeTabIndex)
            {
                case ToolTab.VatBaker:
                    DrawVatBakerSection();
                    break;
                case ToolTab.VatPainter:
                    DrawVatPainterSection();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawVatBakerSection()
        {
            EditorGUILayout.LabelField("VAT Baker", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Bakes VAT position textures for every animation clip on the target object's Animator.", MessageType.Info);

            infoTexGen = (ComputeShader)EditorGUILayout.ObjectField("Compute Shader", infoTexGen, typeof(ComputeShader), false);
            if (infoTexGen == null)
            {
                EditorGUILayout.HelpBox($"If left empty, the tool will try to use '{k_DefaultComputeShaderName}'.", MessageType.Info);
            }
            targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);

            DrawTargetObjectDiagnostics();

            Rect pathRect = EditorGUILayout.GetControlRect();
            pathRect = EditorGUI.PrefixLabel(pathRect, new GUIContent("Output Path"));
            outputPath = EditorGUI.TextField(pathRect, outputPath);
            HandleDragAndDrop(pathRect);

            if (GUILayout.Button("Select Folder"))
            {
                string selectedFolder = EditorUtility.OpenFolderPanel("Select Output Folder", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    string projectRelativePath = ConvertToProjectRelativePath(selectedFolder);
                    if (!string.IsNullOrEmpty(projectRelativePath))
                    {
                        outputPath = projectRelativePath;
                        Repaint();
                    }
                    else
                    {
                        ReportStatus("The selected folder must be inside the project's Assets directory.", MessageType.Error);
                    }
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!CanBake()))
            {
                if (GUILayout.Button("Bake VAT Position Textures"))
                {
                    BakeVatPositionTextures();
                }
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(statusMessage, statusMessageType);
            }

        }

        private void DrawVatPainterSection()
        {
            EditorGUILayout.LabelField("VAT Painter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Paint VAT-ready prefabs on top of a surface using groups of meshes and VAT materials.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Paint Group"))
                {
                    paintGroups.Add(new PaintGroup { groupName = GenerateUniqueGroupName() });
                    InvalidatePainterHierarchy();
                }

                using (new EditorGUI.DisabledScope(GetPaintRoot(false) == null))
                {
                    if (GUILayout.Button("Clear Painted Instances"))
                    {
                        ClearPaintedInstances();
                    }
                }
            }

            EditorGUILayout.Space();

            for (int i = 0; i < paintGroups.Count; i++)
            {
                if (DrawPaintGroup(paintGroups[i], i))
                {
                    i--;
                }
            }

            if (paintGroups.Count == 0)
            {
                EditorGUILayout.HelpBox("No paint groups defined yet. Add one to start painting VAT characters.", MessageType.Info);
            }

            EditorGUILayout.Space();

            painterFocusTarget = (Transform)EditorGUILayout.ObjectField("Focus Target", painterFocusTarget, typeof(Transform), true);

            GameObject newSurface = (GameObject)EditorGUILayout.ObjectField("Paint Surface", painterSurface, typeof(GameObject), true);
            if (newSurface != painterSurface)
            {
                painterSurface = newSurface;
                UpdatePaintSurfaceCollider();
            }

            painterBrushRadius = EditorGUILayout.Slider("Brush Radius", painterBrushRadius, 0.05f, 25f);
            painterBrushDensity = EditorGUILayout.IntSlider("Brush Density", painterBrushDensity, 1, 64);
            painterMinDistance = EditorGUILayout.Slider("Min Distance Between Instances", painterMinDistance, 0f, 10f);

            DrawPainterDiagnostics();

            bool requestedMode = GUILayout.Toggle(painterPaintingMode, "Enable Painting Mode", "Button");
            if (requestedMode != painterPaintingMode)
            {
                TogglePaintingMode(requestedMode);
            }

            if (painterPaintingMode)
            {
                EditorGUILayout.HelpBox("Left-click in the Scene view to paint VAT instances. Hold Alt to keep navigating the camera.", MessageType.Info);
            }
        }

        private string GenerateUniqueGroupName()
        {
            const string baseName = "Paint Group";
            int index = paintGroups.Count + 1;

            while (true)
            {
                string candidate = $"{baseName} {index}";
                bool exists = false;

                foreach (PaintGroup group in paintGroups)
                {
                    if (string.Equals(group.groupName, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    return candidate;
                }

                index++;
            }
        }

        private bool DrawPaintGroup(PaintGroup group, int index)
        {
            bool removeGroup = false;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            group.isExpanded = EditorGUILayout.Foldout(group.isExpanded, group.groupName, true);
            if (GUILayout.Button("Remove", GUILayout.Width(70f)))
            {
                removeGroup = true;
            }
            EditorGUILayout.EndHorizontal();

            if (group.isExpanded)
            {
                EditorGUI.indentLevel++;

                string newName = EditorGUILayout.TextField("Group Name", group.groupName);
                if (!string.Equals(newName, group.groupName, StringComparison.Ordinal))
                {
                    group.groupName = newName;
                    InvalidatePainterHierarchy(group);
                }

                DrawMeshFilterList(group);
                DrawMaterialList(group);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            if (removeGroup)
            {
                InvalidatePainterHierarchy(group);
                paintGroupParents.Remove(group);
                paintGroups.RemoveAt(index);
                return true;
            }

            return false;
        }

        private void DrawMeshFilterList(PaintGroup group)
        {
            EditorGUILayout.LabelField("Mesh Filters", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            for (int i = 0; i < group.meshFilters.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                group.meshFilters[i] = (MeshFilter)EditorGUILayout.ObjectField(group.meshFilters[i], typeof(MeshFilter), true);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                {
                    group.meshFilters.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Mesh Filter"))
                {
                    group.meshFilters.Add(null);
                }

                if (GUILayout.Button("Add Selected"))
                {
                    AddSelectedMeshFilters(group);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawMaterialList(PaintGroup group)
        {
            EditorGUILayout.LabelField("VAT Materials", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            for (int i = 0; i < group.vatMaterials.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                group.vatMaterials[i] = (Material)EditorGUILayout.ObjectField(group.vatMaterials[i], typeof(Material), false);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                {
                    group.vatMaterials.RemoveAt(i);
                    i--;
                    InvalidatePainterHierarchy(group);
                }
                EditorGUILayout.EndHorizontal();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Material"))
                {
                    group.vatMaterials.Add(null);
                    InvalidatePainterHierarchy(group);
                }

                if (GUILayout.Button("Add Selected"))
                {
                    if (AddSelectedMaterials(group))
                    {
                        InvalidatePainterHierarchy(group);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private void AddSelectedMeshFilters(PaintGroup group)
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            bool added = false;

            foreach (GameObject go in selectedObjects)
            {
                if (go == null)
                {
                    continue;
                }

                MeshFilter filter = go.GetComponent<MeshFilter>();
                if (filter != null && !group.meshFilters.Contains(filter))
                {
                    group.meshFilters.Add(filter);
                    added = true;
                }
            }

            if (added)
            {
                Repaint();
            }
        }

        private bool AddSelectedMaterials(PaintGroup group)
        {
            bool added = false;

            foreach (UnityEngine.Object obj in Selection.objects)
            {
                if (obj is Material material && !group.vatMaterials.Contains(material))
                {
                    group.vatMaterials.Add(material);
                    added = true;
                }
            }

            if (!added)
            {
                foreach (GameObject go in Selection.gameObjects)
                {
                    if (go == null)
                    {
                        continue;
                    }

                    Renderer renderer = go.GetComponent<Renderer>();
                    if (renderer == null)
                    {
                        continue;
                    }

                    foreach (Material shared in renderer.sharedMaterials)
                    {
                        if (shared != null && !group.vatMaterials.Contains(shared))
                        {
                            group.vatMaterials.Add(shared);
                            added = true;
                        }
                    }
                }
            }

            if (added)
            {
                Repaint();
            }

            return added;
        }

        private void DrawPainterDiagnostics()
        {
            if (painterSurface == null)
            {
                EditorGUILayout.HelpBox("Assign a Paint Surface with a MeshCollider to receive brush strokes.", MessageType.Info);
            }
            else if (painterSurfaceCollider == null)
            {
                EditorGUILayout.HelpBox("The selected Paint Surface does not contain a MeshCollider component.", MessageType.Warning);
            }

            if (!HasAnyValidPaintGroup())
            {
                EditorGUILayout.HelpBox("Create at least one paint group with valid mesh filters and VAT materials to start painting.", MessageType.Warning);
            }

            if (painterFocusTarget == null)
            {
                EditorGUILayout.HelpBox("Assign a Focus Target to orient painted instances. Without it, instances keep their original forward direction.", MessageType.Info);
            }
        }

        private void InvalidatePainterHierarchy(PaintGroup group = null)
        {
            if (group == null)
            {
                paintGroupParents.Clear();
                return;
            }

            paintGroupParents.Remove(group);
        }

        private void UpdatePaintSurfaceCollider()
        {
            painterSurfaceCollider = painterSurface != null ? painterSurface.GetComponent<MeshCollider>() : null;
        }

        private GameObject GetPaintRoot(bool createIfMissing)
        {
            if (painterRoot != null)
            {
                return painterRoot;
            }

            painterRoot = GameObject.Find(k_PaintRootName);
            if (painterRoot == null && createIfMissing)
            {
                painterRoot = new GameObject(k_PaintRootName);
                Undo.RegisterCreatedObjectUndo(painterRoot, "Create VAT Paint Root");
            }

            return painterRoot;
        }

        private void ClearPaintedInstances()
        {
            GameObject root = GetPaintRoot(false);
            if (root == null)
            {
                return;
            }

            Undo.DestroyObjectImmediate(root);
            painterRoot = null;
            paintGroupParents.Clear();
        }

        private void TogglePaintingMode(bool enable)
        {
            if (painterPaintingMode == enable)
            {
                return;
            }

            painterPaintingMode = enable;

            if (enable)
            {
                UpdatePaintSurfaceCollider();
                SceneView.duringSceneGui += HandlePainterSceneGUI;
            }
            else
            {
                SceneView.duringSceneGui -= HandlePainterSceneGUI;
                painterRoot = null;
                paintGroupParents.Clear();
            }

            SceneView.RepaintAll();
        }

        private bool PainterHasValidSetup()
        {
            return painterSurfaceCollider != null && HasAnyValidPaintGroup();
        }

        private void HandlePainterSceneGUI(SceneView sceneView)
        {
            if (!painterPaintingMode)
            {
                return;
            }

            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            if (current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (!PainterHasValidSetup())
            {
                return;
            }

            Ray guiRay = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!TryGetPaintHit(guiRay, out RaycastHit hit))
            {
                return;
            }

            DrawBrushPreview(hit);

            if (current.alt)
            {
                return;
            }

            bool shouldPaint = (current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 0;
            if (shouldPaint && PaintAtRayHit(hit))
            {
                current.Use();
            }
        }

        private bool TryGetPaintHit(Ray ray, out RaycastHit hit)
        {
            if (painterSurfaceCollider != null)
            {
                return painterSurfaceCollider.Raycast(ray, out hit, 10000f);
            }

            hit = default;
            return false;
        }

        private void DrawBrushPreview(RaycastHit hit)
        {
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = k_BrushFillColor;
            Handles.DrawSolidDisc(hit.point, hit.normal, painterBrushRadius);
            Handles.color = k_BrushOutlineColor;
            Handles.DrawWireDisc(hit.point, hit.normal, painterBrushRadius);
        }

        private static void BuildBrushFrame(Vector3 normal, out Vector3 tangent, out Vector3 bitangent)
        {
            tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 1e-6f)
            {
                tangent = Vector3.Cross(normal, Vector3.right);
            }

            tangent.Normalize();
            bitangent = Vector3.Cross(normal, tangent).normalized;
        }

        private bool PaintAtRayHit(RaycastHit hit)
        {
            bool paintedAny = false;

            BuildBrushFrame(hit.normal, out Vector3 tangent, out Vector3 bitangent);

            for (int i = 0; i < painterBrushDensity; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * painterBrushRadius;
                Vector3 samplePoint = hit.point + tangent * offset.x + bitangent * offset.y;
                Ray offsetRay = new Ray(samplePoint + hit.normal * 0.5f, -hit.normal);

                if (!TryGetPaintHit(offsetRay, out RaycastHit offsetHit))
                {
                    continue;
                }

                if (painterMinDistance > 0f && IsTooClose(offsetHit.point))
                {
                    continue;
                }

                if (PaintInstanceAt(offsetHit.point, offsetHit.normal))
                {
                    paintedAny = true;
                }
            }

            if (paintedAny)
            {
                SceneView.RepaintAll();
            }

            return paintedAny;
        }

        private bool PaintInstanceAt(Vector3 position, Vector3 normal)
        {
            PaintGroup group = GetRandomValidGroup();
            if (group == null)
            {
                return false;
            }

            MeshFilter meshFilter = GetRandomMeshFilter(group);
            if (meshFilter == null)
            {
                return false;
            }

            Material material = GetRandomMaterial(group, out int materialIndex);
            if (material == null)
            {
                return false;
            }

            GameObject instance = CreateInstanceFromSource(meshFilter.gameObject);
            if (instance == null)
            {
                return false;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Paint VAT Instance");
            instance.transform.position = position;

            AlignPaintedInstance(instance.transform, position, normal);

            MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Undo.RecordObject(renderer, "Assign VAT Material");
                renderer.sharedMaterial = material;
            }

            Transform parent = GetOrCreateGroupParent(group, materialIndex);
            if (parent != null)
            {
                Undo.SetTransformParent(instance.transform, parent, "Assign Paint Parent");
                instance.transform.position = position;
            }

            return true;
        }

        private PaintGroup GetRandomValidGroup()
        {
            reusablePaintGroups.Clear();

            foreach (PaintGroup group in paintGroups)
            {
                if (GroupHasValidMesh(group) && GroupHasValidMaterial(group))
                {
                    reusablePaintGroups.Add(group);
                }
            }

            if (reusablePaintGroups.Count == 0)
            {
                return null;
            }

            int selected = UnityEngine.Random.Range(0, reusablePaintGroups.Count);
            return reusablePaintGroups[selected];
        }

        private MeshFilter GetRandomMeshFilter(PaintGroup group)
        {
            reusableMeshFilterIndices.Clear();

            for (int i = 0; i < group.meshFilters.Count; i++)
            {
                if (group.meshFilters[i] != null)
                {
                    reusableMeshFilterIndices.Add(i);
                }
            }

            if (reusableMeshFilterIndices.Count == 0)
            {
                return null;
            }

            int selected = reusableMeshFilterIndices[UnityEngine.Random.Range(0, reusableMeshFilterIndices.Count)];
            return group.meshFilters[selected];
        }

        private Material GetRandomMaterial(PaintGroup group, out int materialIndex)
        {
            reusableMaterialIndices.Clear();

            for (int i = 0; i < group.vatMaterials.Count; i++)
            {
                if (group.vatMaterials[i] != null)
                {
                    reusableMaterialIndices.Add(i);
                }
            }

            if (reusableMaterialIndices.Count == 0)
            {
                materialIndex = -1;
                return null;
            }

            materialIndex = reusableMaterialIndices[UnityEngine.Random.Range(0, reusableMaterialIndices.Count)];
            return group.vatMaterials[materialIndex];
        }

        private GameObject CreateInstanceFromSource(GameObject source)
        {
            if (source == null)
            {
                return null;
            }

            GameObject instance = null;

            if (PrefabUtility.IsPartOfPrefabAsset(source))
            {
                instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            }
            else if (PrefabUtility.IsPartOfPrefabInstance(source))
            {
                GameObject prefabRoot = PrefabUtility.GetCorrespondingObjectFromSource(source);
                if (prefabRoot != null)
                {
                    instance = PrefabUtility.InstantiatePrefab(prefabRoot) as GameObject;
                }
            }

            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(source);
            }

            if (instance != null)
            {
                instance.name = source.name;
            }

            return instance;
        }

        private void AlignPaintedInstance(Transform instanceTransform, Vector3 position, Vector3 surfaceNormal)
        {
            if (instanceTransform == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.identity;

            if (painterFocusTarget != null)
            {
                Bounds bounds = GetFocusTargetBounds();
                Vector3 lookAtPoint = bounds.ClosestPoint(position);
                Vector3 direction = lookAtPoint - position;
                direction.y = 0f;

                if (direction.sqrMagnitude > 1e-4f)
                {
                    rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }

            if (rotation == Quaternion.identity)
            {
                rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            }

            Quaternion normalAlignment = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
            instanceTransform.rotation = normalAlignment * rotation;
        }

        private Bounds GetFocusTargetBounds()
        {
            if (painterFocusTarget == null)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Renderer[] renderers = painterFocusTarget.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    combined.Encapsulate(renderers[i].bounds);
                }

                return combined;
            }

            return new Bounds(painterFocusTarget.position, Vector3.one);
        }

        private bool IsTooClose(Vector3 point)
        {
            GameObject root = GetPaintRoot(false);
            if (root == null)
            {
                return false;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>())
            {
                if (child == null || child == root.transform)
                {
                    continue;
                }

                if (child.GetComponent<MeshRenderer>() == null && child.GetComponent<MeshFilter>() == null)
                {
                    continue;
                }

                if (Vector3.Distance(child.position, point) < painterMinDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private Transform GetOrCreateGroupParent(PaintGroup group, int materialIndex)
        {
            GameObject root = GetPaintRoot(true);
            if (root == null)
            {
                return null;
            }

            if (!paintGroupParents.TryGetValue(group, out List<Transform> parents))
            {
                parents = new List<Transform>();
                paintGroupParents[group] = parents;
            }

            while (parents.Count <= materialIndex)
            {
                parents.Add(null);
            }

            string suffix = $"_{materialIndex + 1}_{group.id}";
            string baseName = string.IsNullOrWhiteSpace(group.groupName) ? "Group" : group.groupName.Trim();
            string targetName = $"{baseName}{suffix}";

            Transform parent = parents[materialIndex];
            if (parent == null)
            {
                parent = FindChildBySuffix(root.transform, suffix);
                if (parent == null)
                {
                    GameObject container = new GameObject(targetName);
                    Undo.RegisterCreatedObjectUndo(container, "Create VAT Paint Group Parent");
                    container.transform.SetParent(root.transform, false);
                    parent = container.transform;
                }
            }

            parent.name = targetName;
            parent.SetParent(root.transform, false);
            parents[materialIndex] = parent;

            return parent;
        }

        private static Transform FindChildBySuffix(Transform root, string suffix)
        {
            if (root == null)
            {
                return null;
            }

            foreach (Transform child in root)
            {
                if (child != null && child.name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private bool HasAnyValidPaintGroup()
        {
            foreach (PaintGroup group in paintGroups)
            {
                if (GroupHasValidMesh(group) && GroupHasValidMaterial(group))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool GroupHasValidMesh(PaintGroup group)
        {
            foreach (MeshFilter filter in group.meshFilters)
            {
                if (filter != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool GroupHasValidMaterial(PaintGroup group)
        {
            foreach (Material material in group.vatMaterials)
            {
                if (material != null)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanBake()
        {
            return infoTexGen != null && targetObject != null && !string.IsNullOrEmpty(outputPath);
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event current = Event.current;
            if ((current.type == EventType.DragUpdated || current.type == EventType.DragPerform) && dropArea.Contains(current.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    bool assigned = TryAssignOutputPathFromPaths(DragAndDrop.paths) ||
                                     TryAssignOutputPathFromObjectReferences(DragAndDrop.objectReferences);

                    if (!assigned)
                    {
                        ReportStatus("Dragged folder must be inside the project's Assets directory.", MessageType.Error);
                    }

                    current.Use();
                }
            }
        }

        private bool TryAssignOutputPathFromPaths(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return false;
            }

            foreach (string path in paths)
            {
                if (TryAssignOutputPath(path))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryAssignOutputPathFromObjectReferences(UnityEngine.Object[] objectReferences)
        {
            if (objectReferences == null)
            {
                return false;
            }

            foreach (UnityEngine.Object reference in objectReferences)
            {
                if (reference == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(reference);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                if (TryAssignOutputPath(assetPath))
                {
                    return true;
                }

                string directory = Path.GetDirectoryName(assetPath);
                if (TryAssignOutputPath(directory))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryAssignOutputPath(string rawPath)
        {
            string projectRelativePath = ConvertToProjectRelativePath(rawPath);
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return false;
            }

            if (!IsProjectRelativeFolder(projectRelativePath))
            {
                return false;
            }

            if (outputPath != projectRelativePath)
            {
                outputPath = projectRelativePath;
                Repaint();
            }

            return true;
        }

        private bool IsProjectRelativeFolder(string projectRelativePath)
        {
            if (AssetDatabase.IsValidFolder(projectRelativePath))
            {
                return true;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return false;
            }

            string absolutePath = Path.Combine(projectRoot, projectRelativePath);
            if (string.IsNullOrEmpty(absolutePath))
            {
                return false;
            }

            if (File.Exists(absolutePath))
            {
                return false;
            }

            return true;
        }

        private string ConvertToProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string normalizedPath = path.Replace('\\', '/').Trim();
            if (normalizedPath.Length == 0)
            {
                return string.Empty;
            }

            if (normalizedPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeProjectRelativePath(normalizedPath);
            }

            string dataPath = Application.dataPath.Replace('\\', '/');
            if (normalizedPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = "Assets" + normalizedPath.Substring(dataPath.Length);
                return NormalizeProjectRelativePath(relativePath);
            }

            return string.Empty;
        }

        private static string NormalizeProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string sanitizedPath = path.Replace('\\', '/').Trim();
            if (sanitizedPath.Length == 0)
            {
                return string.Empty;
            }

            string[] segments = sanitizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return string.Empty;
            }

            if (!segments[0].Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            for (int i = 1; i < segments.Length; i++)
            {
                if (segments[i] == "." || segments[i] == "..")
                {
                    return string.Empty;
                }
            }

            if (segments.Length == 1)
            {
                return "Assets";
            }

            return "Assets/" + string.Join("/", segments, 1, segments.Length - 1);
        }

        private void BakeVatPositionTextures()
        {
            if (!ValidateInputs())
            {
                return;
            }

            if (!EnsureOutputDirectory())
            {
                return;
            }

            SkinnedMeshRenderer skin = targetObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null)
            {
                ReportStatus("No SkinnedMeshRenderer found on the target object.", MessageType.Error);
                return;
            }

            if (skin.sharedMesh == null)
            {
                ReportStatus("The SkinnedMeshRenderer on the target object does not have a shared mesh assigned.", MessageType.Error);
                return;
            }

            Animator animator = targetObject.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                ReportStatus("No Animator with a RuntimeAnimatorController found on the target object.", MessageType.Error);
                return;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0)
            {
                ReportStatus("No animation clips found on the target object's Animator.", MessageType.Warning);
                return;
            }

            Mesh mesh = new Mesh();

            try
            {
                ReportStatus("Baking VAT position textures...", MessageType.Info, false);
                int vertexCount = skin.sharedMesh.vertexCount;
                int kernel = infoTexGen.FindKernel(k_KernelName);
                infoTexGen.GetKernelThreadGroupSizes(kernel, out uint threadSizeX, out uint threadSizeY, out _);

                foreach (AnimationClip clip in clips)
                {
                    int frameCount = CalculateFrameCount(clip.length);
                    float deltaTime = frameCount <= 1 || clip.length <= 0f ? 0f : clip.length / frameCount;

                    List<VertInfo> infoList = new List<VertInfo>(frameCount * vertexCount);

                    RenderTexture positionTexture = CreatePositionRenderTexture(targetObject.name, clip.name, vertexCount, frameCount);

                    try
                    {
                        RenderTexture previousActive = RenderTexture.active;
                        RenderTexture.active = positionTexture;
                        GL.Clear(true, true, Color.clear);
                        RenderTexture.active = previousActive;

                        for (int frame = 0; frame < frameCount; frame++)
                        {
                            float sampleTime = frameCount <= 1 ? 0f : Mathf.Min(deltaTime * frame, clip.length);
                            clip.SampleAnimation(targetObject, sampleTime);
                            skin.BakeMesh(mesh);

                            Vector3[] vertices = mesh.vertices;
                            for (int i = 0; i < vertexCount; i++)
                            {
                                infoList.Add(new VertInfo
                                {
                                    position = vertices[i]
                                });
                            }
                        }

                        using (ComputeBuffer buffer = new ComputeBuffer(infoList.Count, Marshal.SizeOf(typeof(VertInfo))))
                        {
                            buffer.SetData(infoList);

                            infoTexGen.SetInt("VertCount", vertexCount);
                            infoTexGen.SetBuffer(kernel, "Info", buffer);
                            infoTexGen.SetTexture(kernel, "OutPosition", positionTexture);

                            int dispatchX = Mathf.Max(1, Mathf.CeilToInt(vertexCount / (float)threadSizeX));
                            int dispatchY = Mathf.Max(1, Mathf.CeilToInt(frameCount / (float)threadSizeY));
                            infoTexGen.Dispatch(kernel, dispatchX, dispatchY, 1);
                        }

                        Texture2D bakedTexture = RenderTextureToTexture2D.Convert(positionTexture);
                        SaveTextureAsset(bakedTexture, positionTexture.name);
                    }
                    finally
                    {
                        RenderTexture.active = null;
                        positionTexture.Release();
                        DestroyImmediate(positionTexture);
                    }
                }
            }
            finally
            {
                DestroyImmediate(mesh);
                animator.Rebind();
                animator.Update(0f);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ReportStatus($"VAT position texture baking completed. Generated assets in '{outputPath}'.", MessageType.Info);
        }

        private bool ValidateInputs()
        {
            if (infoTexGen == null)
            {
                TryAssignDefaultComputeShader();
                if (infoTexGen == null)
                {
                    ReportStatus("Compute Shader is not assigned.", MessageType.Error);
                    return false;
                }
            }

            if (!infoTexGen.HasKernel(k_KernelName))
            {
                ReportStatus($"Compute Shader does not contain a kernel named '{k_KernelName}'.", MessageType.Error);
                return false;
            }

            if (targetObject == null)
            {
                ReportStatus("Target Object is not assigned.", MessageType.Error);
                return false;
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                ReportStatus("Output path cannot be empty.", MessageType.Error);
                return false;
            }

            if (!outputPath.StartsWith("Assets"))
            {
                ReportStatus("Output path must be inside the project's Assets directory.", MessageType.Error);
                return false;
            }

            return true;
        }

        private bool EnsureOutputDirectory()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                ReportStatus("Unable to determine the project root path.", MessageType.Error);
                return false;
            }

            string absolutePath = Path.Combine(projectRoot, outputPath);
            if (string.IsNullOrEmpty(absolutePath))
            {
                ReportStatus("Failed to resolve the output directory.", MessageType.Error);
                return false;
            }

            if (!Directory.Exists(absolutePath))
            {
                Directory.CreateDirectory(absolutePath);
                AssetDatabase.Refresh();
            }

            return true;
        }

        private int CalculateFrameCount(float clipLength)
        {
            if (clipLength <= 0f)
            {
                return 1;
            }

            int rawFrameCount = Mathf.CeilToInt(clipLength / k_DefaultFrameSampleStep);
            rawFrameCount = Mathf.Max(rawFrameCount, 1);
            return Mathf.Max(1, Mathf.NextPowerOfTwo(rawFrameCount));
        }

        private RenderTexture CreatePositionRenderTexture(string objectName, string clipName, int vertexCount, int frameCount)
        {
            int textureWidth = Mathf.Max(1, Mathf.NextPowerOfTwo(Mathf.Max(vertexCount, 1)));
            RenderTexture renderTexture = new RenderTexture(textureWidth, frameCount, 0, RenderTextureFormat.ARGBHalf)
            {
                name = $"{objectName}.{clipName}.posTex",
                enableRandomWrite = true
            };

            renderTexture.Create();
            return renderTexture;
        }

        private void DrawTargetObjectDiagnostics()
        {
            if (targetObject == null)
            {
                return;
            }

            SkinnedMeshRenderer skin = targetObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null)
            {
                EditorGUILayout.HelpBox("The selected object does not contain a SkinnedMeshRenderer. A skinned mesh is required to bake vertex animation textures.", MessageType.Warning);
            }
            else if (skin.sharedMesh == null)
            {
                EditorGUILayout.HelpBox("The detected SkinnedMeshRenderer does not have a shared mesh assigned.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox($"Skinned mesh detected: {skin.sharedMesh.name} ({skin.sharedMesh.vertexCount} vertices).", MessageType.Info);
            }

            Animator animator = targetObject.GetComponent<Animator>();
            if (animator == null)
            {
                EditorGUILayout.HelpBox("The selected object does not contain an Animator component. Add one to sample animations.", MessageType.Warning);
                return;
            }

            if (animator.runtimeAnimatorController == null)
            {
                EditorGUILayout.HelpBox("The Animator does not have a RuntimeAnimatorController assigned.", MessageType.Warning);
                return;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0)
            {
                EditorGUILayout.HelpBox("The Animator controller does not expose any animation clips to bake.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox($"Animator ready: {clips.Length} animation clip(s) detected.", MessageType.Info);
            }
        }

        private void TryAssignDefaultComputeShader()
        {
            if (infoTexGen != null)
            {
                return;
            }

            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_DefaultComputeShaderPath);
            if (shader == null)
            {
                string[] guids = AssetDatabase.FindAssets($"{k_DefaultComputeShaderName} t:ComputeShader");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    if (shader != null)
                    {
                        break;
                    }
                }
            }

            if (shader != null)
            {
                infoTexGen = shader;
            }
        }

        private void ReportStatus(string message, MessageType type, bool logToConsole = true)
        {
            statusMessage = message;
            statusMessageType = type;

            if (logToConsole)
            {
                switch (type)
                {
                    case MessageType.Error:
                        Debug.LogError(message);
                        break;
                    case MessageType.Warning:
                        Debug.LogWarning(message);
                        break;
                    default:
                        Debug.Log(message);
                        break;
                }
            }

            Repaint();
        }

        private void SaveTextureAsset(Texture2D texture, string baseName)
        {
            string directoryPath = outputPath.TrimEnd('/');
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(directoryPath, baseName + ".asset").Replace("\\", "/"));

            AssetDatabase.CreateAsset(texture, assetPath);
        }

        [System.Serializable]
        private struct VertInfo
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector3 tangent;
        }

        private static class RenderTextureToTexture2D
        {
            public static Texture2D Convert(RenderTexture renderTexture)
            {
                Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBAHalf, false);

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;

                return texture;
            }
        }
    }
}
