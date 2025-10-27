using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
        private GameObject combinerParentObject;
        private string combinerOutputPath = "Assets/CombinedMeshes";
        private Shader combinerVatMultipleShader;
        private Vector2 scrollPosition;
        private string statusMessage = string.Empty;
        private MessageType statusMessageType = MessageType.Info;
        private ToolTab statusMessageTab = ToolTab.VatBaker;

        private static GUIStyle sectionTitleStyle;
        private static GUIStyle sectionSubtitleStyle;
        private static GUIStyle messageCardStyle;
        private static GUIStyle messageTitleStyle;
        private static GUIStyle messageBodyStyle;
        private static readonly Dictionary<MessageType, GUIContent> messageIcons = new Dictionary<MessageType, GUIContent>();

        private Texture2D uvVisualReferenceTexture;
        private Texture2D uvVisualGeneratedAtlas;
        private MeshFilter uvVisualTargetMeshFilter;
        private SkinnedMeshRenderer uvVisualTargetSkinnedMeshRenderer;
        private UnityEngine.Object uvVisualTargetSelectionOverride;
        private Mesh uvVisualLastMesh;
        private Vector2 uvVisualPosition;
        private Vector2 uvVisualScale = Vector2.one;
        private float uvVisualRotation;
        private Vector2[] uvVisualOriginalUvs;
        private Vector2[] uvVisualInitialUvs;
        private bool uvVisualLockUniformScale = true;
        private bool uvVisualIsDragging;
        private Vector2 uvVisualDragStartMousePos;
        private Vector2 uvVisualDragStartUvPos;

        private bool uvVisualShowAtlasBuilder = true;
        private int uvVisualAtlasImageCount = 1;
        private int uvVisualAtlasCellResolution = 256;
        private string uvVisualAtlasExportFolder = "Assets/VATAtlases";
        private readonly List<Texture2D> uvVisualAtlasSourceTextures = new List<Texture2D>();
        private readonly List<UvVisualTargetEntry> uvVisualTargets = new List<UvVisualTargetEntry>();
        private int uvVisualActiveTargetIndex = -1;
        private UnityEngine.Object uvVisualNewTargetCandidate;

        private static readonly string[] k_UvVisualAtlasResolutionLabels =
        {
            "64 x 64",
            "128 x 128",
            "256 x 256",
            "512 x 512",
            "1024 x 1024"
        };

        private static readonly int[] k_UvVisualAtlasResolutionSizes =
        {
            64,
            128,
            256,
            512,
            1024
        };

        private const string k_PaintRootName = "VATPaintRoot";
        private static readonly Color k_BrushFillColor = new Color(0f, 0.5f, 1f, 0.25f);
        private static readonly Color k_BrushOutlineColor = Color.cyan;
        private static readonly Color[] k_UvVisualWireColors =
        {
            new Color(0.0f, 0.78f, 1.0f),
            new Color(1.0f, 0.55f, 0.35f),
            new Color(0.4f, 0.9f, 0.35f),
            new Color(0.9f, 0.3f, 0.8f),
            new Color(1.0f, 0.85f, 0.3f),
            new Color(0.55f, 0.6f, 1.0f)
        };

        private enum ToolTab
        {
            VatBaker,
            VatUvVisual,
            VatPainter,
            VatCombiner
        }

        private static readonly GUIContent[] k_ToolTabLabels =
        {
            new GUIContent("VAT Baker"),
            new GUIContent("VAT UV Visual"),
            new GUIContent("VAT Painter"),
            new GUIContent("VAT Combiner")
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

        [Serializable]
        private class UvVisualTargetEntry
        {
            public UnityEngine.Object selectionOverride;
            public MeshFilter meshFilter;
            public SkinnedMeshRenderer skinnedMeshRenderer;

            public Mesh SharedMesh
            {
                get
                {
                    if (skinnedMeshRenderer != null)
                    {
                        return skinnedMeshRenderer.sharedMesh;
                    }

                    return meshFilter != null ? meshFilter.sharedMesh : null;
                }
            }

            public bool HasValidRenderer => meshFilter != null || skinnedMeshRenderer != null;
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
            ShowWindow(ToolTab.VatBaker);
        }

        //[MenuItem("Tools/JaimeCamachoDev/VATs Tool/VAT Baker")]
        //[MenuItem("Assets/JaimeCamachoDev/VATs Tool/VAT Baker")]
        //public static void ShowVatBaker()
        //{
        //    ShowWindow(ToolTab.VatBaker);
        //}

        //[MenuItem("Tools/JaimeCamachoDev/VATs Tool/VAT Painter")]
        //[MenuItem("Assets/JaimeCamachoDev/VATs Tool/VAT Painter")]
        //public static void ShowVatPainter()
        //{
        //    ShowWindow(ToolTab.VatPainter);
        //}

        //[MenuItem("Tools/JaimeCamachoDev/VATs Tool/VAT UV Visual")]
        //[MenuItem("Assets/JaimeCamachoDev/VATs Tool/VAT UV Visual")]
        //public static void ShowVatUvVisual()
        //{
        //    ShowWindow(ToolTab.VatUvVisual);
        //}

        private static void ShowWindow(ToolTab initialTab)
        {
            var window = GetWindow<VATsToolWindow>("VATs Tool");
            window.minSize = new Vector2(420f, 320f);
            window.SetActiveTab(initialTab, true);
            window.Focus();
        }

        private void OnEnable()
        {
            TryAssignDefaultComputeShader();
            OnUvVisualExportFolderChanged();
        }

        private void OnFocus()
        {
            TryAssignDefaultComputeShader();
        }

        private void SetActiveTab(ToolTab tab, bool resetScroll = false)
        {
            activeTabIndex = (int)tab;

            if (resetScroll)
                scrollPosition = Vector2.zero;

            Repaint();
            Focus();
        }


        private void OnDisable()
        {
            if (painterPaintingMode)
            {
                TogglePaintingMode(false);
            }

            if (uvVisualGeneratedAtlas != null)
            {
                if (uvVisualReferenceTexture == uvVisualGeneratedAtlas)
                {
                    uvVisualReferenceTexture = null;
                }

                DestroyImmediate(uvVisualGeneratedAtlas);
                uvVisualGeneratedAtlas = null;
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
                case ToolTab.VatUvVisual:
                    DrawVatUvVisualSection();
                    break;
                case ToolTab.VatPainter:
                    DrawVatPainterSection();
                    break;
                case ToolTab.VatCombiner:
                    DrawVatCombinerSection();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSectionHeader(string title, string subtitle)
        {
            EnsureUiStyles();

            float height = string.IsNullOrEmpty(subtitle) ? 28f : 42f;
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(height), GUILayout.ExpandWidth(true));

            Color background = EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f, 0.95f) : new Color(0.93f, 0.93f, 0.93f, 0.95f);
            Color accent = new Color(0.25f, 0.6f, 1f, 0.9f);

            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);

            Rect contentRect = new Rect(rect.x + 10f, rect.y + 6f, rect.width - 14f, rect.height - 12f);
            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 18f), title, sectionTitleStyle);

            if (!string.IsNullOrEmpty(subtitle))
            {
                GUI.Label(new Rect(contentRect.x, contentRect.y + 18f, contentRect.width, 16f), subtitle, sectionSubtitleStyle);
            }

            GUILayout.Space(6f);
        }

        private static void EnsureUiStyles()
        {
            if (sectionTitleStyle == null)
            {
                sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 15,
                    richText = true
                };
            }

            if (sectionSubtitleStyle == null)
            {
                sectionSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true,
                    fontSize = 11,
                    richText = true
                };
            }

            if (messageCardStyle == null)
            {
                messageCardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    richText = true,
                    wordWrap = true,
                    padding = new RectOffset(12, 12, 10, 10)
                };
            }

            if (messageTitleStyle == null)
            {
                messageTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    richText = true,
                    wordWrap = true
                };
            }

            if (messageBodyStyle == null)
            {
                messageBodyStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true,
                    richText = true
                };
            }
        }

        private void DrawMessageCard(string message, MessageType type)
        {
            DrawMessageCard(GetDefaultMessageTitle(type), message, type);
        }

        private void DrawMessageCard(string title, string message, MessageType type)
        {
            EnsureUiStyles();
            GetMessageColors(type, out Color background, out Color accent);

            Rect rect = EditorGUILayout.BeginVertical(messageCardStyle);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUIContent icon = GetMessageIcon(type);
                if (icon != null && icon.image != null)
                {
                    GUILayout.Label(icon, GUILayout.Width(36f), GUILayout.Height(36f));
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(title, messageTitleStyle);
                    EditorGUILayout.LabelField(message, messageBodyStyle);
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(6f);
        }

        private void DrawStatusMessageIfNeeded(ToolTab tab)
        {
            if (statusMessageTab != tab || string.IsNullOrEmpty(statusMessage))
            {
                return;
            }

            DrawMessageCard(statusMessage, statusMessageType);
        }

        private static string GetDefaultMessageTitle(MessageType type)
        {
            switch (type)
            {
                case MessageType.Error:
                    return "Error";
                case MessageType.Warning:
                    return "Advertencia";
                case MessageType.Info:
                    return "Información";
                default:
                    return "Nota";
            }
        }

        private static GUIContent GetMessageIcon(MessageType type)
        {
            if (!messageIcons.TryGetValue(type, out GUIContent icon) || icon == null)
            {
                string iconName = type switch
                {
                    MessageType.Error => EditorGUIUtility.isProSkin ? "d_console.erroricon" : "console.erroricon",
                    MessageType.Warning => EditorGUIUtility.isProSkin ? "d_console.warnicon" : "console.warnicon",
                    MessageType.Info => EditorGUIUtility.isProSkin ? "d_console.infoicon" : "console.infoicon",
                    _ => EditorGUIUtility.isProSkin ? "d_console.infoicon" : "console.infoicon"
                };

                icon = EditorGUIUtility.IconContent(iconName);
                messageIcons[type] = icon;
            }

            return icon;
        }

        private static void GetMessageColors(MessageType type, out Color background, out Color accent)
        {
            bool pro = EditorGUIUtility.isProSkin;
            switch (type)
            {
                case MessageType.Error:
                    accent = new Color(0.85f, 0.3f, 0.3f, 1f);
                    background = pro ? new Color(0.45f, 0.18f, 0.18f, 0.65f) : new Color(1f, 0.8f, 0.8f, 0.65f);
                    break;
                case MessageType.Warning:
                    accent = new Color(0.95f, 0.65f, 0.2f, 1f);
                    background = pro ? new Color(0.45f, 0.32f, 0.15f, 0.65f) : new Color(1f, 0.9f, 0.75f, 0.65f);
                    break;
                case MessageType.Info:
                    accent = new Color(0.3f, 0.65f, 1f, 1f);
                    background = pro ? new Color(0.18f, 0.32f, 0.5f, 0.65f) : new Color(0.72f, 0.84f, 1f, 0.6f);
                    break;
                default:
                    accent = new Color(0.55f, 0.55f, 0.55f, 1f);
                    background = pro ? new Color(0.25f, 0.25f, 0.25f, 0.6f) : new Color(0.9f, 0.9f, 0.9f, 0.55f);
                    break;
            }
        }

        private void DrawVatBakerSection()
        {
            DrawSectionHeader("VAT Baker", "Horneado de texturas de posición animada para tus personajes.");

            DrawStatusMessageIfNeeded(ToolTab.VatBaker);

            DrawMessageCard("Flujo de trabajo", "Genera texturas de posición VAT para cada clip de animación del Animator del objeto seleccionado. Asegúrate de que la ruta de salida permanezca dentro de Assets.", MessageType.Info);

            TryAssignDefaultComputeShader();
            if (infoTexGen == null)
            {
                DrawMessageCard("Shader requerido", $"No se encontró el compute shader interno \"{k_DefaultComputeShaderName}\". Reinstala el paquete o restaura el asset para continuar.", MessageType.Error);
            }

            targetObject = (GameObject)EditorGUILayout.ObjectField("Objeto de destino", targetObject, typeof(GameObject), true);

            DrawTargetObjectDiagnostics();

            Rect pathRect = EditorGUILayout.GetControlRect();
            pathRect = EditorGUI.PrefixLabel(pathRect, new GUIContent("Ruta de salida"));
            outputPath = EditorGUI.TextField(pathRect, outputPath);
            HandleDragAndDrop(pathRect);

            if (GUILayout.Button("Seleccionar carpeta"))
            {
                string selectedFolder = EditorUtility.OpenFolderPanel("Seleccionar carpeta de salida", Application.dataPath, string.Empty);
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
                        ReportStatus("La carpeta seleccionada debe estar dentro de la carpeta Assets del proyecto.", MessageType.Error);
                    }
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!CanBake()))
            {
                if (GUILayout.Button("Hornear texturas de posición VAT"))
                {
                    BakeVatPositionTextures();
                }
            }
        }

        private void DrawVatCombinerSection()
        {
            DrawSectionHeader("VAT Combiner", "Combina múltiples MeshFilters en un único mesh y material compatible con VAT Turbo.");

            DrawStatusMessageIfNeeded(ToolTab.VatCombiner);

            DrawMessageCard("Descripción", "Fusiona geometría estática en un mesh optimizado y genera un material VAT Multiple con offsets y rotaciones prehorneados, replicando la herramienta Mesh Combiner Turbo.", MessageType.Info);

            combinerParentObject = (GameObject)EditorGUILayout.ObjectField("Objeto raíz", combinerParentObject, typeof(GameObject), true);

            DrawCombinerDiagnostics();

            combinerVatMultipleShader = (Shader)EditorGUILayout.ObjectField("Shader VAT Multiple", combinerVatMultipleShader, typeof(Shader), false);
            if (combinerVatMultipleShader == null)
            {
                DrawMessageCard("Shader requerido", "Asigna el shader VAT Multiple que utilizará el material combinado.", MessageType.Info);
            }

            Rect pathRect = EditorGUILayout.GetControlRect();
            pathRect = EditorGUI.PrefixLabel(pathRect, new GUIContent("Ruta de salida"));
            combinerOutputPath = EditorGUI.TextField(pathRect, combinerOutputPath);
            HandleDragAndDrop(pathRect, ref combinerOutputPath);

            if (!IsCombinerOutputPathValid())
            {
                DrawMessageCard("Ruta no válida", "La ruta debe estar dentro de la carpeta Assets del proyecto para guardar el mesh, material y prefab combinados.", MessageType.Warning);
            }

            if (GUILayout.Button("Seleccionar carpeta"))
            {
                string selectedFolder = EditorUtility.OpenFolderPanel("Seleccionar carpeta de salida", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    string projectRelativePath = ConvertToProjectRelativePath(selectedFolder);
                    if (!string.IsNullOrEmpty(projectRelativePath) && IsProjectRelativeFolder(projectRelativePath))
                    {
                        combinerOutputPath = projectRelativePath;
                        Repaint();
                    }
                    else
                    {
                        ReportStatus("La carpeta seleccionada debe estar dentro de la carpeta Assets del proyecto.", MessageType.Error);
                    }
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!CanCombineMeshes()))
            {
                if (GUILayout.Button("Combinar y guardar VAT Turbo"))
                {
                    CombineVatMeshesTurbo();
                }
            }
        }

        private void DrawCombinerDiagnostics()
        {
            if (combinerParentObject == null)
            {
                DrawMessageCard("Objeto requerido", "Selecciona un GameObject raíz que contenga los MeshFilters a combinar.", MessageType.Info);
                return;
            }

            MeshFilter[] meshFilters = combinerParentObject.GetComponentsInChildren<MeshFilter>();
            if (meshFilters == null || meshFilters.Length == 0)
            {
                DrawMessageCard("MeshFilters faltantes", "El objeto raíz no contiene MeshFilters en su jerarquía.", MessageType.Warning);
                return;
            }

            int validCount = 0;
            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter != null && meshFilter.sharedMesh != null && meshFilter.sharedMesh.vertexCount > 0)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                DrawMessageCard("Mallas faltantes", "Los MeshFilters detectados no tienen mallas asignadas.", MessageType.Warning);
            }
            else
            {
                DrawMessageCard("Listo para combinar", $"Se detectaron {validCount} mallas válidas para combinar.", MessageType.Info);
            }
        }

        private void DrawVatPainterSection()
        {
            DrawSectionHeader("VAT Painter", "Organiza tus grupos VAT y pinta directamente en la escena.");

            DrawStatusMessageIfNeeded(ToolTab.VatPainter);

            DrawMessageCard("Cómo funciona", "Pinta prefabs preparados para VAT sobre una superficie con MeshCollider. Cada grupo combina varias mallas y materiales para generar variedad automática.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Añadir grupo de pintado"))
                {
                    paintGroups.Add(new PaintGroup { groupName = GenerateUniqueGroupName() });
                    InvalidatePainterHierarchy();
                }

                using (new EditorGUI.DisabledScope(GetPaintRoot(false) == null))
                {
                    if (GUILayout.Button("Limpiar instancias pintadas"))
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
                DrawMessageCard("Sin grupos", "No hay grupos de pintado definidos. Añade uno para comenzar a colocar personajes VAT.", MessageType.Info);
            }

            EditorGUILayout.Space();

            painterFocusTarget = (Transform)EditorGUILayout.ObjectField("Objetivo de enfoque", painterFocusTarget, typeof(Transform), true);

            GameObject newSurface = (GameObject)EditorGUILayout.ObjectField("Superficie de pintado", painterSurface, typeof(GameObject), true);
            if (newSurface != painterSurface)
            {
                painterSurface = newSurface;
                UpdatePaintSurfaceCollider();
            }

            painterBrushRadius = EditorGUILayout.Slider("Radio del pincel", painterBrushRadius, 0.05f, 25f);
            painterBrushDensity = EditorGUILayout.IntSlider("Densidad del pincel", painterBrushDensity, 1, 64);
            painterMinDistance = EditorGUILayout.Slider("Distancia mínima entre instancias", painterMinDistance, 0f, 10f);

            DrawPainterDiagnostics();

            bool requestedMode = GUILayout.Toggle(painterPaintingMode, "Activar modo de pintado", "Button");
            if (requestedMode != painterPaintingMode)
            {
                TogglePaintingMode(requestedMode);
            }

            if (painterPaintingMode)
            {
                DrawMessageCard("Controles en escena", "Haz clic izquierdo en la vista de escena para pintar instancias VAT. Mantén Alt para seguir navegando con la cámara.", MessageType.Info);
            }

        }

        private void DrawVatUvVisualSection()
        {
            DrawSectionHeader("VAT UV Visual", "Ajusta visualmente las coordenadas UV de una malla con una textura de referencia.");

            DrawStatusMessageIfNeeded(ToolTab.VatUvVisual);

            DrawMessageCard("Consejos de uso", "Haz clic y arrastra en la vista previa para desplazar las UV. Mientras arrastras usa la rueda del ratón para escalar y emplea el control de rotación para giros precisos.", MessageType.Info);

            DrawUvVisualAtlasBuilder();

            uvVisualReferenceTexture = (Texture2D)EditorGUILayout.ObjectField("Textura de referencia", uvVisualReferenceTexture, typeof(Texture2D), false);

            DrawUvVisualTargetControls();

            Mesh mesh = GetUvVisualMesh();

            DrawUvVisualDiagnostics(mesh);

            bool hasValidUvs = uvVisualOriginalUvs != null && uvVisualOriginalUvs.Length > 0;

            using (new EditorGUI.DisabledScope(!hasValidUvs))
            {
                Vector2 previousPosition = uvVisualPosition;
                uvVisualPosition = EditorGUILayout.Vector2Field("Posición", uvVisualPosition);
                if (!Mathf.Approximately(previousPosition.x, uvVisualPosition.x) || !Mathf.Approximately(previousPosition.y, uvVisualPosition.y))
                {
                    StoreActiveTargetTransform();
                }

                EditorGUILayout.BeginHorizontal();
                Vector2 previousScale = uvVisualScale;
                Vector2 newScale = EditorGUILayout.Vector2Field("Escala", uvVisualScale);

                if (uvVisualLockUniformScale)
                {
                    if (!Mathf.Approximately(newScale.x, uvVisualScale.x))
                    {
                        newScale.y = newScale.x;
                    }
                    else if (!Mathf.Approximately(newScale.y, uvVisualScale.y))
                    {
                        newScale.x = newScale.y;
                    }
                }

                newScale.x = Mathf.Clamp(newScale.x, 0.01f, 100f);
                newScale.y = Mathf.Clamp(newScale.y, 0.01f, 100f);

                bool newLock = GUILayout.Toggle(uvVisualLockUniformScale, new GUIContent("Uniforme"), "Button", GUILayout.Width(90f));
                if (!uvVisualLockUniformScale && newLock)
                {
                    float uniform = Mathf.Max(0.01f, (newScale.x + newScale.y) * 0.5f);
                    newScale = new Vector2(uniform, uniform);
                }

                EditorGUILayout.EndHorizontal();

                bool scaleChanged = !Mathf.Approximately(previousScale.x, newScale.x) || !Mathf.Approximately(previousScale.y, newScale.y);

                uvVisualScale = newScale;
                uvVisualLockUniformScale = newLock;
                if (scaleChanged)
                {
                    StoreActiveTargetTransform();
                }

                float previousRotation = uvVisualRotation;
                uvVisualRotation = EditorGUILayout.Slider("Rotación", uvVisualRotation, -360f, 360f);
                if (!Mathf.Approximately(previousRotation, uvVisualRotation))
                {
                    StoreActiveTargetTransform();
                }
            }

            EditorGUILayout.Space();

            Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(420f));
            DrawUvVisualBackground(previewRect);

            if (uvVisualReferenceTexture != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(previewRect, uvVisualReferenceTexture, ScaleMode.ScaleToFit);
            }

            DrawUvVisualGrid(previewRect);
            DrawUvVisualPreview(previewRect, mesh);

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Restablecer gizmo"))
                {
                    ResetUvVisualTransform();
                    Repaint();
                }

                using (new EditorGUI.DisabledScope(!hasValidUvs))
                {
                    if (GUILayout.Button("Aplicar UV a la malla"))
                    {
                        ApplyUvVisualTransform(mesh);
                    }

                    if (GUILayout.Button("Restaurar UV originales"))
                    {
                        UndoUvVisualChanges(mesh);
                    }
                }
            }
        }

        private UnityEngine.Object uvVisualTargetSelection
        {
            get
            {
                if (uvVisualTargetSelectionOverride != null)
                {
                    return uvVisualTargetSelectionOverride;
                }

                if (uvVisualTargetSkinnedMeshRenderer != null)
                {
                    return uvVisualTargetSkinnedMeshRenderer;
                }

                return uvVisualTargetMeshFilter;
            }
        }

        private bool TryAssignUvVisualTarget(UnityEngine.Object newTarget)
        {
            if (newTarget == null)
            {
                uvVisualTargetMeshFilter = null;
                uvVisualTargetSkinnedMeshRenderer = null;
                uvVisualTargetSelectionOverride = null;
                return true;
            }

            MeshFilter meshFilter = null;
            SkinnedMeshRenderer skinnedMeshRenderer = null;
            UnityEngine.Object selectionObject = null;

            if (newTarget is MeshFilter directMeshFilter)
            {
                meshFilter = directMeshFilter;
                selectionObject = directMeshFilter;
            }
            else if (newTarget is SkinnedMeshRenderer directSkinnedMesh)
            {
                skinnedMeshRenderer = directSkinnedMesh;
                selectionObject = directSkinnedMesh;
            }
            else if (newTarget is GameObject go)
            {
                if (!TryFindUvVisualTargetInGameObject(go, out meshFilter, out skinnedMeshRenderer))
                {
                    ReportStatus($"\"{go.name}\" no contiene un MeshFilter ni un SkinnedMeshRenderer en su jerarquía.", MessageType.Warning, false);
                    return false;
                }

                selectionObject = go;
            }
            else if (newTarget is Component component)
            {
                GameObject componentGameObject = component.gameObject;

                if (!TryFindUvVisualTargetInGameObject(componentGameObject, out meshFilter, out skinnedMeshRenderer))
                {
                    ReportStatus($"\"{componentGameObject.name}\" no contiene un MeshFilter ni un SkinnedMeshRenderer en su jerarquía.", MessageType.Warning, false);
                    return false;
                }

                selectionObject = componentGameObject;
            }
            else
            {
                ReportStatus("Selecciona un MeshFilter, SkinnedMeshRenderer o un GameObject que los contenga.", MessageType.Warning, false);
                return false;
            }

            if (!(newTarget is MeshFilter) && skinnedMeshRenderer != null)
            {
                meshFilter = null;
            }

            uvVisualTargetMeshFilter = meshFilter;
            uvVisualTargetSkinnedMeshRenderer = skinnedMeshRenderer;
            uvVisualTargetSelectionOverride = selectionObject ?? newTarget;

            return true;
        }

        private static bool TryFindUvVisualTargetInGameObject(GameObject candidate, out MeshFilter meshFilter, out SkinnedMeshRenderer skinnedMeshRenderer)
        {
            meshFilter = null;
            skinnedMeshRenderer = null;

            if (candidate == null)
            {
                return false;
            }

            skinnedMeshRenderer = candidate.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skinnedMeshRenderer != null)
            {
                return true;
            }

            meshFilter = candidate.GetComponentInChildren<MeshFilter>(true);
            return meshFilter != null;
        }

        private void DrawUvVisualAtlasBuilder()
        {
            uvVisualShowAtlasBuilder = EditorGUILayout.BeginFoldoutHeaderGroup(uvVisualShowAtlasBuilder, "Generador de atlas de referencia");
            if (uvVisualShowAtlasBuilder)
            {
                EditorGUILayout.HelpBox("Crea una textura de referencia combinando varias imágenes individuales. El atlas generado se asignará automáticamente al control de textura de referencia.", MessageType.None);

                int newCount = EditorGUILayout.IntSlider("Número de imágenes", uvVisualAtlasImageCount, 1, 16);
                if (newCount != uvVisualAtlasImageCount)
                {
                    uvVisualAtlasImageCount = newCount;
                    EnsureUvVisualAtlasSourceListSize();
                }

                EnsureUvVisualAtlasSourceListSize();

                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < uvVisualAtlasSourceTextures.Count; i++)
                    {
                        uvVisualAtlasSourceTextures[i] = (Texture2D)EditorGUILayout.ObjectField($"Imagen {i + 1}", uvVisualAtlasSourceTextures[i], typeof(Texture2D), false);
                    }
                }

                uvVisualAtlasCellResolution = EditorGUILayout.IntPopup("Resolución por imagen", uvVisualAtlasCellResolution, k_UvVisualAtlasResolutionLabels, k_UvVisualAtlasResolutionSizes);

                Rect exportFolderRect = EditorGUILayout.GetControlRect();
                exportFolderRect = EditorGUI.PrefixLabel(exportFolderRect, new GUIContent("Carpeta de exportación"));
                uvVisualAtlasExportFolder = EditorGUI.TextField(exportFolderRect, uvVisualAtlasExportFolder);
                int previousIndentLevel = EditorGUI.indentLevel;
                HandleDragAndDrop(exportFolderRect, ref uvVisualAtlasExportFolder);
                EditorGUI.indentLevel = previousIndentLevel;

                if (!IsUvVisualAtlasExportPathValid())
                {
                    DrawMessageCard("Ruta no válida", "La carpeta debe estar dentro de Assets para exportar el atlas.", MessageType.Warning);
                }

                if (GUILayout.Button("Seleccionar carpeta de exportación"))
                {
                    string selectedFolder = EditorUtility.OpenFolderPanel("Seleccionar carpeta de exportación", Application.dataPath, string.Empty);
                    if (!string.IsNullOrEmpty(selectedFolder))
                    {
                        string projectRelativePath = ConvertToProjectRelativePath(selectedFolder);
                        if (!string.IsNullOrEmpty(projectRelativePath))
                        {
                            uvVisualAtlasExportFolder = projectRelativePath;
                            Repaint();
                        }
                        else
                        {
                            ReportStatus("La carpeta seleccionada debe estar dentro de la carpeta Assets del proyecto.", MessageType.Error);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Generar atlas de referencia"))
                    {
                        GenerateUvVisualReferenceAtlas();
                    }

                    using (new EditorGUI.DisabledScope(uvVisualGeneratedAtlas == null))
                    {
                        if (GUILayout.Button("Exportar atlas"))
                        {
                            ExportUvVisualReferenceAtlas();
                        }

                        if (GUILayout.Button("Limpiar atlas generado"))
                        {
                            if (uvVisualGeneratedAtlas != null)
                            {
                                if (uvVisualReferenceTexture == uvVisualGeneratedAtlas)
                                {
                                    uvVisualReferenceTexture = null;
                                }

                                DestroyImmediate(uvVisualGeneratedAtlas);
                                uvVisualGeneratedAtlas = null;
                            }

                            Repaint();
                        }
                    }
                }

                if (uvVisualGeneratedAtlas != null)
                {
                    float atlasAspect = (float)uvVisualGeneratedAtlas.width / uvVisualGeneratedAtlas.height;
                    Rect atlasPreviewRect = GUILayoutUtility.GetAspectRect(atlasAspect, GUILayout.MaxHeight(256f), GUILayout.ExpandWidth(true));
                    EditorGUI.DrawPreviewTexture(atlasPreviewRect, uvVisualGeneratedAtlas, null, ScaleMode.ScaleToFit);
                    EditorGUILayout.HelpBox($"Atlas generado: {uvVisualGeneratedAtlas.width}x{uvVisualGeneratedAtlas.height} píxeles.", MessageType.Info);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawUvVisualTargetControls()
        {
            EditorGUILayout.LabelField("Mallas objetivo", EditorStyles.boldLabel);

            if (uvVisualTargets.Count == 0)
            {
                EditorGUILayout.HelpBox("Agrega una o varias mallas para visualizar y transformar sus UV.", MessageType.Info);
            }

            for (int i = 0; i < uvVisualTargets.Count; i++)
            {
                if (DrawUvVisualTargetRow(i))
                {
                    // La lista se ha modificado dentro de la fila, por lo que debemos detenernos.
                    break;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                uvVisualNewTargetCandidate = EditorGUILayout.ObjectField("Añadir malla objetivo", uvVisualNewTargetCandidate, typeof(UnityEngine.Object), true);

                using (new EditorGUI.DisabledScope(uvVisualNewTargetCandidate == null))
                {
                    if (GUILayout.Button("Agregar", GUILayout.Width(80f)))
                    {
                        if (TryAddUvVisualTarget(uvVisualNewTargetCandidate))
                        {
                            uvVisualNewTargetCandidate = null;
                        }
                    }
                }
            }
        }

        private bool DrawUvVisualTargetRow(int index)
        {
            if (index < 0 || index >= uvVisualTargets.Count)
            {
                return false;
            }

            UvVisualTargetEntry entry = uvVisualTargets[index];

            using (new EditorGUILayout.HorizontalScope())
            {
                bool wasActive = uvVisualActiveTargetIndex == index;
                bool newActive = GUILayout.Toggle(wasActive, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(20f));
                if (newActive && !wasActive)
                {
                    SetActiveUvVisualTarget(index);
                }

                UnityEngine.Object displayTarget = GetUvVisualTargetDisplayObject(entry);
                UnityEngine.Object updatedTarget = EditorGUILayout.ObjectField(displayTarget, typeof(UnityEngine.Object), true);

                if (updatedTarget != displayTarget)
                {
                    if (TryPopulateUvVisualTargetEntry(entry, updatedTarget))
                    {
                        if (!entry.HasValidRenderer)
                        {
                            RemoveUvVisualTargetAt(index);
                            return true;
                        }

                        if (uvVisualActiveTargetIndex == index)
                        {
                            ResetUvVisualTargetCache();
                            Repaint();
                        }
                    }
                    else
                    {
                        Repaint();
                    }
                }

                if (GUILayout.Button("X", GUILayout.Width(22f)))
                {
                    RemoveUvVisualTargetAt(index);
                    return true;
                }
            }

            return false;
        }

        private bool TryAddUvVisualTarget(UnityEngine.Object candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            var entry = new UvVisualTargetEntry();
            if (!TryPopulateUvVisualTargetEntry(entry, candidate) || !entry.HasValidRenderer)
            {
                return false;
            }

            uvVisualTargets.Add(entry);
            SetActiveUvVisualTarget(uvVisualTargets.Count - 1);

            return true;
        }

        private void RemoveUvVisualTargetAt(int index)
        {
            if (index < 0 || index >= uvVisualTargets.Count)
            {
                return;
            }

            bool removingActive = uvVisualActiveTargetIndex == index;

            uvVisualTargets.RemoveAt(index);

            if (uvVisualTargets.Count == 0)
            {
                uvVisualActiveTargetIndex = -1;
                ResetUvVisualTargetCache();
                Repaint();
                return;
            }

            if (removingActive)
            {
                int newIndex = Mathf.Clamp(index, 0, uvVisualTargets.Count - 1);
                uvVisualActiveTargetIndex = -1;
                SetActiveUvVisualTarget(newIndex);
            }
            else if (uvVisualActiveTargetIndex > index)
            {
                uvVisualActiveTargetIndex = Mathf.Clamp(uvVisualActiveTargetIndex - 1, 0, uvVisualTargets.Count - 1);
            }
        }

        private void SetActiveUvVisualTarget(int index)
        {
            if (index < 0 || index >= uvVisualTargets.Count)
            {
                uvVisualActiveTargetIndex = -1;
                ResetUvVisualTargetCache();
                Repaint();
                return;
            }

            if (uvVisualActiveTargetIndex == index)
            {
                return;
            }

            uvVisualActiveTargetIndex = index;
            ResetUvVisualTargetCache();
            Repaint();
        }

        private UvVisualTargetEntry GetActiveUvVisualTarget()
        {
            if (uvVisualActiveTargetIndex < 0 || uvVisualActiveTargetIndex >= uvVisualTargets.Count)
            {
                return null;
            }

            UvVisualTargetEntry entry = uvVisualTargets[uvVisualActiveTargetIndex];
            if (entry == null || !entry.HasValidRenderer)
            {
                return null;
            }

            return entry;
        }

        private static UnityEngine.Object GetUvVisualTargetDisplayObject(UvVisualTargetEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (entry.selectionOverride != null)
            {
                return entry.selectionOverride;
            }

            if (entry.skinnedMeshRenderer != null)
            {
                return entry.skinnedMeshRenderer;
            }

            return entry.meshFilter;
        }

        private bool TryPopulateUvVisualTargetEntry(UvVisualTargetEntry entry, UnityEngine.Object newTarget)
        {
            if (entry == null)
            {
                return false;
            }

            if (newTarget == null)
            {
                entry.meshFilter = null;
                entry.skinnedMeshRenderer = null;
                entry.selectionOverride = null;
                return true;
            }

            MeshFilter meshFilter = null;
            SkinnedMeshRenderer skinnedMeshRenderer = null;
            UnityEngine.Object selectionObject = null;

            if (newTarget is MeshFilter directMeshFilter)
            {
                meshFilter = directMeshFilter;
                selectionObject = directMeshFilter;
            }
            else if (newTarget is SkinnedMeshRenderer directSkinnedMesh)
            {
                skinnedMeshRenderer = directSkinnedMesh;
                selectionObject = directSkinnedMesh;
            }
            else if (newTarget is GameObject go)
            {
                if (!TryFindUvVisualTargetInGameObject(go, out meshFilter, out skinnedMeshRenderer))
                {
                    ReportStatus($"\"{go.name}\" no contiene un MeshFilter ni un SkinnedMeshRenderer en su jerarquía.", MessageType.Warning, false);
                    return false;
                }

                selectionObject = go;
            }
            else if (newTarget is Component component)
            {
                GameObject componentGameObject = component.gameObject;

                if (!TryFindUvVisualTargetInGameObject(componentGameObject, out meshFilter, out skinnedMeshRenderer))
                {
                    ReportStatus($"\"{componentGameObject.name}\" no contiene un MeshFilter ni un SkinnedMeshRenderer en su jerarquía.", MessageType.Warning, false);
                    return false;
                }

                selectionObject = componentGameObject;
            }
            else
            {
                ReportStatus("Selecciona un MeshFilter, SkinnedMeshRenderer o un GameObject que los contenga.", MessageType.Warning, false);
                return false;
            }

            if (!(newTarget is MeshFilter) && skinnedMeshRenderer != null)
            {
                meshFilter = null;
            }

            entry.meshFilter = meshFilter;
            entry.skinnedMeshRenderer = skinnedMeshRenderer;
            entry.selectionOverride = selectionObject ?? newTarget;

            return true;
        }

        private void DrawUvVisualDiagnostics(Mesh mesh)
        {
            if (GetActiveUvVisualTarget() == null)
            {
                DrawMessageCard("Selecciona una malla", "Asigna uno o varios Mesh Filters, Skinned Mesh Renderers u objetos que los contengan para visualizar y transformar sus UV.", MessageType.Info);
            }
            else if (mesh == null)
            {
                DrawMessageCard("Malla no válida", "El objeto seleccionado no tiene una malla compartida.", MessageType.Warning);
            }
            else if (mesh.uv == null || mesh.uv.Length == 0)
            {
                DrawMessageCard("UV ausentes", "La malla seleccionada no contiene coordenadas UV.", MessageType.Warning);
            }

            if (uvVisualReferenceTexture == null)
            {
                DrawMessageCard("Textura de referencia", "Asigna una textura para visualizar la alineación de las UV (opcional pero recomendado).", MessageType.Info);
            }
        }

        private void EnsureUvVisualAtlasSourceListSize()
        {
            if (uvVisualAtlasImageCount < 1)
            {
                uvVisualAtlasImageCount = 1;
            }

            while (uvVisualAtlasSourceTextures.Count < uvVisualAtlasImageCount)
            {
                uvVisualAtlasSourceTextures.Add(null);
            }

            while (uvVisualAtlasSourceTextures.Count > uvVisualAtlasImageCount)
            {
                uvVisualAtlasSourceTextures.RemoveAt(uvVisualAtlasSourceTextures.Count - 1);
            }
        }

        private void GenerateUvVisualReferenceAtlas()
        {
            EnsureUvVisualAtlasSourceListSize();

            if (uvVisualAtlasSourceTextures.Count == 0)
            {
                ReportStatus("Asigna al menos una imagen para generar el atlas de referencia.", MessageType.Warning);
                return;
            }

            foreach (Texture2D source in uvVisualAtlasSourceTextures)
            {
                if (source == null)
                {
                    ReportStatus("Todos los espacios de imagen deben estar asignados antes de generar el atlas.", MessageType.Warning);
                    return;
                }

                if (!IsTextureReadable(source, out string reason))
                {
                    ReportStatus(reason, MessageType.Error);
                    return;
                }
            }

            int cellResolution = Mathf.Max(1, uvVisualAtlasCellResolution);
            int textureCount = uvVisualAtlasSourceTextures.Count;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(textureCount));
            int rows = Mathf.CeilToInt((float)textureCount / columns);
            int atlasWidth = Mathf.Max(1, columns * cellResolution);
            int atlasHeight = Mathf.Max(1, rows * cellResolution);

            Texture2D atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false)
            {
                name = "VAT_UV_ReferenceAtlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] clearPixels = new Color32[atlasWidth * atlasHeight];
            for (int i = 0; i < clearPixels.Length; i++)
            {
                clearPixels[i] = new Color32(0, 0, 0, 0);
            }

            atlas.SetPixels32(clearPixels);

            for (int index = 0; index < textureCount; index++)
            {
                int column = index % columns;
                int row = index / columns;
                int offsetX = column * cellResolution;
                int offsetY = row * cellResolution;

                CopyTextureToAtlas(uvVisualAtlasSourceTextures[index], atlas, offsetX, offsetY, cellResolution);
            }

            atlas.Apply();

            if (uvVisualGeneratedAtlas != null)
            {
                DestroyImmediate(uvVisualGeneratedAtlas);
            }

            uvVisualGeneratedAtlas = atlas;
            uvVisualReferenceTexture = uvVisualGeneratedAtlas;

            ReportStatus($"Atlas de referencia generado con {textureCount} imágenes ({atlasWidth}x{atlasHeight} píxeles).", MessageType.Info, false);
            Repaint();
        }

        private void ExportUvVisualReferenceAtlas()
        {
            if (uvVisualGeneratedAtlas == null)
            {
                ReportStatus("No hay un atlas generado para exportar.", MessageType.Warning);
                return;
            }

            string defaultName = string.IsNullOrEmpty(uvVisualGeneratedAtlas.name) ? "VAT_UV_ReferenceAtlas" : uvVisualGeneratedAtlas.name;
            string path = EditorUtility.SaveFilePanelInProject("Exportar atlas de referencia", $"{defaultName}.png", "png", "Selecciona la ubicación dentro de la carpeta Assets para guardar el atlas generado.");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            byte[] pngData = uvVisualGeneratedAtlas.EncodeToPNG();
            if (pngData == null || pngData.Length == 0)
            {
                ReportStatus("No se pudo codificar el atlas generado en formato PNG.", MessageType.Error);
                return;
            }

            try
            {
                File.WriteAllBytes(path, pngData);
            }
            catch (Exception e)
            {
                ReportStatus($"No se pudo guardar el atlas en disco: {e.Message}", MessageType.Error);
                return;
            }

            AssetDatabase.ImportAsset(path);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Texture2D exportedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (exportedTexture != null)
            {
                uvVisualReferenceTexture = exportedTexture;
            }

            Repaint();
            ReportStatus($"Atlas exportado correctamente a \"{path}\".", MessageType.Info);
        }

        private static void CopyTextureToAtlas(Texture2D source, Texture2D atlas, int offsetX, int offsetY, int targetResolution)
        {
            int maxX = Mathf.Min(targetResolution, atlas.width - offsetX);
            int maxY = Mathf.Min(targetResolution, atlas.height - offsetY);

            for (int y = 0; y < maxY; y++)
            {
                float v = targetResolution > 1 ? (float)y / (targetResolution - 1) : 0f;

                for (int x = 0; x < maxX; x++)
                {
                    float u = targetResolution > 1 ? (float)x / (targetResolution - 1) : 0f;
                    Color sampled = source.GetPixelBilinear(u, v);
                    atlas.SetPixel(offsetX + x, offsetY + y, sampled);
                }
            }
        }

        private static bool IsTextureReadable(Texture2D texture, out string reason)
        {
            reason = string.Empty;

            if (texture == null)
            {
                reason = "La textura proporcionada es nula.";
                return false;
            }

            if (texture.isReadable)
            {
                return true;
            }

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(assetPath))
            {
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
                {
                    if (!importer.isReadable)
                    {
                        reason = $"La textura '{texture.name}' no es legible. Habilita la opción Read/Write en su importador para poder generar el atlas.";
                        return false;
                    }
                }
            }

            reason = $"La textura '{texture.name}' no permite lectura en modo de edición.";
            return false;
        }

        private static void DrawUvVisualBackground(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color baseColor = EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f, 1f) : new Color(0.95f, 0.95f, 0.95f, 1f);
            EditorGUI.DrawRect(rect, baseColor);

            Color border = new Color(0.25f, 0.6f, 1f, 0.85f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), border);
        }

        private static void DrawUvVisualGrid(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Handles.BeginGUI();
            Color previous = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.08f);

            const int lines = 8;
            for (int i = 1; i < lines; i++)
            {
                float t = rect.x + rect.width * (i / (float)lines);
                Handles.DrawLine(new Vector3(t, rect.y), new Vector3(t, rect.yMax));

                float y = rect.y + rect.height * (i / (float)lines);
                Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
            }

            Handles.color = previous;
            Handles.EndGUI();
        }

        private void DrawUvVisualPreview(Rect rect, Mesh mesh)
        {
            if (mesh == null || uvVisualOriginalUvs == null || uvVisualOriginalUvs.Length == 0)
            {
                return;
            }

            int[] triangles = mesh.triangles;
            if (triangles == null || triangles.Length == 0)
            {
                return;
            }

            Matrix4x4 previewMatrix = Matrix4x4.TRS(uvVisualPosition, Quaternion.Euler(0f, 0f, uvVisualRotation), uvVisualScale);

            HandleUvVisualInput(rect, previewMatrix);

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Handles.BeginGUI();
            Color previous = Handles.color;
            UvVisualTargetEntry activeTarget = GetActiveUvVisualTarget();
            Color baseColor = activeTarget?.wireColor ?? new Color(0f, 0.78f, 1f, 1f);
            Color fillColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.22f);
            Color outlineColor = Color.Lerp(baseColor, Color.white, 0.2f);
            outlineColor.a = 0.9f;

            Handles.color = fillColor;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 uvA = uvVisualOriginalUvs[triangles[i]];
                Vector2 uvB = uvVisualOriginalUvs[triangles[i + 1]];
                Vector2 uvC = uvVisualOriginalUvs[triangles[i + 2]];

                Vector3 transformedA = previewMatrix.MultiplyPoint3x4(new Vector3(uvA.x, uvA.y, 0f));
                Vector3 transformedB = previewMatrix.MultiplyPoint3x4(new Vector3(uvB.x, uvB.y, 0f));
                Vector3 transformedC = previewMatrix.MultiplyPoint3x4(new Vector3(uvC.x, uvC.y, 0f));

                Vector2 a = UvVisualUvToScreen(new Vector2(transformedA.x, transformedA.y), rect);
                Vector2 b = UvVisualUvToScreen(new Vector2(transformedB.x, transformedB.y), rect);
                Vector2 c = UvVisualUvToScreen(new Vector2(transformedC.x, transformedC.y), rect);

                Handles.DrawAAConvexPolygon(a, b, c);
                Handles.color = outlineColor;
                Handles.DrawAAPolyLine(2f, a, b, c, a);
                Handles.color = fillColor;
            }

            Vector3 pivot = previewMatrix.MultiplyPoint3x4(Vector3.zero);
            Vector3 axisX = previewMatrix.MultiplyPoint3x4(new Vector3(0.2f, 0f, 0f));
            Vector3 axisY = previewMatrix.MultiplyPoint3x4(new Vector3(0f, 0.2f, 0f));

            Vector2 pivotScreen = UvVisualUvToScreen(new Vector2(pivot.x, pivot.y), rect);
            Vector2 axisXScreen = UvVisualUvToScreen(new Vector2(axisX.x, axisX.y), rect);
            Vector2 axisYScreen = UvVisualUvToScreen(new Vector2(axisY.x, axisY.y), rect);

            Handles.color = new Color(1f, 0.35f, 0.35f, 0.9f);
            Handles.DrawAAPolyLine(3f, pivotScreen, axisXScreen);
            Handles.color = new Color(0.35f, 1f, 0.5f, 0.9f);
            Handles.DrawAAPolyLine(3f, pivotScreen, axisYScreen);

            Handles.color = previous;
            Handles.EndGUI();
        }

        private void HandleUvVisualInput(Rect rect, Matrix4x4 previewMatrix)
        {
            Event e = Event.current;
            if (e == null)
            {
                return;
            }

            Vector2 mouseUv = UvVisualScreenToUv(e.mousePosition, rect);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition) && IsMouseNearAnyTransformedUv(mouseUv, previewMatrix))
                    {
                        uvVisualIsDragging = true;
                        uvVisualDragStartMousePos = e.mousePosition;
                        uvVisualDragStartUvPos = uvVisualPosition;
                        GUI.FocusControl(null);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (uvVisualIsDragging)
                    {
                        Vector2 deltaPixels = e.mousePosition - uvVisualDragStartMousePos;
                        Vector2 deltaUv = new Vector2(deltaPixels.x / rect.width, -deltaPixels.y / rect.height);
                        uvVisualPosition = uvVisualDragStartUvPos + deltaUv;
                        StoreActiveTargetTransform();
                        Repaint();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (uvVisualIsDragging && e.button == 0)
                    {
                        uvVisualIsDragging = false;
                        e.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (uvVisualIsDragging && rect.Contains(e.mousePosition))
                    {
                        float scroll = -e.delta.y;
                        float scaleFactor = 1f + (scroll * 0.05f);

                        if (uvVisualLockUniformScale)
                        {
                            uvVisualScale *= scaleFactor;
                        }
                        else
                        {
                            uvVisualScale.x *= scaleFactor;
                            uvVisualScale.y *= scaleFactor;
                        }

                        uvVisualScale.x = Mathf.Clamp(uvVisualScale.x, 0.01f, 100f);
                        uvVisualScale.y = Mathf.Clamp(uvVisualScale.y, 0.01f, 100f);

                        StoreActiveTargetTransform();
                        Repaint();
                        e.Use();
                    }
                    break;
            }
        }

        private bool IsMouseNearAnyTransformedUv(Vector2 mouseUv, Matrix4x4 previewMatrix)
        {
            if (uvVisualOriginalUvs == null || uvVisualOriginalUvs.Length == 0)
            {
                return false;
            }

            const float threshold = 0.05f;
            for (int i = 0; i < uvVisualOriginalUvs.Length; i++)
            {
                Vector2 uv = uvVisualOriginalUvs[i];
                Vector3 transformed = previewMatrix.MultiplyPoint3x4(new Vector3(uv.x, uv.y, 0f));
                Vector2 transformed2D = new Vector2(transformed.x, transformed.y);

                if (Vector2.Distance(transformed2D, mouseUv) < threshold)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetUvVisualTargetCache()
        {
            uvVisualLastMesh = null;
            uvVisualOriginalUvs = null;
            uvVisualInitialUvs = null;
            uvVisualIsDragging = false;
        }

        private void ResetUvVisualTransform(bool propagateToActiveTarget = true)
        {
            uvVisualPosition = Vector2.zero;
            uvVisualScale = Vector2.one;
            uvVisualRotation = 0f;
            uvVisualIsDragging = false;

            if (propagateToActiveTarget)
            {
                StoreActiveTargetTransform();
            }
        }

        private Mesh GetUvVisualMesh()
        {
            UvVisualTargetEntry activeTarget = GetActiveUvVisualTarget();
            if (activeTarget == null)
            {
                uvVisualOriginalUvs = null;
                uvVisualInitialUvs = null;
                uvVisualLastMesh = null;
                return null;
            }

            Mesh mesh = activeTarget.SharedMesh;
            if (mesh == null)
            {
                uvVisualOriginalUvs = null;
                uvVisualInitialUvs = null;
                uvVisualLastMesh = null;
                return null;
            }

            Vector2[] meshUvs = mesh.uv;
            if (meshUvs == null || meshUvs.Length == 0)
            {
                uvVisualOriginalUvs = null;
                return mesh;
            }

            if (uvVisualLastMesh != mesh || uvVisualOriginalUvs == null || uvVisualOriginalUvs.Length != meshUvs.Length)
            {
                uvVisualOriginalUvs = (Vector2[])meshUvs.Clone();
                uvVisualInitialUvs = (Vector2[])meshUvs.Clone();
                uvVisualLastMesh = mesh;
            }

            return mesh;
        }

        private void ApplyUvVisualTransform(Mesh mesh)
        {
            Mesh previewMesh = mesh ?? GetUvVisualMesh();
            if (previewMesh == null)
            {
                ReportStatus("Selecciona una malla válida para aplicar la transformación UV.", MessageType.Warning);
                return;
            }

            if (uvVisualOriginalUvs == null || uvVisualOriginalUvs.Length == 0)
            {
                ReportStatus("La malla seleccionada no contiene coordenadas UV transformables.", MessageType.Warning);
                return;
            }

            Matrix4x4 transformMatrix = Matrix4x4.TRS(uvVisualPosition, Quaternion.Euler(0f, 0f, uvVisualRotation), uvVisualScale);
            var processedMeshes = new HashSet<Mesh>();
            int affectedCount = 0;

            foreach (UvVisualTargetEntry entry in uvVisualTargets)
            {
                if (entry == null)
                {
                    continue;
                }

                Mesh targetMesh = entry.SharedMesh;
                if (targetMesh == null || processedMeshes.Contains(targetMesh))
                {
                    continue;
                }

                Vector2[] sourceUvs;
                if (targetMesh == previewMesh)
                {
                    sourceUvs = (Vector2[])uvVisualOriginalUvs.Clone();
                }
                else
                {
                    Vector2[] meshUvs = targetMesh.uv;
                    if (meshUvs == null || meshUvs.Length == 0)
                    {
                        continue;
                    }

                    sourceUvs = (Vector2[])meshUvs.Clone();
                }

                Vector2[] transformed = new Vector2[sourceUvs.Length];
                for (int i = 0; i < sourceUvs.Length; i++)
                {
                    Vector3 result = transformMatrix.MultiplyPoint3x4(new Vector3(sourceUvs[i].x, sourceUvs[i].y, 0f));
                    transformed[i] = new Vector2(result.x, result.y);
                }

                Undo.RecordObject(targetMesh, "Aplicar transformación UV");
                targetMesh.uv = transformed;
                EditorUtility.SetDirty(targetMesh);

                if (targetMesh == previewMesh)
                {
                    uvVisualOriginalUvs = (Vector2[])transformed.Clone();
                }

                processedMeshes.Add(targetMesh);
                affectedCount++;
            }

            if (affectedCount == 0)
            {
                ReportStatus("No se encontraron mallas válidas para aplicar la transformación UV.", MessageType.Warning);
                return;
            }

            uvVisualPosition = Vector2.zero;
            uvVisualScale = Vector2.one;
            uvVisualRotation = 0f;
            Repaint();

            uvVisualPosition = Vector2.zero;
            uvVisualScale = Vector2.one;
            Repaint();

            string message = affectedCount > 1 ? "Transformación UV aplicada a las mallas seleccionadas." : "Transformación UV aplicada correctamente.";
            ReportStatus(message, MessageType.Info);
        }

        private void UndoUvVisualChanges(Mesh mesh)
        {
            mesh ??= GetUvVisualMesh();
            if (mesh == null)
            {
                ReportStatus("No hay una malla válida para restaurar.", MessageType.Warning);
                return;
            }

            if (uvVisualInitialUvs == null || uvVisualInitialUvs.Length == 0)
            {
                ReportStatus("No se ha guardado un estado original de UV para esta malla.", MessageType.Warning);
                return;
            }

            Undo.RecordObject(mesh, "Restaurar UV originales");
            Vector2[] restored = (Vector2[])uvVisualInitialUvs.Clone();
            mesh.uv = restored;
            EditorUtility.SetDirty(mesh);

            uvVisualOriginalUvs = (Vector2[])restored.Clone();

            ResetUvVisualTransform();
            LoadActiveUvVisualTargetTransform();
            Repaint();

            ReportStatus("UV restauradas a su estado original.", MessageType.Info);
        }

        private static Vector2 UvVisualScreenToUv(Vector2 screenPosition, Rect rect)
        {
            float x = (screenPosition.x - rect.x) / rect.width;
            float y = 1f - ((screenPosition.y - rect.y) / rect.height);
            return new Vector2(x, y);
        }

        private static Vector2 UvVisualUvToScreen(Vector2 uv, Rect rect)
        {
            return new Vector2(rect.x + uv.x * rect.width, rect.y + (1f - uv.y) * rect.height);
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
            if (GUILayout.Button("Eliminar", GUILayout.Width(70f)))
            {
                removeGroup = true;
            }
            EditorGUILayout.EndHorizontal();

            if (group.isExpanded)
            {
                EditorGUI.indentLevel++;

                string newName = EditorGUILayout.TextField("Nombre del grupo", group.groupName);
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
            EditorGUILayout.LabelField("Mesh Filters (mallas origen)", EditorStyles.boldLabel);
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
                if (GUILayout.Button("Añadir Mesh Filter"))
                {
                    group.meshFilters.Add(null);
                }

                if (GUILayout.Button("Añadir selección"))
                {
                    AddSelectedMeshFilters(group);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawMaterialList(PaintGroup group)
        {
            EditorGUILayout.LabelField("Materiales VAT", EditorStyles.boldLabel);
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
                if (GUILayout.Button("Añadir material"))
                {
                    group.vatMaterials.Add(null);
                    InvalidatePainterHierarchy(group);
                }

                if (GUILayout.Button("Añadir selección"))
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
                DrawMessageCard("Superficie pendiente", "Asigna una superficie de pintado con MeshCollider para recibir los trazos del pincel.", MessageType.Info);
            }
            else if (painterSurfaceCollider == null)
            {
                DrawMessageCard("MeshCollider requerido", "La superficie seleccionada no incluye un componente MeshCollider.", MessageType.Warning);
            }

            if (!HasAnyValidPaintGroup())
            {
                DrawMessageCard("Configura tus grupos", "Crea al menos un grupo con Mesh Filters y materiales VAT válidos para comenzar a pintar.", MessageType.Warning);
            }

            if (painterFocusTarget == null)
            {
                DrawMessageCard("Objetivo de enfoque", "Asigna un objetivo para orientar las instancias pintadas. Sin él conservarán su orientación original.", MessageType.Info);
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
                Undo.RegisterCreatedObjectUndo(painterRoot, "Crear raíz de pintado VAT");
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

            Undo.RegisterCreatedObjectUndo(instance, "Pintar instancia VAT");
            instance.transform.position = position;

            AlignPaintedInstance(instance.transform, position, normal);

            MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Undo.RecordObject(renderer, "Asignar material VAT");
                renderer.sharedMaterial = material;
            }

            Transform parent = GetOrCreateGroupParent(group, materialIndex);
            if (parent != null)
            {
                Undo.SetTransformParent(instance.transform, parent, "Asignar contenedor de pintado");
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
                    Undo.RegisterCreatedObjectUndo(container, "Crear contenedor de grupo VAT");
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

        private bool CanCombineMeshes()
        {
            return combinerParentObject != null && combinerVatMultipleShader != null && IsCombinerOutputPathValid();
        }

        private bool IsCombinerOutputPathValid()
        {
            if (string.IsNullOrEmpty(combinerOutputPath))
            {
                return false;
            }

            if (!combinerOutputPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return IsProjectRelativeFolder(combinerOutputPath);
        }

        private bool CanBake()
        {
            return infoTexGen != null && targetObject != null && !string.IsNullOrEmpty(outputPath);
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            HandleDragAndDrop(dropArea, ref outputPath);
        }

        private void HandleDragAndDrop(Rect dropArea, ref string targetPath)
        {
            Event current = Event.current;
            if ((current.type == EventType.DragUpdated || current.type == EventType.DragPerform) && dropArea.Contains(current.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    bool assigned = TryAssignOutputPathFromPaths(DragAndDrop.paths, ref targetPath) ||
                                     TryAssignOutputPathFromObjectReferences(DragAndDrop.objectReferences, ref targetPath);

                    if (!assigned)
                    {
                        ReportStatus("La carpeta arrastrada debe estar dentro de la carpeta Assets del proyecto.", MessageType.Error);
                    }

                    current.Use();
                }
            }
        }

        private void OnUvVisualExportFolderChanged()
        {
            if (string.IsNullOrEmpty(uvVisualAtlasExportFolder))
            {
                uvVisualAtlasExportFolder = string.Empty;
                uvVisualAtlasExportFolderAsset = null;
                return;
            }

            uvVisualAtlasExportFolder = uvVisualAtlasExportFolder.Replace('\\', '/');

            if (!IsUvVisualAtlasExportPathValid())
            {
                uvVisualAtlasExportFolderAsset = null;
                return;
            }

            uvVisualAtlasExportFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(uvVisualAtlasExportFolder);
        }

        private static string GetPathFromFolderAsset(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                return string.Empty;
            }

            string assetPath = AssetDatabase.GetAssetPath(folderAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return assetPath;
            }

            string directory = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(directory) ? string.Empty : directory.Replace('\\', '/');
        }

        private bool TryAssignOutputPathFromPaths(IEnumerable<string> paths, ref string targetPath)
        {
            if (paths == null)
            {
                return false;
            }

            foreach (string path in paths)
            {
                if (TryAssignOutputPath(path, ref targetPath))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryAssignOutputPathFromObjectReferences(UnityEngine.Object[] objectReferences, ref string targetPath)
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

                if (TryAssignOutputPath(assetPath, ref targetPath))
                {
                    return true;
                }

                string directory = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(directory) && TryAssignOutputPath(directory, ref targetPath))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryAssignOutputPath(string rawPath, ref string targetPath)
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

            if (!string.Equals(targetPath, projectRelativePath, StringComparison.Ordinal))
            {
                targetPath = projectRelativePath;
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

        private bool ValidateCombinerInputs()
        {
            if (combinerParentObject == null)
            {
                ReportStatus("Asigna un objeto raíz que contenga los MeshFilters a combinar.", MessageType.Error);
                return false;
            }

            if (combinerVatMultipleShader == null)
            {
                ReportStatus("Asigna un shader VAT Multiple para generar el material combinado.", MessageType.Error);
                return false;
            }

            if (string.IsNullOrEmpty(combinerOutputPath))
            {
                ReportStatus("La ruta de salida no puede estar vacía.", MessageType.Error);
                return false;
            }

            if (!IsCombinerOutputPathValid())
            {
                ReportStatus("La ruta de salida debe estar dentro de la carpeta Assets del proyecto.", MessageType.Error);
                return false;
            }

            return true;
        }

        private void CombineVatMeshesTurbo()
        {
            if (!ValidateCombinerInputs())
            {
                return;
            }

            MeshFilter[] meshFilters = combinerParentObject.GetComponentsInChildren<MeshFilter>();
            if (meshFilters == null || meshFilters.Length == 0)
            {
                ReportStatus("No se encontraron MeshFilters bajo el objeto raíz seleccionado.", MessageType.Error);
                return;
            }

            List<CombineInstance> combineInstances = new List<CombineInstance>();
            List<Vector4> combinedOffsets = new List<Vector4>();
            List<Vector4> combinedRotations = new List<Vector4>();
            List<Material> originalMaterials = new List<Material>();

            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter == null)
                {
                    continue;
                }

                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0)
                {
                    continue;
                }

                CombineInstance instance = new CombineInstance
                {
                    mesh = mesh,
                    transform = meshFilter.transform.localToWorldMatrix
                };
                combineInstances.Add(instance);

                Vector3 position = meshFilter.transform.position;
                Quaternion rotation = meshFilter.transform.rotation;

                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    combinedOffsets.Add(new Vector4(position.x, position.y, position.z, 0f));
                    combinedRotations.Add(new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));
                }

                MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    originalMaterials.Add(renderer.sharedMaterial);
                }
            }

            if (combineInstances.Count == 0)
            {
                ReportStatus("Los MeshFilters encontrados no contienen mallas válidas para combinar.", MessageType.Error);
                return;
            }

            Mesh combinedMesh = new Mesh
            {
                name = combinerParentObject.name + "_TurboCombinedMesh"
            };

            if (combinedOffsets.Count > 65535)
            {
                combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            combinedMesh.SetUVs(2, combinedOffsets);
            combinedMesh.SetUVs(3, combinedRotations);
            combinedMesh.RecalculateBounds();

            GameObject combinedObject = new GameObject(combinerParentObject.name + "_TurboCombined");
            combinedObject.AddComponent<MeshFilter>().sharedMesh = combinedMesh;

            Material vatMaterial = new Material(combinerVatMultipleShader);
            if (originalMaterials.Count > 0)
            {
                CopyMaterialProperties(originalMaterials[0], vatMaterial);
            }

            vatMaterial.SetInt("_NumberOfMeshes", combineInstances.Count);
            vatMaterial.SetInt("_TotalVertex", combinedMesh.vertexCount);

            MeshRenderer finalRenderer = combinedObject.AddComponent<MeshRenderer>();
            finalRenderer.sharedMaterial = vatMaterial;

            if (!EnsureDirectoryExists(combinerOutputPath))
            {
                DestroyImmediate(combinedObject);
                return;
            }

            string sanitizedMeshPath = Path.Combine(combinerOutputPath, combinedMesh.name + ".asset").Replace("\\", "/");
            string meshAssetPath = AssetDatabase.GenerateUniqueAssetPath(sanitizedMeshPath);
            AssetDatabase.CreateAsset(combinedMesh, meshAssetPath);

            string sanitizedMatPath = Path.Combine(combinerOutputPath, combinerParentObject.name + "_TurboVAT_Material.mat").Replace("\\", "/");
            string matAssetPath = AssetDatabase.GenerateUniqueAssetPath(sanitizedMatPath);
            AssetDatabase.CreateAsset(vatMaterial, matAssetPath);

            AssetDatabase.SaveAssets();

            string sanitizedPrefabPath = Path.Combine(combinerOutputPath, combinedObject.name + ".prefab").Replace("\\", "/");
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(sanitizedPrefabPath);
            PrefabUtility.SaveAsPrefabAsset(combinedObject, prefabPath);
            AssetDatabase.SaveAssets();

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject prefabInstance = null;
            if (prefabAsset != null)
            {
                prefabInstance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            }
            if (prefabInstance != null)
            {
                Selection.activeGameObject = prefabInstance;
            }

            DestroyImmediate(combinedObject);

            ReportStatus($"Combinación completada. Recursos guardados en '{combinerOutputPath}'.", MessageType.Info);
            SceneView.RepaintAll();
        }

        private static void CopyMaterialProperties(Material source, Material destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Shader sourceShader = source.shader;
            if (sourceShader == null)
            {
                return;
            }

            int propertyCount = ShaderUtil.GetPropertyCount(sourceShader);
            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = ShaderUtil.GetPropertyName(sourceShader, i);
                ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(sourceShader, i);

                switch (propertyType)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        destination.SetColor(propertyName, source.GetColor(propertyName));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        destination.SetVector(propertyName, source.GetVector(propertyName));
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        destination.SetFloat(propertyName, source.GetFloat(propertyName));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        destination.SetTexture(propertyName, source.GetTexture(propertyName));
                        break;
                }
            }
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
                ReportStatus("No se encontró un SkinnedMeshRenderer en el objeto seleccionado.", MessageType.Error);
                return;
            }

            if (skin.sharedMesh == null)
            {
                ReportStatus("El SkinnedMeshRenderer del objeto seleccionado no tiene una malla asignada.", MessageType.Error);
                return;
            }

            Animator animator = targetObject.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                ReportStatus("No se encontró un Animator con RuntimeAnimatorController en el objeto seleccionado.", MessageType.Error);
                return;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0)
            {
                ReportStatus("El Animator del objeto seleccionado no contiene clips de animación.", MessageType.Warning);
                return;
            }

            Mesh mesh = new Mesh();

            try
            {
                ReportStatus("Horneando texturas de posición VAT...", MessageType.Info, false);
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

            ReportStatus($"Horneado de texturas VAT completado. Recursos generados en '{outputPath}'.", MessageType.Info);
        }

        private bool ValidateInputs()
        {
            if (infoTexGen == null)
            {
                TryAssignDefaultComputeShader();
                if (infoTexGen == null)
                {
                    ReportStatus($"No se encontró el compute shader interno \"{k_DefaultComputeShaderName}\".", MessageType.Error);
                    return false;
                }
            }

            if (!infoTexGen.HasKernel(k_KernelName))
            {
                ReportStatus($"El Compute Shader no contiene un kernel llamado '{k_KernelName}'.", MessageType.Error);
                return false;
            }

            if (targetObject == null)
            {
                ReportStatus("No se asignó un objeto de destino.", MessageType.Error);
                return false;
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                ReportStatus("La ruta de salida no puede estar vacía.", MessageType.Error);
                return false;
            }

            if (!outputPath.StartsWith("Assets"))
            {
                ReportStatus("La ruta de salida debe estar dentro de la carpeta Assets del proyecto.", MessageType.Error);
                return false;
            }

            return true;
        }

        private bool EnsureOutputDirectory()
        {
            return EnsureDirectoryExists(outputPath);
        }

        private bool EnsureDirectoryExists(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                ReportStatus("La ruta de salida no puede estar vacía.", MessageType.Error);
                return false;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                ReportStatus("No se pudo determinar la ruta raíz del proyecto.", MessageType.Error);
                return false;
            }

            string absolutePath = Path.Combine(projectRoot, projectRelativePath);
            if (string.IsNullOrEmpty(absolutePath))
            {
                ReportStatus("No se pudo resolver el directorio de salida.", MessageType.Error);
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
                DrawMessageCard("Skinned Mesh requerido", "El objeto seleccionado no contiene un SkinnedMeshRenderer. Necesitas una malla esquelética para hornear VAT.", MessageType.Warning);
            }
            else if (skin.sharedMesh == null)
            {
                DrawMessageCard("Malla ausente", "El SkinnedMeshRenderer detectado no tiene una malla asignada.", MessageType.Warning);
            }
            else
            {
                DrawMessageCard("Malla lista", $"Skinned mesh detectada: {skin.sharedMesh.name} ({skin.sharedMesh.vertexCount} vértices).", MessageType.Info);
            }

            Animator animator = targetObject.GetComponent<Animator>();
            if (animator == null)
            {
                DrawMessageCard("Animator requerido", "El objeto seleccionado no contiene un componente Animator. Añade uno para muestrear las animaciones.", MessageType.Warning);
                return;
            }

            if (animator.runtimeAnimatorController == null)
            {
                DrawMessageCard("Controlador faltante", "El Animator no tiene asignado un RuntimeAnimatorController.", MessageType.Warning);
                return;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0)
            {
                DrawMessageCard("Sin clips", "El controlador de Animator no expone clips de animación para hornear.", MessageType.Warning);
            }
            else
            {
                DrawMessageCard("Animator listo", $"Clips detectados: {clips.Length}.", MessageType.Info);
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
            statusMessageTab = (ToolTab)activeTabIndex;

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
