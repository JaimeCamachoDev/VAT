using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using UnityEngine.Formats.Alembic.Importer;

public enum TopologyType
{
	Undefined,
	Analysing,
	Variable,
	Fixed	
}

public class ATV_Editor : EditorWindow
{  
	public string ExportPath = "Assets/ExportVAT/";
    public bool NameFromAlembicPlayer = true;
    public string ExportFilename = "AlembicVAT";
	public float StartTime = 0.0f;
	public float EndTime = 10.0f;
	public float SampleRate = 24.0f;
	public bool StoreCenterPositionInUV3 = false;
    public bool FromBlender = false;
    public bool UnlitMesh = false;
    public bool CompressNormal = false;
    public TopologyType VariableTopology = TopologyType.Undefined;
	private List<string> directoryList = new List<string>();
	private int directoryIndex = 0;
	private float progress = 0.0f;

    public AlembicStreamPlayer AlembicPlayer;
    public Shader ReferenceShader;
    public Shader UnlitReferenceShader;

    private Transform meshToBake;

    [MenuItem("Window/Alembic to VAT")]
	public static void ShowWindow()
	{
		EditorWindow.GetWindow(typeof(ATV_Editor));
	}


    public void Awake()
	{
		ReferenceShader = Resources.Load<Shader>("VAT_SRP_Lit_Material");
		UnlitReferenceShader = Resources.Load<Shader>("VAT_SRP_Unlit_Material");

        if (ReferenceShader == null)
            ReferenceShader = Resources.Load<Shader>("VAT_Legacy_Material");
        if (UnlitReferenceShader == null)
            UnlitReferenceShader = Resources.Load<Shader>("VAT_Legacy_Material");

        directoryList.Clear();
    }

	void RefreshMenu()
    {
        var folders = AssetDatabase.GetSubFolders("Assets");
        foreach (var folder in folders)
        {
            RecursiveSearchResources(folder);
        }
    }

    void RecursiveSearchResources(string folder)
    {
        if (folder.ToLower().EndsWith("/resources"))
		{
			string modifiedFolder = folder.Replace('/', '\u2215');
            directoryList.Add(modifiedFolder);
        }
        var folders = AssetDatabase.GetSubFolders(folder);
        foreach (var fld in folders)
        {
            RecursiveSearchResources(fld);
        }
    }

