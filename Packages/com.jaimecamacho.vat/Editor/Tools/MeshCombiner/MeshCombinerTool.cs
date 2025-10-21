using UnityEngine;
using UnityEditor;
using System.IO;

[CreateAssetMenu(menuName = "VAT Tools/Mesh Combiner Tool")]
public class MeshCombinerTool : ToolBase
{
    public GameObject parentObject;
    public string outputPathMesh = "Assets/CombinedMeshes";

    public override void OnGUI()
    {
        parentObject = (GameObject)EditorGUILayout.ObjectField("Parent Object", parentObject, typeof(GameObject), true);

        if (GUILayout.Button("Combine and Save Mesh"))
        {
            if (parentObject == null)
            {
                Debug.LogError("Parent object is null.");
                return;
            }

            CombineMeshes();
        }
    }

    protected virtual void CombineMeshes()
    {
        MeshFilter[] meshFilters = parentObject.GetComponentsInChildren<MeshFilter>();
        if (meshFilters.Length == 0)
        {
            Debug.LogError("No MeshFilters found under the root object.");
            return;
        }

        Mesh combinedMesh = new Mesh();
        CombineInstance[] combineInstances = new CombineInstance[meshFilters.Length];

        int totalVertexCount = 0;
        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh != null)
                totalVertexCount += mf.sharedMesh.vertexCount;
        }

        Vector4[] combinedOffsets = new Vector4[totalVertexCount];
        Vector4[] combinedRotations = new Vector4[totalVertexCount];
        int vertexOffset = 0;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            var mesh = meshFilters[i].sharedMesh;
            if (mesh == null) continue;

            Transform t = meshFilters[i].transform;
            Vector3 positionOffset = t.position;
            Quaternion rotation = t.rotation;

            combineInstances[i].mesh = mesh;
            combineInstances[i].transform = t.localToWorldMatrix;

            for (int j = 0; j < mesh.vertexCount; j++)
            {
                combinedOffsets[vertexOffset + j] = new Vector4(positionOffset.x, positionOffset.y, positionOffset.z, 0);
                combinedRotations[vertexOffset + j] = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
            }

            vertexOffset += mesh.vertexCount;
        }

        combinedMesh.CombineMeshes(combineInstances, true, true);
        combinedMesh.SetUVs(2, combinedOffsets);
        combinedMesh.SetUVs(3, combinedRotations);

        GameObject combinedObject = new GameObject(parentObject.name + "_Combined");
        combinedObject.AddComponent<MeshFilter>().mesh = combinedMesh;
        combinedObject.AddComponent<MeshRenderer>();

        if (!AssetDatabase.IsValidFolder(outputPathMesh))
        {
            Directory.CreateDirectory(outputPathMesh);
        }

        string meshAssetPath = Path.Combine(outputPathMesh, parentObject.name + "_CombinedMesh.asset");
        AssetDatabase.CreateAsset(combinedMesh, meshAssetPath);
        AssetDatabase.SaveAssets();

        string prefabPath = Path.Combine(outputPathMesh, combinedObject.name + ".prefab");
        PrefabUtility.SaveAsPrefabAsset(combinedObject, prefabPath);
        AssetDatabase.SaveAssets();

        GameObject prefabInstance = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)) as GameObject;
        GameObject.DestroyImmediate(combinedObject);

        Debug.Log("Mesh combined and saved successfully.");
    }
}
