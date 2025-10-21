using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "VAT Tools/Tool Group")]
public class ScriptableToolGroup : ScriptableObject
{
    public List<ToolBase> tools = new List<ToolBase>();
}
