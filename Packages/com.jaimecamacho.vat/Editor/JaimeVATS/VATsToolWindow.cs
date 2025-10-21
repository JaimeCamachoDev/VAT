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
                                ReportStatus("Dragged folder must be inside the project's Assets directory.", MessageType.Error);
                            }

                            if (!assigned)
                            {
                                ReportStatus("Dragged folder must be inside the project's Assets directory.", MessageType.Error);
                            }
                        }

                        current.Use();
                    }
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
