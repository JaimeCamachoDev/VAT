using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "VAT Tools/UV Adjuster Tool")]
public class UVAdjusterTool : ToolBase
{
    public int rows = 1;
    public int columns = 1;
    public int gridX = 0;
    public int gridY = 0;

    public List<MeshFilter> selectedMeshFilters = new List<MeshFilter>();

    // Guardamos un mapa de UVs originales para deshacer
    private Dictionary<Mesh, Vector2[]> originalUVs = new Dictionary<Mesh, Vector2[]>();

    public override void OnGUI()
    {
        rows = EditorGUILayout.IntField("Rows", rows);
        columns = EditorGUILayout.IntField("Columns", columns);
        gridX = EditorGUILayout.IntField("Grid X", gridX);
        gridY = EditorGUILayout.IntField("Grid Y", gridY);

        EditorGUILayout.Space();
        GUILayout.Label("Selected Mesh Filters", EditorStyles.label);

        if (GUILayout.Button("Add Mesh Filter"))
        {
            selectedMeshFilters.Add(null);
        }

        for (int i = 0; i < selectedMeshFilters.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            selectedMeshFilters[i] = (MeshFilter)EditorGUILayout.ObjectField(selectedMeshFilters[i], typeof(MeshFilter), true);

            if (GUILayout.Button("Remove", GUILayout.Width(70)))
            {
                selectedMeshFilters.RemoveAt(i);
                i--; // Evitar salto al siguiente elemento
                continue;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Adjust UVs"))
        {
            AdjustUVs();
        }

        if (GUILayout.Button("Undo UV Adjustments"))
        {
            UndoUVAdjustments();
        }
    }

    private void AdjustUVs()
    {
        if (selectedMeshFilters.Count == 0)
        {
            Debug.LogError("No mesh filters selected!");
            return;
        }

        if (rows <= 0 || columns <= 0)
        {
            Debug.LogError("Rows and columns must be greater than 0.");
            return;
        }

        foreach (var meshFilter in selectedMeshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            Mesh mesh = meshFilter.sharedMesh;

            if (!originalUVs.ContainsKey(mesh))
            {
                originalUVs[mesh] = mesh.uv.Clone() as Vector2[];
            }

            Vector2[] uvs = mesh.uv;
            float uvWidth = 1.0f / columns;
            float uvHeight = 1.0f / rows;
            Vector2 offset = new Vector2(gridX * uvWidth, gridY * uvHeight);

            for (int i = 0; i < uvs.Length; i++)
            {
                uvs[i] = new Vector2(
                    uvs[i].x * uvWidth + offset.x,
                    uvs[i].y * uvHeight + offset.y
                );
            }

            mesh.uv = uvs;
            EditorUtility.SetDirty(mesh);
        }

        Debug.Log("UVs adjusted successfully for all selected meshes.");
    }

    private void UndoUVAdjustments()
    {
        foreach (var meshFilter in selectedMeshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            Mesh mesh = meshFilter.sharedMesh;

            if (originalUVs.TryGetValue(mesh, out Vector2[] original))
            {
                mesh.uv = original;
                EditorUtility.SetDirty(mesh);
            }
        }

        Debug.Log("UV adjustments undone for all selected meshes.");
    }
}
