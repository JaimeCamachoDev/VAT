using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

[CreateAssetMenu(menuName = "VAT Tools/Animation Clip Texture Baker Tool")]
public class AnimationClipTextureBakerTool : ToolBase
{
    public ComputeShader infoTexGen;
    public GameObject targetObject;
    public string outputPath = "Assets/BakedAnimationTex";

    public override void OnGUI()
    {
        infoTexGen = (ComputeShader)EditorGUILayout.ObjectField("Compute Shader", infoTexGen, typeof(ComputeShader), false);
        if (infoTexGen == null)
        {
            EditorGUILayout.HelpBox("Compute Shader is not assigned!", MessageType.Warning);
        }

        targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Output Path:");
        outputPath = EditorGUILayout.TextField(outputPath);
        EditorGUILayout.EndHorizontal();

        HandleDragAndDrop();

        if (GUILayout.Button("Select Folder"))
        {
            string selectedFolder = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                outputPath = selectedFolder.Replace(Application.dataPath, "Assets");
                RepaintWindow();
            }
        }

        if (GUILayout.Button("Bake Textures"))
        {
            if (infoTexGen == null)
            {
                Debug.LogError("Compute Shader is not assigned!");
                return;
            }

            if (targetObject == null)
            {
                Debug.LogError("Target Object is not assigned!");
                return;
            }

            BakeTextures();
        }
    }

    private void HandleDragAndDrop()
    {
        Event evt = Event.current;
        Rect dropArea = GUILayoutUtility.GetLastRect();

        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            if (dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    foreach (var draggedObject in DragAndDrop.paths)
                    {
                        if (Directory.Exists(draggedObject))
                        {
                            outputPath = draggedObject.Replace(Application.dataPath, "Assets");
                            RepaintWindow();
                        }
                    }
                }
            }
        }
    }

    private void RepaintWindow()
    {
        if (EditorWindow.focusedWindow != null)
            EditorWindow.focusedWindow.Repaint();
    }

    private void BakeTextures()
    {
        var skin = targetObject.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null)
        {
            Debug.LogError("No SkinnedMeshRenderer found on the target object.");
            return;
        }

        var vCount = skin.sharedMesh.vertexCount;
        var texWidth = Mathf.NextPowerOfTwo(vCount);
        var mesh = new Mesh();

        var animator = targetObject.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogError("No Animator or RuntimeAnimatorController found on the target object.");
            return;
        }

        var clips = animator.runtimeAnimatorController.animationClips;

        foreach (var clip in clips)
        {
            var frames = Mathf.NextPowerOfTwo((int)(clip.length / 0.05f));
            var dt = clip.length / frames;
            var infoList = new List<VertInfo>();

            var pRt = new RenderTexture(texWidth, frames, 0, RenderTextureFormat.ARGBHalf)
            {
                name = $"{targetObject.name}.{clip.name}.posTex",
                enableRandomWrite = true
            };
            pRt.Create();
            RenderTexture.active = pRt;
            GL.Clear(true, true, Color.clear);

            for (var i = 0; i < frames; i++)
            {
                clip.SampleAnimation(targetObject, dt * i);
                skin.BakeMesh(mesh);

                var vertexArray = mesh.vertices;
                infoList.AddRange(Enumerable.Range(0, vCount)
                    .Select(idx => new VertInfo()
                    {
                        position = vertexArray[idx]
                    })
                );
            }

            var buffer = new ComputeBuffer(infoList.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(VertInfo)));
            buffer.SetData(infoList.ToArray());

            int kernel = infoTexGen.FindKernel("CSMain");
            infoTexGen.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out uint z);

            infoTexGen.SetInt("VertCount", vCount);
            infoTexGen.SetBuffer(kernel, "Info", buffer);
            infoTexGen.SetTexture(kernel, "OutPosition", pRt);
            infoTexGen.Dispatch(kernel, vCount / (int)x + 1, frames / (int)y + 1, 1);

            buffer.Release();

#if UNITY_EDITOR
            var posTex = RenderTextureToTexture2D.Convert(pRt);
            Graphics.CopyTexture(pRt, posTex);

            var assetPath = Path.Combine(outputPath, pRt.name + ".asset").Replace("\\", "/");
            AssetDatabase.CreateAsset(posTex, assetPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
        }

        Debug.Log("Baking complete.");
    }

    public struct VertInfo
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector3 tangent;
    }

    public static class RenderTextureToTexture2D
    {
        public static Texture2D Convert(RenderTexture rt)
        {
            Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            return texture;
        }
    }
}
