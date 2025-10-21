using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "VAT Tools/UV Transformer Tool")]
public class UVTransformTool : ToolBase
{
    public Texture2D referenceTexture;
    public MeshFilter targetMeshFilter;

    private Vector2 uvPosition;
    private Vector2 uvScale = Vector2.one;
    private float uvRotation;

    private Vector2[] originalUVs;
    private bool lockUniformScale = true;

    private bool isDraggingUV = false;
    private Vector2 dragStartMousePos;
    private Vector2 dragStartUVPos;

    public override void OnGUI()
    {
        referenceTexture = (Texture2D)EditorGUILayout.ObjectField("Reference Texture", referenceTexture, typeof(Texture2D), false);
        targetMeshFilter = (MeshFilter)EditorGUILayout.ObjectField("Target Mesh Filter", targetMeshFilter, typeof(MeshFilter), true);

        EditorGUILayout.Space();
        GUILayout.Label("UV Transform", EditorStyles.boldLabel);
        uvPosition = EditorGUILayout.Vector2Field("Position", uvPosition); 
        EditorGUILayout.BeginHorizontal();
        uvScale = EditorGUILayout.Vector2Field("Scale", uvScale);
        lockUniformScale = GUILayout.Toggle(lockUniformScale, "Lock", GUILayout.Width(50));
        if (lockUniformScale)
        {
            if (GUI.changed)
            {
                uvScale.y = uvScale.x;
            }
        }

        EditorGUILayout.EndHorizontal();

        uvRotation = EditorGUILayout.FloatField("Rotation (degrees)", uvRotation);

        EditorGUILayout.Space();

        Rect textureRect = GUILayoutUtility.GetRect(512, 512, GUILayout.ExpandWidth(false));

        GUI.Box(textureRect, GUIContent.none);

        if (referenceTexture)
        {
            GUI.DrawTexture(textureRect, referenceTexture, ScaleMode.ScaleToFit);
        }

        if (targetMeshFilter != null && targetMeshFilter.sharedMesh != null)
        {
            DrawUVPreview(textureRect);
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Apply UV Transform"))
        {
            ApplyUVTransform();
        }

        if (GUILayout.Button("Undo UV Changes"))
        {
            UndoUV();
        }
    }

    private void DrawUVPreview(Rect rect)
    {
        if (targetMeshFilter == null || targetMeshFilter.sharedMesh == null)
            return;

        Mesh mesh = targetMeshFilter.sharedMesh;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;

        if (originalUVs == null || originalUVs.Length != uvs.Length)
        {
            originalUVs = (Vector2[])uvs.Clone();
        }

        // Preparamos transformación de visualización
        Matrix4x4 previewMatrix = Matrix4x4.TRS(uvPosition, Quaternion.Euler(0, 0, uvRotation), uvScale);

        Handles.BeginGUI();
        Handles.color = new Color(0f, 1f, 0f, 0.4f); // Verde translúcido

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector2 uv0 = (Vector2)previewMatrix.MultiplyPoint(originalUVs[triangles[i]]);
            Vector2 uv1 = (Vector2)previewMatrix.MultiplyPoint(originalUVs[triangles[i + 1]]);
            Vector2 uv2 = (Vector2)previewMatrix.MultiplyPoint(originalUVs[triangles[i + 2]]);

            uv0 = UVToScreen(uv0, rect);
            uv1 = UVToScreen(uv1, rect);
            uv2 = UVToScreen(uv2, rect);

            Handles.DrawAAConvexPolygon(uv0, uv1, uv2);
            Handles.DrawAAPolyLine(2, uv0, uv1, uv2, uv0); // Borde
        }

        Handles.EndGUI();

        // Eventos para arrastrar UVs
        Event e = Event.current;
        Vector2 mouseUV = ScreenToUV(e.mousePosition, rect);

        // Comienzo del arrastre
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (IsMouseNearAnyTransformedUV(mouseUV))
            {
                isDraggingUV = true;
                dragStartMousePos = e.mousePosition;
                dragStartUVPos = uvPosition;
                e.Use();
            }
        }
        // Arrastre
        else if (e.type == EventType.MouseDrag && isDraggingUV)
        {
            Vector2 deltaPixels = e.mousePosition - dragStartMousePos;
            Vector2 deltaUV = new Vector2(deltaPixels.x / rect.width, -deltaPixels.y / rect.height); // Y invertido
            uvPosition = dragStartUVPos + deltaUV;
            e.Use();
        }
        // Fin del arrastre
        else if (e.type == EventType.MouseUp && isDraggingUV)
        {
            isDraggingUV = false;
            e.Use();
        }