    void handleDirectoriesItemClicked(object obj)
    {
		Debug.Log("Selected: " + obj);
    }


void OnGUI()
	{
		GUILayout.Label ("Source", EditorStyles.boldLabel);
		EditorGUI.BeginChangeCheck();
		AlembicPlayer = EditorGUILayout.ObjectField("Alembic player", AlembicPlayer, typeof(AlembicStreamPlayer), true) as AlembicStreamPlayer;
		if (EditorGUI.EndChangeCheck())
		{
            if (AlembicPlayer != null)
			{
                Debug.Log("Object to bake has changed ... updating data");
                currentBaking = EditorCoroutineUtility.StartCoroutine(UpdateFromAlembic(), this);
                if (NameFromAlembicPlayer)
                    ExportFilename = AlembicPlayer.gameObject.name;
            } else
			{
				if (currentBaking !=null)
					EditorCoroutineUtility.StopCoroutine(currentBaking);
                VariableTopology = TopologyType.Undefined;
            }
        }

		GUILayout.Space(10);
		GUILayout.Label ("Export", EditorStyles.boldLabel);

		RefreshMenu();
		directoryIndex = EditorGUILayout.Popup("Export path",directoryIndex, directoryList.ToArray());
        ExportPath = directoryList[directoryIndex].Replace('\u2215', '/');

        NameFromAlembicPlayer = EditorGUILayout.Toggle("Name from alembic player", NameFromAlembicPlayer);
		ExportFilename = EditorGUILayout.TextField ("Export filename", ExportFilename);
		GUILayout.Space(2);
		GUILayout.Label ("Final path : "+ ExportPath + "/"+ExportFilename+"_xxx.xxx");
		GUILayout.Space(10);
		GUILayout.Label ("Animation info", EditorStyles.boldLabel);
		if (VariableTopology!=TopologyType.Undefined && VariableTopology != TopologyType.Analysing)
        {
			StartTime = EditorGUILayout.FloatField("Start time", StartTime);
			EndTime = EditorGUILayout.FloatField("End time", EndTime);
			SampleRate = EditorGUILayout.FloatField("Sample rate", SampleRate);
			if (!float.IsFinite(SampleRate))
				SampleRate = 60.0f;
			StoreCenterPositionInUV3 = EditorGUILayout.Toggle("Store position in UV3", StoreCenterPositionInUV3);
			FromBlender = EditorGUILayout.Toggle("Exported from Blender", FromBlender);
            UnlitMesh = EditorGUILayout.Toggle("Unlit mesh", UnlitMesh);
			if (!UnlitMesh)
			{
                CompressNormal = EditorGUILayout.Toggle("Compress normal", CompressNormal);
            }
            if (UnlitMesh)
                UnlitReferenceShader = EditorGUILayout.ObjectField("Unlit reference shader", UnlitReferenceShader, typeof(Shader), true) as Shader;
            else
                ReferenceShader = EditorGUILayout.ObjectField("Lit reference shader", ReferenceShader, typeof(Shader), true) as Shader;
        }
        switch (VariableTopology)
		{
            case TopologyType.Undefined:
                GUILayout.Label("Undefined topology");
                break;
            case TopologyType.Analysing:
                GUILayout.Label("Analysing topology ... please wait");
                break;
            case TopologyType.Fixed:
				GUILayout.Label ("Fixed topology (morphing mesh)");
				break;
			case TopologyType.Variable:
				GUILayout.Label ("Variable topology (mesh sequence)");
				break;
		}
		GUILayout.Space(10);

		if (VariableTopology!=TopologyType.Undefined && VariableTopology != TopologyType.Analysing)
        {
			if (bakingInProgress)
			{
				if (GUILayout.Button("Cancel bake"))
				{
					CancelBake();
				}
			}
			else
			{
				if (GUILayout.Button("Bake mesh"))
				{
					BakeMesh();
				}
			}
		}
	}

	SerializedProperty timeProp = null;
	SerializedProperty startTimeProp = null;
	SerializedProperty endTimeProp = null;
	SerializedObject alembicObject = null;
	EditorCoroutine currentBaking = null;

	bool bakingInProgress = false;
	int maxTriangleCount=0;
	int minTriangleCount=10000000;

	SerializedObject InitAlembic()
	{
		if (AlembicPlayer == null)
		{
			Debug.LogError("Alembic player!");
			return null;
		}
		alembicObject = new SerializedObject(AlembicPlayer);

		timeProp = alembicObject.FindProperty("currentTime");
		startTimeProp = alembicObject.FindProperty("startTime");
		endTimeProp = alembicObject.FindProperty("endTime");

		return alembicObject;
	}

	private void BakeMesh()
	{
		Debug.Log("Start baking mesh!");
		currentBaking = EditorCoroutineUtility.StartCoroutine(ExportFrames(), this);
	}

