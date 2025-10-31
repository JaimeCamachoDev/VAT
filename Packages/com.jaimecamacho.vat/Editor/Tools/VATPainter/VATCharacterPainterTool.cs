using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

[CreateAssetMenu(menuName = "VAT Tools/Character Painter Tool")]
public class VATCharacterPainterTool : ToolBase
{
    [System.Serializable]
    public class PaintGroup
    {
        public string groupName = "New Group";
        public List<MeshFilter> meshFilters = new();
        public List<Material> vatMaterials = new();
    }

    public List<PaintGroup> paintGroups = new();

    private const string ConvertedAssetsFolder = "Assets/VATConvertedMeshes";

    public Transform focusTarget;
    public GameObject paintSurface;
    private MeshCollider paintCollider;

    private bool paintingMode = false;
    private GameObject parentPaintRoot;
    private Dictionary<string, List<Transform>> groupMaterialParents = new();

    private static readonly Dictionary<string, MeshFilter> convertedMeshFilters = new();

    public float brushRadius = 2f;
    public int brushDensity = 5;
    public float minDistance = 0.5f;

    private void EnsureCollider()
    {
        if (paintSurface != null && paintCollider == null)
        {
            paintCollider = paintSurface.GetComponent<MeshCollider>();
            if (paintCollider == null)
            {
                Debug.LogWarning("Paint Surface needs a MeshCollider to work.");
            }
        }
    }

