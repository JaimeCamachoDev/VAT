using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class ToolHubEditorWindow : EditorWindow
{
    private List<ToolBase> tools = new List<ToolBase>();
    private ToolBase activeTool;
    private ScriptableToolGroup toolGroup;

    private Vector2 leftScroll, rightScroll;

    [MenuItem("VAT Tools/Tool Hub")]
    public static void ShowWindow()
    {
        var window = GetWindow<ToolHubEditorWindow>("VAT Tool Hub");
        window.minSize = new Vector2(600, 400);
        window.LoadTools();
    }

    private void LoadTools()
    {
        tools.Clear();

        // Si no hay grupo asignado, intenta cargar el primero disponible
        if (toolGroup == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:ScriptableToolGroup");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                toolGroup = AssetDatabase.LoadAssetAtPath<ScriptableToolGroup>(path);
            }
        }

        if (toolGroup != null)
        {
            tools = new List<ToolBase>(toolGroup.tools);
            if (tools.Count > 0)
                activeTool = tools[0];
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();

        // Selector manual de grupo
        EditorGUI.BeginChangeCheck();
        toolGroup = (ScriptableToolGroup)EditorGUILayout.ObjectField("Tool Group", toolGroup, typeof(ScriptableToolGroup), false);
        if (EditorGUI.EndChangeCheck())
        {
            LoadTools();
        }

        if (toolGroup == null)
        {
            EditorGUILayout.HelpBox("No se encontró ningún ScriptableToolGroup. Crea uno con clic derecho > Create > VAT Tools > Tool Group.", MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();

        DrawToolSidebar();
        DrawToolUI();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawToolSidebar()
    {
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll, GUILayout.Width(180));

        foreach (var tool in tools)
        {
            if (tool == null) continue;

            Rect rect = GUILayoutUtility.GetRect(180, 40);
            if (GUI.Button(rect, GUIContent.none))
            {
                activeTool = tool;
            }

            if (tool.Icon != null)
            {
                Rect iconRect = new Rect(rect.xMax - 36, rect.y + 4, 32, 32);
                GUI.DrawTexture(iconRect, tool.Icon, ScaleMode.ScaleToFit);
            }

            Rect labelRect = new Rect(rect.x + 6, rect.y + 12, rect.width - 48, 20);
            GUI.Label(labelRect, tool.ToolName, EditorStyles.label);
        }

        EditorGUILayout.EndScrollView();
    }


    private void DrawToolUI()
    {
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        if (activeTool != null)
        {
            EditorGUILayout.LabelField(activeTool.ToolName, EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            activeTool.OnGUI();
        }
        else
        {
            EditorGUILayout.HelpBox("Selecciona una herramienta del panel lateral.", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }
}