	IEnumerator UpdateFromAlembic()
	{
		Debug.Log("Get time from Alembic!");
        VariableTopology = TopologyType.Analysing;
		progress = 0.0f;

        SerializedObject alembic = InitAlembic();
        MeshFilter meshFilter = AlembicPlayer.gameObject.GetComponentInChildren<MeshFilter>();
		meshToBake = meshFilter.transform.parent;

        if (alembic!=null)
		{

			maxTriangleCount=0;
			minTriangleCount=10000000;

			{
				Debug.Log("Checking max triangle count");
				int framesCount = Mathf.RoundToInt((EndTime-StartTime) * SampleRate + 0.5f);	

				for(int frame = 0;frame<framesCount;frame++)
				{
                    progress = (float)frame / (float)framesCount;

                    float timing = StartTime + ((float)frame)/SampleRate;
					timeProp.floatValue = timing; 
					alembicObject.ApplyModifiedProperties();
					yield return null;

					int triangleCount = 0;
					for(int i=0;i< meshToBake.childCount;i++)
					{
						MeshFilter localMeshFilter = meshToBake.GetChild(i).GetComponent<MeshFilter>();

						if (localMeshFilter == null && meshToBake.GetChild(i).childCount>0)
						{
							localMeshFilter = meshToBake.GetChild(i).GetChild(0).GetComponent<MeshFilter>();
							//if (localMeshFilter != null)
							//	Debug.Log("Found a submesh at " + i);
							//else
							//	Debug.Log("Not found at " + i);
						}

						if (localMeshFilter != null)
						{
							triangleCount += localMeshFilter.sharedMesh.triangles.Length/3;
						}
					}
					if (triangleCount>maxTriangleCount)
						maxTriangleCount = triangleCount;
					if (triangleCount<minTriangleCount)
						minTriangleCount = triangleCount;
				}
				Debug.Log("Max triangles count : " + maxTriangleCount);
				Debug.Log("Min triangles count : " + minTriangleCount);
			}
			yield return null;
			StartTime = 0.0f;
			SampleRate = 1.0f/startTimeProp.floatValue;
			EndTime = endTimeProp.floatValue;
			VariableTopology = (maxTriangleCount == minTriangleCount)?TopologyType.Fixed : TopologyType.Variable;
			yield return null;
		}
	}

	private void CancelBake()
	{
		Debug.Log("Cancel current baking!");
		EditorCoroutineUtility.StopCoroutine(currentBaking);
	}

	Vector2 getUV(int Xpos, int Ypos, int Xsize, int Ysize)
	{
		Vector2 uv = new Vector2();

		uv.x = (0.5f + (float)Xpos)/(float)Xsize;
		uv.y = (0.5f + (float)Ypos)/(float)Ysize;

		return uv;
	}

	Vector2Int getCoord(int Xindex, int Yindex, int Xsize, int Ysize, int columnSize)
	{
		Vector2Int uv = new Vector2Int();
		
		int columnIndex = Yindex/Ysize;
		int verticalIndex = Yindex % Ysize;

		uv.x = Xindex + columnIndex*columnSize;
		uv.y = verticalIndex;

		return uv;
	}

