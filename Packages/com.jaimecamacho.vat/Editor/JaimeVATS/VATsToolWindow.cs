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

        private ComputeShader infoTexGen;
        private GameObject targetObject;
        private string outputPath = "Assets/BakedAnimationTex";
        private Vector2 scrollPosition;

        [MenuItem("Tools/JaimeCamachoDev/VATsTool")]
        [MenuItem("Assets/JaimeCamachoDev/VATsTool")]
        public static void ShowWindow()
        {
            var window = GetWindow<VATsToolWindow>("VATs Tool");
            window.minSize = new Vector2(420f, 320f);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawVatBakerSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawVatBakerSection()
        {
            EditorGUILayout.LabelField("VAT Baker", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Bakes VAT position textures for every animation clip on the target object's Animator.", MessageType.Info);

            infoTexGen = (ComputeShader)EditorGUILayout.ObjectField("Compute Shader", infoTexGen, typeof(ComputeShader), false);
            targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);

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
                        Debug.LogError("The selected folder must be inside the project's Assets directory.");
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

                    foreach (string draggedPath in DragAndDrop.paths)
                    {
                        if (Directory.Exists(draggedPath))
                        {
                            string projectRelativePath = ConvertToProjectRelativePath(draggedPath);
                            if (!string.IsNullOrEmpty(projectRelativePath))
                            {
                                outputPath = projectRelativePath;
                                Repaint();
                            }
                            else
                            {
                                Debug.LogError("Dragged folder must be inside the project's Assets directory.");
                            }

                            break;
                        }
                    }
                }

                current.Use();
            }
        }

        private string ConvertToProjectRelativePath(string absolutePath)
        {
            absolutePath = absolutePath.Replace('\', '/');
            string dataPath = Application.dataPath.Replace('\', '/');

            if (!absolutePath.StartsWith(dataPath))
            {
                return string.Empty;
            }

            string relativePath = "Assets" + absolutePath.Substring(dataPath.Length);
            return relativePath.TrimEnd('/');
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
                Debug.LogError("No SkinnedMeshRenderer found on the target object.");
                return;
            }

            if (skin.sharedMesh == null)
            {
                Debug.LogError("The SkinnedMeshRenderer on the target object does not have a shared mesh assigned.");
                return;
            }

            Animator animator = targetObject.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("No Animator with a RuntimeAnimatorController found on the target object.");
                return;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning("No animation clips found on the target object's Animator.");
                return;
            }

            Mesh mesh = new Mesh();

            try
            {
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

            Debug.Log($"VAT position texture baking completed. Generated assets in '{outputPath}'.");
        }

        private bool ValidateInputs()
        {
            if (infoTexGen == null)
            {
                Debug.LogError("Compute Shader is not assigned.");
                return false;
            }

            if (!infoTexGen.HasKernel(k_KernelName))
            {
                Debug.LogError($"Compute Shader does not contain a kernel named '{k_KernelName}'.");
                return false;
            }

            if (targetObject == null)
            {
                Debug.LogError("Target Object is not assigned.");
                return false;
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                Debug.LogError("Output path cannot be empty.");
                return false;
            }

            if (!outputPath.StartsWith("Assets"))
            {
                Debug.LogError("Output path must be inside the project's Assets directory.");
                return false;
            }

            return true;
        }

        private bool EnsureOutputDirectory()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                Debug.LogError("Unable to determine the project root path.");
                return false;
            }

            string absolutePath = Path.Combine(projectRoot, outputPath);
            if (string.IsNullOrEmpty(absolutePath))
            {
                Debug.LogError("Failed to resolve the output directory.");
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