        // ✅ Zoom con scroll del ratón mientras arrastras
        if (e.type == EventType.ScrollWheel && isDraggingUV)
        {
            float scroll = -e.delta.y; // Scroll hacia arriba = positivo
            float scaleFactor = 1 + (scroll * 0.05f); // sensibilidad

            if (lockUniformScale)
            {
                uvScale *= scaleFactor;
            }
            else
            {
                uvScale.x *= scaleFactor;
                uvScale.y *= scaleFactor;
            }

            uvScale.x = Mathf.Clamp(uvScale.x, 0.01f, 100f);
            uvScale.y = Mathf.Clamp(uvScale.y, 0.01f, 100f);

            e.Use();
        }
    }

    private bool IsMouseNearAnyTransformedUV(Vector2 mouseUV)
    {
        if (originalUVs == null) return false;

        Matrix4x4 previewMatrix = Matrix4x4.TRS(uvPosition, Quaternion.Euler(0, 0, uvRotation), uvScale);

        foreach (var uv in originalUVs)
        {
            Vector2 transformed = (Vector2)previewMatrix.MultiplyPoint(uv);
            if (Vector2.Distance(transformed, mouseUV) < 0.05f) // umbral de cercanía
            {
                return true;
            }
        }

        return false;
    }
    private Vector2 ScreenToUV(Vector2 screenPos, Rect rect)
    {
        float x = Mathf.Clamp01((screenPos.x - rect.x) / rect.width);
        float y = Mathf.Clamp01(1 - ((screenPos.y - rect.y) / rect.height));
        return new Vector2(x, y);
    }





    private Vector2 UVToScreen(Vector2 uv, Rect rect)
    {
        return new Vector2(
            rect.x + uv.x * rect.width,
            rect.y + (1 - uv.y) * rect.height
        );
    }

    private void ApplyUVTransform()
    {
        if (targetMeshFilter == null || targetMeshFilter.sharedMesh == null)
            return;

        var mesh = targetMeshFilter.sharedMesh;
        var uvs = (Vector2[])originalUVs.Clone();

        Matrix4x4 mat = Matrix4x4.TRS(uvPosition, Quaternion.Euler(0, 0, uvRotation), uvScale);

        for (int i = 0; i < uvs.Length; i++)
        {
            Vector3 v = uvs[i];
            v = mat.MultiplyPoint(v);
            uvs[i] = new Vector2(v.x, v.y);
        }

        mesh.uv = uvs;
        EditorUtility.SetDirty(mesh);
        Debug.Log("UV transform applied.");
    }

    private void UndoUV()
    {
        if (targetMeshFilter == null || originalUVs == null)
            return;

        var mesh = targetMeshFilter.sharedMesh;
        mesh.uv = (Vector2[])originalUVs.Clone();
        EditorUtility.SetDirty(mesh);
        Debug.Log("UVs restored to original.");
    }

    private Bounds GetUVBounds(Vector2[] uvs)
    {
        Vector2 min = uvs[0], max = uvs[0];
        foreach (var uv in uvs)
        {
            min = Vector2.Min(min, uv);
            max = Vector2.Max(max, uv);
        }
        return new Bounds((min + max) / 2, max - min);
    }

    private Vector2[] GetRectCorners(Vector2 center, Vector2 size, Matrix4x4 matrix)
    {
        Vector2 half = size / 2;

        Vector3[] corners = new Vector3[4];
        corners[0] = matrix.MultiplyPoint(new Vector2(-half.x, -half.y) + center);
        corners[1] = matrix.MultiplyPoint(new Vector2(half.x, -half.y) + center);
        corners[2] = matrix.MultiplyPoint(new Vector2(half.x, half.y) + center);
        corners[3] = matrix.MultiplyPoint(new Vector2(-half.x, half.y) + center);

        return new Vector2[] {
        (Vector2)corners[0],
        (Vector2)corners[1],
        (Vector2)corners[2],
        (Vector2)corners[3]
    };
    }

}