    IEnumerator ExportFrames()
	{
		Mesh bakedMesh=null;
		Vector3[] vertices;
		Vector2[] uv;
		Vector3[] uv3;
		Vector3[] normals;
		Color[] colors;
		int[] triangles;
		int verticesCount = 0;
		int trianglesIndexCount = 0;

		string finalExportPath = ExportPath + "/";

		SerializedObject alembic = InitAlembic();

		timeProp.floatValue = StartTime; 
		alembicObject.ApplyModifiedProperties();
		yield return null;


		bakedMesh = new Mesh();
		bakedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

		verticesCount = 0;
		trianglesIndexCount = 0;

		if ((meshToBake != null) && (meshToBake.childCount!=0))
		{
			bool hasNormal = false;
			bool hasUVs = false;
			bool hasColors = false;

			if (VariableTopology == TopologyType.Variable)
			{
				hasNormal = true;
				verticesCount = maxTriangleCount*3;
				trianglesIndexCount = maxTriangleCount*3;
			}
			else
			{
				for(int i=0;i<meshToBake.childCount;i++)
				{
					MeshFilter localMeshFilter = meshToBake.GetChild(i).GetComponent<MeshFilter>();

					if (localMeshFilter == null && meshToBake.GetChild(i).childCount > 0)
					{
						localMeshFilter = meshToBake.GetChild(i).GetChild(0).GetComponent<MeshFilter>();
						//if (localMeshFilter != null)
						//    Debug.Log("Found a submesh at " + i);
						//else
						//    Debug.Log("Not found at " + i);
					}

					if (localMeshFilter != null)
					{
						verticesCount += localMeshFilter.sharedMesh.vertices.Length;
						trianglesIndexCount += localMeshFilter.sharedMesh.triangles.Length;

						hasNormal |= (localMeshFilter.sharedMesh.normals.Length>0);
						hasColors |= (localMeshFilter.sharedMesh.colors.Length>0);
						hasUVs |= (localMeshFilter.sharedMesh.uv.Length>0);				
					}
				}		
			}

			vertices = new Vector3[verticesCount];
			uv = new Vector2[verticesCount];
			uv3 = new Vector3[verticesCount];
			normals = new Vector3[verticesCount];
			colors = new Color[verticesCount];
			triangles = new int[trianglesIndexCount];

			int currentTrianglesIndex = 0;
			int verticesOffset = 0;

			if (VariableTopology == TopologyType.Variable)
			{
				for(int i=0;i<verticesCount;i++)	// everything is initialized to 0
				{
					triangles[i] = i;
					vertices[i] = Vector3.zero;
					normals[i] = Vector3.up;
				}
			}
			else
			{
				for(int i=0;i<meshToBake.childCount;i++)
				{
					MeshFilter localMeshFilter = meshToBake.GetChild(i).GetComponent<MeshFilter>();

					if (localMeshFilter == null && meshToBake.GetChild(i).childCount > 0)
					{
						localMeshFilter = meshToBake.GetChild(i).GetChild(0).GetComponent<MeshFilter>();
						//if (localMeshFilter != null)
						//    Debug.Log("Found a submesh at " + i);
						//else
						//    Debug.Log("Not found at " + i);
					}

					if (localMeshFilter != null)
					{
						Vector3 center = Vector3.zero;
						
						for(int j=0;j<localMeshFilter.sharedMesh.vertices.Length;j++)
						{
							if (hasUVs)
								uv[j + verticesOffset] = localMeshFilter.sharedMesh.uv[j];
							if (hasColors)
								colors[j + verticesOffset] = localMeshFilter.sharedMesh.colors[j];

							vertices[j + verticesOffset] = localMeshFilter.sharedMesh.vertices[j];
							center += localMeshFilter.sharedMesh.vertices[j];
						}

						center /= (float)localMeshFilter.sharedMesh.vertices.Length;

						if (StoreCenterPositionInUV3)
						{
							for (int j = 0; j < localMeshFilter.sharedMesh.vertices.Length; j++)
							{
								uv3[j + verticesOffset] = center;
							}
						}

						for (int j=0;j<localMeshFilter.sharedMesh.triangles.Length;j++)
						{
							triangles[currentTrianglesIndex++] = localMeshFilter.sharedMesh.triangles[j] + verticesOffset;
						}

						verticesOffset += localMeshFilter.sharedMesh.vertices.Length;
					}
				}
			}
			
			bakedMesh.vertices = vertices;
			if (hasUVs)
				bakedMesh.uv = uv;
			if (hasNormal)
				bakedMesh.normals = normals;
			if (hasColors)
				bakedMesh.colors = colors;
			bakedMesh.triangles = triangles;
			if (StoreCenterPositionInUV3)
				bakedMesh.SetUVs(2,uv3);

			int[] textureSize = {32,64,128,256,512,1024,2048,4096,8192,16384};
			bakedMesh.RecalculateBounds();

			int columns = -1;
			int textureHeight=-1;
			int textureWidth=-1;

			int vertexCount = vertices.Length;
			int framesCount = Mathf.RoundToInt((EndTime-StartTime) * SampleRate + 0.5f);	
			int adjustedFramesCount = framesCount +2; // = space between columns

			Debug.Log("Frames count : "+framesCount);
			Debug.Log("Vertices count : "+vertexCount);

			bool exportVAT = true;

			columns = Mathf.CeilToInt(Mathf.Sqrt((float)vertexCount/(float)adjustedFramesCount));
			Debug.Log("Initial columns : "+columns);
			int textureHeightAdjusted =  Mathf.CeilToInt(((float)vertexCount/(float)columns));
			for(int i=0;i<textureSize.Length;i++)
			{
				if ((textureHeight==-1) && (textureHeightAdjusted<=textureSize[i]))
					textureHeight = textureSize[i];
			}
			Debug.Log("Wanted height : "+textureHeightAdjusted+" - next POW 2 : "+textureHeight);
			if (textureHeight==-1)
			{
				Debug.LogError("Alembic too big to be encoded in VAT format ... too high");
				exportVAT=false;
			}

			if (exportVAT)
			{
				columns = Mathf.CeilToInt(((float)vertexCount/(float)textureHeight));

				Debug.Log("Adjusted columns : "+columns);
				for(int i=0;i<textureSize.Length;i++)
				{
					if ((textureWidth==-1) && ((adjustedFramesCount*columns)<=textureSize[i]))
						textureWidth = textureSize[i];
				}
				Debug.Log("Wanted width : "+(adjustedFramesCount*columns)+" - next POW 2 : "+textureWidth);

				if (textureWidth==-1)
				{
					Debug.LogError("Alembic too big to be encoded in VAT format ... too wide");
					exportVAT=false;
				}
			}


            Debug.Log("Delete older prefabs");
			AssetDatabase.DeleteAsset(finalExportPath + ExportFilename + "_position.asset");
			AssetDatabase.DeleteAsset(finalExportPath + ExportFilename + "_normal.asset");
			AssetDatabase.DeleteAsset(finalExportPath + ExportFilename + "_mesh.asset");
            AssetDatabase.DeleteAsset(finalExportPath + ExportFilename + "_material.mat");
            AssetDatabase.DeleteAsset(finalExportPath + ExportFilename + "_prefab.prefab");

            if (exportVAT)
			{
				Bounds newBounds = new Bounds();
				Vector3 minBounds = new Vector3(1e9f, 1e9f, 1e9f);
				Vector3 maxBounds = new Vector3(-1e9f, -1e9f, -1e9f);

				Vector2[] uv2 = new Vector2[verticesCount];

				Debug.Log("Texture size : "+textureWidth+" x "+textureHeight+" Vertices : "+vertexCount+" Frames : "+framesCount);

                Texture2D positionTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBAHalf, false, true);
				if (VariableTopology == TopologyType.Variable)
					positionTexture.filterMode = FilterMode.Point;
				positionTexture.wrapMode = TextureWrapMode.Clamp;
				Texture2D normalTexture = null;

				if (!UnlitMesh)
				{
					if (CompressNormal)
                        normalTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false, true);
                    else
						normalTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBAHalf, false, true);
                    if (VariableTopology == TopologyType.Variable)
                        normalTexture.filterMode = FilterMode.Point;
                    normalTexture.wrapMode = TextureWrapMode.Clamp;
                }

