using UnityEngine;

public abstract class ToolBase : ScriptableObject
{
    public string ToolName = "New Tool";
    public Texture2D Icon;
    public abstract void OnGUI();
    public virtual void Undo() { }
    public virtual void Redo() { }
}