    public override void OnGUI()
    {
        if (GUILayout.Button("Add Paint Group"))
        {
            paintGroups.Add(new PaintGroup());
        }

        EditorGUILayout.Space();

        for (int i = 0; i < paintGroups.Count; i++)
        {
            var group = paintGroups[i];
            EditorGUILayout.BeginVertical("box");

            group.groupName = EditorGUILayout.TextField("Group Name", group.groupName);

            EditorGUILayout.LabelField("Mesh Filters:", EditorStyles.boldLabel);
            for (int j = 0; j < group.meshFilters.Count; j++)
            {
                bool invalidSelection = false;
                EditorGUILayout.BeginHorizontal();

                var currentValue = group.meshFilters[j] != null ? (UnityEngine.Object)group.meshFilters[j] : null;
                EditorGUI.BeginChangeCheck();
                UnityEngine.Object selectedObject = EditorGUILayout.ObjectField(currentValue, typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    if (selectedObject == null)
                    {
                        group.meshFilters[j] = null;
                    }
                    else
                    {
                        MeshFilter resolvedFilter = ResolveMeshFilter(selectedObject);
                        if (resolvedFilter != null)
                        {
                            group.meshFilters[j] = resolvedFilter;
                        }
                        else
                        {
                            invalidSelection = true;
                        }
                    }
                }

                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    group.meshFilters.RemoveAt(j);
                    j--;
                }
                EditorGUILayout.EndHorizontal();

                if (invalidSelection)
                {
                    EditorGUILayout.HelpBox("The selected object does not contain a MeshFilter component.", MessageType.Warning);
                }
            }
            if (GUILayout.Button("Add Mesh Filter"))
            {
                group.meshFilters.Add(null);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("VAT Materials:", EditorStyles.boldLabel);
            for (int k = 0; k < group.vatMaterials.Count; k++)
            {
                EditorGUILayout.BeginHorizontal();
                group.vatMaterials[k] = (Material)EditorGUILayout.ObjectField(group.vatMaterials[k], typeof(Material), false);
                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    group.vatMaterials.RemoveAt(k);
                    k--;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add VAT Material"))
            {
                group.vatMaterials.Add(null);
            }

            if (GUILayout.Button("Remove Paint Group"))
            {
                paintGroups.RemoveAt(i);
                i--;
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();
        focusTarget = (Transform)EditorGUILayout.ObjectField("Focus Target (LookAt)", focusTarget, typeof(Transform), true);
        paintSurface = (GameObject)EditorGUILayout.ObjectField("Paint Surface (MeshCollider)", paintSurface, typeof(GameObject), true);

        brushRadius = EditorGUILayout.FloatField("Brush Radius", brushRadius);
        brushDensity = EditorGUILayout.IntField("Brush Density", brushDensity);
        minDistance = EditorGUILayout.FloatField("Min Distance Between Instances", minDistance);

        EditorGUILayout.Space();
        paintingMode = GUILayout.Toggle(paintingMode, "Painting Mode (Click in SceneView to paint)", "Button");

        SceneView.duringSceneGui -= OnSceneGUI;
        if (paintingMode)
        {
            PreparePaintHierarchy();
            SceneView.duringSceneGui += OnSceneGUI;
        }
    }

    private static MeshFilter ResolveMeshFilter(UnityEngine.Object source)
    {
        if (source == null)
        {
            return null;
        }

        if (source is MeshFilter meshFilter)
        {
            return meshFilter;
        }

        if (source is SkinnedMeshRenderer skinnedMeshRenderer)
        {
            return ConvertSkinnedMeshRenderer(skinnedMeshRenderer);
        }

        if (source is Mesh meshAsset)
        {
            return ConvertMeshAsset(meshAsset);
        }

        if (source is GameObject gameObject)
        {
            MeshFilter foundFilter = gameObject.GetComponent<MeshFilter>() ?? gameObject.GetComponentInChildren<MeshFilter>();
            if (foundFilter != null)
            {
                return foundFilter;
            }

            SkinnedMeshRenderer foundSkinned = gameObject.GetComponent<SkinnedMeshRenderer>() ?? gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (foundSkinned != null)
            {
                return ConvertSkinnedMeshRenderer(foundSkinned);
            }
        }

        if (source is Component component)
        {
            MeshFilter foundFilter = component.GetComponent<MeshFilter>() ?? component.GetComponentInChildren<MeshFilter>();
            if (foundFilter != null)
            {
                return foundFilter;
            }

            SkinnedMeshRenderer foundSkinned = component.GetComponent<SkinnedMeshRenderer>() ?? component.GetComponentInChildren<SkinnedMeshRenderer>();
            if (foundSkinned != null)
            {
                return ConvertSkinnedMeshRenderer(foundSkinned);
            }
        }

        return null;
    }

    private static MeshFilter ConvertMeshAsset(Mesh mesh)
    {
        if (mesh == null)
        {
            return null;
        }

        string key = BuildSourceKey(mesh);
        if (convertedMeshFilters.TryGetValue(key, out MeshFilter cachedMeshFilter) && cachedMeshFilter != null)
        {
            return cachedMeshFilter;
        }

        EnsureConvertedFolder();

        string baseName = !string.IsNullOrEmpty(mesh.name) ? mesh.name : "ConvertedMesh";
        string sanitizedName = SanitizeFileName(baseName);

        string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{ConvertedAssetsFolder}/{sanitizedName}_FromMesh.prefab");

        GameObject temp = new GameObject($"{baseName}_SourceMesh");
        MeshFilter tempFilter = temp.AddComponent<MeshFilter>();
        tempFilter.sharedMesh = mesh;

        MeshRenderer tempRenderer = temp.AddComponent<MeshRenderer>();
        Material defaultMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        if (defaultMaterial != null)
        {
            tempRenderer.sharedMaterial = defaultMaterial;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
        Object.DestroyImmediate(temp);

        MeshFilter prefabMeshFilter = prefab != null ? prefab.GetComponent<MeshFilter>() : null;
        if (prefabMeshFilter != null)
        {
            convertedMeshFilters[key] = prefabMeshFilter;
        }

        AssetDatabase.SaveAssets();

        return prefabMeshFilter;
    }

    private static MeshFilter ConvertSkinnedMeshRenderer(SkinnedMeshRenderer skinnedMeshRenderer)
    {
        if (skinnedMeshRenderer == null || skinnedMeshRenderer.sharedMesh == null)
        {
            return null;
        }

        UnityEngine.Object keySource = skinnedMeshRenderer.sharedMesh != null ? skinnedMeshRenderer.sharedMesh : skinnedMeshRenderer;
        string key = BuildSourceKey(keySource);
        if (convertedMeshFilters.TryGetValue(key, out MeshFilter cachedMeshFilter) && cachedMeshFilter != null)
        {
            return cachedMeshFilter;
        }

        EnsureConvertedFolder();

        Mesh bakedMesh = Object.Instantiate(skinnedMeshRenderer.sharedMesh);
        string meshName = !string.IsNullOrEmpty(skinnedMeshRenderer.sharedMesh.name) ? skinnedMeshRenderer.sharedMesh.name : skinnedMeshRenderer.name;
        if (string.IsNullOrEmpty(meshName))
        {
            meshName = "ConvertedSkinnedMesh";
        }
        bakedMesh.name = meshName + "_Converted";

        string sanitizedName = SanitizeFileName(meshName);
        string meshAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{ConvertedAssetsFolder}/{sanitizedName}_Mesh.asset");
        AssetDatabase.CreateAsset(bakedMesh, meshAssetPath);

        GameObject temp = new GameObject($"{meshName}_Converted");
        MeshFilter tempFilter = temp.AddComponent<MeshFilter>();
        tempFilter.sharedMesh = bakedMesh;

        MeshRenderer tempRenderer = temp.AddComponent<MeshRenderer>();
        tempRenderer.sharedMaterials = skinnedMeshRenderer.sharedMaterials;

        string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{ConvertedAssetsFolder}/{sanitizedName}_Prefab.prefab");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
        Object.DestroyImmediate(temp);

        MeshFilter prefabMeshFilter = prefab != null ? prefab.GetComponent<MeshFilter>() : null;
        if (prefabMeshFilter != null)
        {
            convertedMeshFilters[key] = prefabMeshFilter;
        }

        AssetDatabase.SaveAssets();

        return prefabMeshFilter;
    }

    private static void EnsureConvertedFolder()
    {
        if (!AssetDatabase.IsValidFolder(ConvertedAssetsFolder))
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(ConvertedAssetsFolder));
        }
    }

    private static string BuildSourceKey(UnityEngine.Object source)
    {
        if (source == null)
        {
            return string.Empty;
        }

        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
        {
            return $"{guid}_{localId}";
        }

        return source.GetInstanceID().ToString();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "Converted";
        }

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        return name;
    }

    private void PreparePaintHierarchy()
    {
        parentPaintRoot = GameObject.Find("PadrePaint");
        if (!parentPaintRoot)
        {
            parentPaintRoot = new GameObject("PadrePaint");
        }

        groupMaterialParents.Clear();

        foreach (var group in paintGroups)
        {
            var subParents = new List<Transform>();
            for (int i = 0; i < group.vatMaterials.Count; i++)
            {
                string subName = group.groupName + "_" + (i + 1);
                var existing = parentPaintRoot.transform.Find(subName);
                GameObject sub = existing != null ? existing.gameObject : new GameObject(subName);
                sub.transform.parent = parentPaintRoot.transform;
                subParents.Add(sub.transform);
            }
            groupMaterialParents[group.groupName] = subParents;
        }
    }

    // ... (rest of the code remains unchanged)

    private void OnSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

        if (paintingMode && e.type == EventType.Repaint && SceneView.lastActiveSceneView != null)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                Handles.color = new Color(0f, 0.5f, 1f, 0.25f);
                Handles.DrawSolidDisc(hit.point, hit.normal, brushRadius);
                Handles.color = Color.cyan;
                Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
            }
        }

        if (!paintingMode || e == null || e.type != EventType.MouseDown || e.button != 0)
            return;

        EnsureCollider();
        if (paintCollider == null || focusTarget == null || paintGroups.Count == 0)
            return;

        Ray clickRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(clickRay, out RaycastHit clickHit, 1000f))
        {
            for (int i = 0; i < brushDensity; i++)
            {
                Vector2 offset = Random.insideUnitCircle * brushRadius;
                Vector3 point = clickHit.point + new Vector3(offset.x, 0, offset.y);

                if (Physics.Raycast(new Ray(point + Vector3.up * 10, Vector3.down), out RaycastHit subHit, 100f))
                {
                    if (!IsTooClose(subHit.point))
                    {
                        PaintAtPosition(subHit.point);
                    }
                }
            }
            e.Use();
        }
    }


    private bool IsTooClose(Vector3 point)
    {
        foreach (Transform t in parentPaintRoot.transform)
        {
            if (Vector3.Distance(t.position, point) < minDistance)
                return true;
        }
        return false;
    }

    private void PaintAtPosition(Vector3 position)
    {
        var group = paintGroups[Random.Range(0, paintGroups.Count)];
        if (group.meshFilters.Count == 0 || group.vatMaterials.Count == 0) return;

        int matIndex = Random.Range(0, group.vatMaterials.Count);
        var meshFilter = group.meshFilters[Random.Range(0, group.meshFilters.Count)];
        var material = group.vatMaterials[matIndex];

        GameObject instance = Object.Instantiate(meshFilter.gameObject);
        instance.transform.position = position;
        Bounds bounds = GetTargetBounds();
        Vector3 closestPoint = bounds.ClosestPoint(position);

        Vector3 offset = Vector3.Cross(Vector3.up, (closestPoint - position).normalized);
        float maxOffset = Vector3.Distance(position, closestPoint) * 0.1f;
        closestPoint += offset * Random.Range(-maxOffset, maxOffset);

        Vector3 direction = closestPoint - position;
        direction.y = 0f;
        if (direction != Vector3.zero)
        {
            instance.transform.rotation = Quaternion.LookRotation(direction);
        }

        var renderer = instance.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        if (groupMaterialParents.TryGetValue(group.groupName, out var subParents) && matIndex < subParents.Count)
        {
            instance.transform.parent = subParents[matIndex];
        }
        else
        {
            instance.transform.parent = parentPaintRoot.transform;
        }
        instance.transform.eulerAngles = new Vector3(-90, instance.transform.eulerAngles.y, instance.transform.eulerAngles.z);

        //Undo.RegisterCreatedObjectUndo(instance, "Paint Character");
    }

    private Bounds GetTargetBounds()
    {
        Renderer renderer = focusTarget.GetComponent<Renderer>();
        if (renderer != null)
        {
            return renderer.bounds;
        }

        var childRenderers = focusTarget.GetComponentsInChildren<Renderer>();
        if (childRenderers.Length > 0)
        {
            Bounds combined = childRenderers[0].bounds;
            for (int i = 1; i < childRenderers.Length; i++)
            {
                combined.Encapsulate(childRenderers[i].bounds);
            }
            return combined;
        }

        return new Bounds(focusTarget.position, Vector3.one);
    }
}