                for (int frame = 0;frame<framesCount;frame++)
				{
					float timing = StartTime + ((float)frame)/SampleRate;
					Debug.Log("Encoding frame "+frame+" / "+framesCount+" ("+timing+")");
					timeProp.floatValue = timing; 
					alembicObject.ApplyModifiedProperties();
					yield return null;

					if (VariableTopology == TopologyType.Variable)
					{
						MeshFilter localMeshFilter = meshToBake.GetChild(0).GetComponent<MeshFilter>();

						if (localMeshFilter == null && meshToBake.GetChild(0).childCount > 0)
						{
							localMeshFilter = meshToBake.GetChild(0).GetChild(0).GetComponent<MeshFilter>();
							//if (localMeshFilter != null)
							//    Debug.Log("Found a submesh at " + 0);
							//else
							//    Debug.Log("Not found at " + 0);
						}

						if (localMeshFilter != null && localMeshFilter.sharedMesh.subMeshCount>0)
						{
							List<Vector3> local_vertices = new List<Vector3>(); 
							localMeshFilter.sharedMesh.GetVertices(local_vertices);
							List<Vector3> local_normals = new List<Vector3>(); 
							localMeshFilter.sharedMesh.GetNormals(local_normals);
							int[] local_index= localMeshFilter.sharedMesh.GetTriangles(0);

							for(int targetIndex=0 ; targetIndex<maxTriangleCount*3 ;targetIndex++)
							{
								Vector2Int coordinates = getCoord(frame, targetIndex, textureWidth, textureHeight, adjustedFramesCount);
								Vector2Int coordinates_0 = getCoord(0, targetIndex, textureWidth, textureHeight, adjustedFramesCount);
								Vector2 uvCoord = getUV(coordinates_0.x, coordinates_0.y, textureWidth, textureHeight);

								uv2[targetIndex] = uvCoord;

								Vector3 newVertexPos = Vector3.zero;
								Vector3 newVertexNrm  = Vector3.up;

								if (targetIndex<local_index.Length)
								{
									int vtxIndex = local_index[targetIndex];
									newVertexPos = local_vertices[vtxIndex];
									newVertexNrm = local_normals[vtxIndex];

									if (FromBlender)
									{
										newVertexPos = localMeshFilter.transform.TransformPoint(newVertexPos);
										newVertexNrm = localMeshFilter.transform.TransformDirection(newVertexNrm);
									}
								}

								minBounds = Vector3.Min(minBounds, newVertexPos);
								maxBounds = Vector3.Max(maxBounds, newVertexPos);

								float alpha = 1.0f;

                                positionTexture.SetPixel(coordinates.x, coordinates.y, new Color(newVertexPos.x, newVertexPos.y, newVertexPos.z, alpha));
                                if (!UnlitMesh)
								{
                                    newVertexNrm = newVertexNrm.normalized;
                                    newVertexNrm = newVertexNrm * 0.5f + Vector3.one * 0.5f; // Encode to positive only values

                                    normalTexture.SetPixel(coordinates.x, coordinates.y, new Color(newVertexNrm.x, newVertexNrm.y, newVertexNrm.z, 1.0f));
                                }
                            }

						}
					}
					else
					{
						Debug.Log("Doing animated solid meshes ");
						verticesOffset = 0;
						for(int i=0;i<meshToBake.childCount;i++)
						{
							MeshFilter localMeshFilter = meshToBake.GetChild(i).GetComponent<MeshFilter>();
							if (localMeshFilter == null && meshToBake.GetChild(i).childCount > 0)
							{
								localMeshFilter = meshToBake.GetChild(i).GetChild(0).GetComponent<MeshFilter>();
							}
							
							if (localMeshFilter != null)
							{
								List<Vector3> local_vertices = new List<Vector3>(); 
								localMeshFilter.sharedMesh.GetVertices(local_vertices);
								List<Vector3> local_normals = new List<Vector3>(); 
								localMeshFilter.sharedMesh.GetNormals(local_normals);
								int[] local_index= localMeshFilter.sharedMesh.GetTriangles(0);


								for(int j=0;j<local_vertices.Count;j++)
								{
									int targetIndex = j + verticesOffset;

									Vector2Int coordinates = getCoord(frame, targetIndex, textureWidth, textureHeight, adjustedFramesCount);
									Vector2Int coordinates_0 = getCoord(0, targetIndex, textureWidth, textureHeight, adjustedFramesCount);
									Vector2 uvCoord = getUV(coordinates_0.x, coordinates_0.y, textureWidth, textureHeight);

									uv2[targetIndex] = uvCoord;

									Vector3 newVertexPos = local_vertices[j];
									Vector3 newVertexNrm = local_normals[j];

									if (FromBlender)
									{
										newVertexPos = localMeshFilter.transform.TransformPoint(newVertexPos);
										newVertexNrm = localMeshFilter.transform.TransformDirection(newVertexNrm);
									}

									Vector3 refVertexPos = vertices[targetIndex];
									newVertexPos -= refVertexPos;

									minBounds = Vector3.Min(minBounds, newVertexPos);
									maxBounds = Vector3.Max(maxBounds, newVertexPos);

									positionTexture.SetPixel(coordinates.x, coordinates.y, new Color(newVertexPos.x, newVertexPos.y, newVertexPos.z, 1.0f));	
									if (!UnlitMesh)
									{
										newVertexNrm = newVertexNrm.normalized;
										newVertexNrm = newVertexNrm * 0.5f + Vector3.one * 0.5f; // Encode to positive only values
                                        normalTexture.SetPixel(coordinates.x, coordinates.y, new Color(newVertexNrm.x, newVertexNrm.y, newVertexNrm.z, 1.0f));
                                    }
                                }
								verticesOffset += local_vertices.Count;
							}
						}
					}
				}

