using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

[CreateAssetMenu(menuName = "VAT Tools/Mesh Combiner Turbo Tool")]
public class MeshCombinerTurboTool : MeshCombinerTool
{
    public Shader vatMultipleShader;

    public override void OnGUI()
    {
        base.OnGUI();

        EditorGUILayout.Space();
        vatMultipleShader = (Shader)EditorGUILayout.ObjectField("VAT Multiple Shader", vatMultipleShader, typeof(Shader), false);
    }

    protected override void CombineMeshes()
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
        List<Vector4> combinedOffsets = new();
        List<Vector4> combinedRotations = new();

        List<Material> originalMaterials = new();

        int vertexOffset = 0;
        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh == null) continue;

            var mesh = mf.sharedMesh;
            Transform t = mf.transform;

            combineInstances[vertexOffset] = new CombineInstance
            {
                mesh = mesh,
                transform = t.localToWorldMatrix
            };

            Vector3 pos = t.position;
            Quaternion rot = t.rotation;

            for (int j = 0; j < mesh.vertexCount; j++)
            {
                combinedOffsets.Add(new Vector4(pos.x, pos.y, pos.z, 0));
                combinedRotations.Add(new Vector4(rot.x, rot.y, rot.z, rot.w));
            }

            var renderer = mf.GetComponent<MeshRenderer>();
            if (renderer && renderer.sharedMaterial)
            {
                originalMaterials.Add(renderer.sharedMaterial);
            }

            vertexOffset++;
            totalVertexCount += mesh.vertexCount;
        }

        combinedMesh.CombineMeshes(combineInstances, true, true);
        combinedMesh.SetUVs(2, combinedOffsets);
        combinedMesh.SetUVs(3, combinedRotations);

        GameObject combinedObject = new GameObject(parentObject.name + "_TurboCombined");
        combinedObject.AddComponent<MeshFilter>().mesh = combinedMesh;

        // Crear material VAT multiple
        Material vatMat = new Material(vatMultipleShader);

        if (originalMaterials.Count > 0)
        {
            Material refMat = originalMaterials[0];
            Shader refShader = refMat.shader;
            int count = ShaderUtil.GetPropertyCount(refShader);

            for (int i = 0; i < count; i++)
            {
                var propName = ShaderUtil.GetPropertyName(refShader, i);
                var propType = ShaderUtil.GetPropertyType(refShader, i);

                switch (propType)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        vatMat.SetColor(propName, refMat.GetColor(propName));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        vatMat.SetVector(propName, refMat.GetVector(propName));
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        vatMat.SetFloat(propName, refMat.GetFloat(propName));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        vatMat.SetTexture(propName, refMat.GetTexture(propName));
                        break;
                }
            }
        }

        vatMat.SetInt("_NumberOfMeshes", meshFilters.Length);
        vatMat.SetInt("_TotalVertex", totalVertexCount);

        var rendererFinal = combinedObject.AddComponent<MeshRenderer>();
        rendererFinal.sharedMaterial = vatMat;

        if (!AssetDatabase.IsValidFolder(outputPathMesh))
        {
            Directory.CreateDirectory(outputPathMesh);
        }

        string meshAssetPath = Path.Combine(outputPathMesh, parentObject.name + "_TurboCombinedMesh.asset");
        AssetDatabase.CreateAsset(combinedMesh, meshAssetPath);

        string matAssetPath = Path.Combine(outputPathMesh, parentObject.name + "_TurboVAT_Material.mat");
        AssetDatabase.CreateAsset(vatMat, matAssetPath);

        AssetDatabase.SaveAssets();

        string prefabPath = Path.Combine(outputPathMesh, combinedObject.name + ".prefab");
        PrefabUtility.SaveAsPrefabAsset(combinedObject, prefabPath);
        AssetDatabase.SaveAssets();

        GameObject prefabInstance = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)) as GameObject;
        GameObject.DestroyImmediate(combinedObject);

        Debug.Log("Turbo mesh combined and saved with VAT material.");
    }
}