				newBounds.max = maxBounds;
				newBounds.min = minBounds;
				Debug.Log("Min bounds : "+minBounds.x+" , "+minBounds.y+" , "+minBounds.z);
				Debug.Log("Max bounds : "+maxBounds.x+" , "+maxBounds.y+" , "+maxBounds.z);

				bakedMesh.bounds = newBounds;

				positionTexture.Apply();
				if (!UnlitMesh)
					normalTexture.Apply();

                Debug.Log("Saving positions texture asset at "+finalExportPath+ExportFilename+"_position.asset");
				AssetDatabase.CreateAsset(positionTexture,finalExportPath+ExportFilename+"_position.asset" );
				AssetDatabase.SaveAssets();
				if (!UnlitMesh)
				{
                    Debug.Log("Saving normals texture asset at " + finalExportPath + ExportFilename + "_normal.asset");
                    AssetDatabase.CreateAsset(normalTexture, finalExportPath + ExportFilename + "_normal.asset");
                    AssetDatabase.SaveAssets();
                }

                bakedMesh.uv2 = uv2;
			}



            Debug.Log("Saving merged mesh asset at "+finalExportPath+ExportFilename+"_mesh.asset");
			AssetDatabase.CreateAsset(bakedMesh,finalExportPath+ExportFilename+"_mesh.asset" );
			AssetDatabase.SaveAssets();
			yield return null;

            Debug.Log("Create prefab");

			Debug.Log("Saving material asset");
			Material newMaterial = new Material(UnlitMesh? UnlitReferenceShader : ReferenceShader);
			newMaterial.name = ExportFilename+"_material";
			newMaterial.SetFloat("_Framecount", (float)framesCount);

			Texture2D resPosTexture = Resources.Load<Texture2D >(ExportFilename+"_position");
			Texture2D resNormalTexture = Resources.Load<Texture2D >(ExportFilename+"_normal");
			if (resPosTexture == null)
				Debug.Log("Can't load position texture "+finalExportPath+ExportFilename+"_position.asset");
			if (resNormalTexture == null)
				Debug.Log("Can't load position texture "+finalExportPath+ExportFilename+"_normal.asset");
			
			newMaterial.SetTexture("_VAT_positions",resPosTexture );
			newMaterial.SetTexture("_VAT_normals",resNormalTexture);

			AssetDatabase.CreateAsset(newMaterial,finalExportPath+ExportFilename+"_material.mat" );
			AssetDatabase.SaveAssets();

			GameObject newGameObject = new GameObject(ExportFilename+"_Object");

			Mesh resMesh = Resources.Load<Mesh>(ExportFilename+"_mesh");
			if (resMesh == null)
				Debug.Log("Unable to reload created mesh");

			Material resMaterial = Resources.Load<Material>(ExportFilename+"_material");
			if (resMaterial == null)
				Debug.Log("Unable to reload material");

			MeshFilter meshFilter = newGameObject.AddComponent<MeshFilter>();
			meshFilter.mesh = resMesh;
			MeshRenderer meshRenderer = newGameObject.AddComponent<MeshRenderer>();
			meshRenderer.material = resMaterial;

			PrefabUtility.SaveAsPrefabAsset(newGameObject, finalExportPath+ExportFilename+"_prefab.prefab");

			DestroyImmediate(newGameObject);
		}		
	}
}