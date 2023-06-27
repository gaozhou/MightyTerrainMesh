using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Collections;
using Unity.Jobs;
using MightyTerrainMesh;

public class MTDataEditor : EditorWindow
{
    [MenuItem("Assets/Create/MightyTerrainMesh/LOD Policy")]
    static void CreateLodPolicyData()
    {
        UnityEngine.Object folder = Selection.activeObject;
        if (folder)
        {
            var path = AssetDatabase.GetAssetPath(folder);
            path = string.Format("{0}/{1}", path, "LODPolicy.asset");
            var asset = ScriptableObject.CreateInstance<MTLODPolicy>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
    [MenuItem("MightyTerrainMesh/DataCreator")]
    private static void ShowWindow()
    {
        EditorWindow.CreateWindow<MTDataEditor>();
    }
    //properties
    private int QuadTreeDepth = 2;
    private MTLODSetting[] LODSettings = new MTLODSetting[0];
    private Terrain terrainTarget;
    private bool genUV2 = false;
    private int lodCount = 1;
    private int datapack = 1;//每个文件存多少块mesh
    //
    private CreateDataJob dataCreateJob;
    private TessellationJob tessellationJob;
    //
    private void OnGUI()
    {
        Terrain curentTarget = EditorGUILayout.ObjectField("Convert Target", terrainTarget, typeof(Terrain), true) as Terrain;
        if (curentTarget != terrainTarget)
        {
            terrainTarget = curentTarget;
        }
        int curSliceCount = Mathf.FloorToInt(1 << QuadTreeDepth);
        int sliceCount = EditorGUILayout.IntField("Slice Count(NxN)", curSliceCount);
        if (sliceCount != curSliceCount)
        {
            curSliceCount = Mathf.NextPowerOfTwo(sliceCount);
            QuadTreeDepth = Mathf.FloorToInt(Mathf.Log(curSliceCount, 2));
        }
        if (lodCount != LODSettings.Length)
        {
            MTLODSetting[] old = LODSettings;
            LODSettings = new MTLODSetting[lodCount];
            for (int i = 0; i < Mathf.Min(lodCount, old.Length); ++i)
            {
                LODSettings[i] = old[i];
            }
            for (int i = old.Length; i < lodCount; ++i)
            {
                LODSettings[i] = new MTLODSetting();
                LODSettings[i].Subdivision = 4;
            }
        }
        lodCount = EditorGUILayout.IntField("LOD Count", LODSettings.Length);
        if (LODSettings.Length > 0)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < LODSettings.Length; ++i)
                LODSettings[i].OnGUIDraw(i);
            EditorGUI.indentLevel--;
        }
        datapack = EditorGUILayout.IntField("Data Pack", datapack);
        genUV2 = EditorGUILayout.ToggleLeft("Generate UV2", genUV2);
        if (GUILayout.Button("Generate"))
        {
            if (LODSettings == null || LODSettings.Length == 0)
            {
                MTLog.LogError("no lod setting");
                return;
            }
            if (terrainTarget == null)
            {
                MTLog.LogError("no target terrain");
                return;
            }
            //calculate min tri size
            int max_sub = 1;
            if (LODSettings.Length > 0)
                max_sub = LODSettings[0].Subdivision;
            float max_sub_grids = sliceCount * (1 << max_sub);
            float min_edge_len = Mathf.Max(terrainTarget.terrainData.bounds.size.x, terrainTarget.terrainData.bounds.size.z) / max_sub_grids;
            float minArea = min_edge_len * min_edge_len / 8f;
            //
            var tBnd = new Bounds(terrainTarget.transform.TransformPoint(terrainTarget.terrainData.bounds.center),
                terrainTarget.terrainData.bounds.size);
            dataCreateJob = new CreateDataJob(terrainTarget, tBnd, QuadTreeDepth, LODSettings, 0.5f * min_edge_len);
            for (int i = 0; i < int.MaxValue; ++i)
            {
                dataCreateJob.Update();
                EditorUtility.DisplayProgressBar("creating data", "scaning volumn", dataCreateJob.progress);
                if (dataCreateJob.IsDone)
                    break;
            }
            dataCreateJob.EndProcess();
            //
            tessellationJob = new TessellationDataJob(dataCreateJob.LODs, minArea);
            for (int i = 0; i < int.MaxValue; ++i)
            {
                tessellationJob.Update();
                EditorUtility.DisplayProgressBar("creating data", "tessellation", tessellationJob.progress);
                if (tessellationJob.IsDone)
                    break;
            }
            //build quad tree data, this is the first level quadtree
            MTQuadTreeBuildNode treeRoot = new MTQuadTreeBuildNode(QuadTreeDepth, tBnd.min, tBnd.max, Vector2.zero, Vector2.one);
            //output parameters
            var folder0 = AssetDatabase.CreateFolder("Assets", string.Format("{0}", terrainTarget.name));
            folder0 = AssetDatabase.GUIDToAssetPath(folder0);
            var topFulllPath = Application.dataPath + folder0.Substring(folder0.IndexOf("/"));
            var folder1 = "Assets/MeshData";
            if (!AssetDatabase.IsValidFolder(folder1))
            {
                folder1 = AssetDatabase.CreateFolder("Assets", "MeshData");
                folder1 = AssetDatabase.GUIDToAssetPath(folder1);
            }
            var meshFulllPath = Application.dataPath + folder1.Substring(folder1.IndexOf("/"));
            //
            MTData dataHeader = ScriptableObject.CreateInstance<MTData>();
            dataHeader.MeshDataPack = datapack;
            dataHeader.MeshPrefix = terrainTarget.name;
            {
                int packed = 0;
                int start_meshId = -1;
                MemoryStream stream = new MemoryStream();
                for (int i = 0; i < tessellationJob.mesh.Length; ++i)
                {
                    MTMeshData data = tessellationJob.mesh[i];
                    if (!treeRoot.AddMesh(data))
                    {
                        Debug.LogError("mesh can't insert into tree : " + data.meshId);
                    }
                    if (start_meshId < 0)
                        start_meshId = data.meshId;
                    EditorUtility.DisplayProgressBar("saving mesh data", "processing", (float)i / tessellationJob.mesh.Length);
                    if (packed % datapack == 0)
                    {
                        if (stream.Length > 0)
                        {
                            File.WriteAllBytes(string.Format("{0}/{1}_{2}.bytes", meshFulllPath, terrainTarget.name, start_meshId), stream.ToArray());
                            stream.Close();
                            start_meshId = data.meshId;
                            stream = new MemoryStream();
                        }
                        packed = 0;
                        //预写入offset
                        for (int o = 0; o < datapack; ++o)
                        {
                            MTFileUtils.WriteInt(stream, 0);
                        }
                    }
                    //offset
                    int reserve = (int)stream.Position;
                    stream.Position = packed * sizeof(int);
                    MTFileUtils.WriteInt(stream, reserve);
                    stream.Position = reserve;
                    MTMeshUtils.Serialize(stream, data.lods[0]);
                    ++packed;
                }
                //last one
                if (stream.Length > 0 && start_meshId >= 0)
                {
                    File.WriteAllBytes(string.Format("{0}/{1}_{2}.bytes", meshFulllPath, terrainTarget.name, start_meshId), stream.ToArray());
                    stream.Close();
                }
                AssetDatabase.Refresh();
            }
            {
                List<MTQuadTreeNode> nodes = new List<MTQuadTreeNode>();
                MTQuadTreeNode rootNode = new MTQuadTreeNode(0);
                nodes.Add(rootNode);
                ExportTree(treeRoot, rootNode, nodes);
                EditorUtility.DisplayProgressBar("saving tree data", "processing", 0);
                MemoryStream stream = new MemoryStream();
                SerializeTrees(stream, nodes);
                File.WriteAllBytes(string.Format("{0}/treeData.bytes", topFulllPath), stream.ToArray());
                stream.Close();
                AssetDatabase.Refresh();
                dataHeader.TreeData = AssetDatabase.LoadAssetAtPath(string.Format("{0}/treeData.bytes", folder0), typeof(TextAsset)) as TextAsset;
            }
            List<string> detailMats = new List<string>();
            List<string> bakeAlbetoMats = new List<string>();
            List<string> bakeBumpMats = new List<string>();
            //materials for baking resource
            MTMatUtils.SaveMixMaterials(folder0, terrainTarget.name, terrainTarget, detailMats);
            //materials for baking texture
            MTMatUtils.SaveVTMaterials(folder0, terrainTarget.name, terrainTarget, bakeAlbetoMats, bakeBumpMats);
            dataHeader.DetailMats = new Material[detailMats.Count];
            for (int p = 0; p < detailMats.Count; ++p)
            {
                dataHeader.DetailMats[p] = AssetDatabase.LoadAssetAtPath(detailMats[p], typeof(Material)) as Material;
            }
            dataHeader.BakeDiffuseMats = new Material[bakeAlbetoMats.Count];
            for (int p = 0; p < bakeAlbetoMats.Count; ++p)
            {
                dataHeader.BakeDiffuseMats[p] = AssetDatabase.LoadAssetAtPath(bakeAlbetoMats[p], typeof(Material)) as Material;
            }
            dataHeader.BakeNormalMats = new Material[bakeBumpMats.Count];
            for (int p = 0; p < bakeBumpMats.Count; ++p)
            {
                dataHeader.BakeNormalMats[p] = AssetDatabase.LoadAssetAtPath(bakeBumpMats[p], typeof(Material)) as Material;
            }
            //materials for baked texture
            Material bakedMat = new Material(Shader.Find("MT/TerrainVTLit"));
            bakedMat.EnableKeyword("_NORMALMAP");
            var bakedMatPath = string.Format("{0}/BakedMat.mat", folder0);
            AssetDatabase.CreateAsset(bakedMat, bakedMatPath);
            dataHeader.BakedMat = AssetDatabase.LoadAssetAtPath(bakedMatPath, typeof(Material)) as Material;
            //export height map
            ExportHeightMap(dataHeader, curentTarget, topFulllPath, folder0);
            //
            AssetDatabase.CreateAsset(dataHeader, string.Format("{0}/{1}.asset", folder0, terrainTarget.name));
            AssetDatabase.SaveAssets();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }
    }
    private void ExportTree(MTQuadTreeBuildNode root, MTQuadTreeNode node, List<MTQuadTreeNode> nodes)
    {
        if (root != null)
        {
            node.bnd = root.Bound;
            node.meshIdx = root.MeshID;
            node.lodLv = (byte)root.LODLv;
            if (root.SubNode != null)
            {
                node.children = new int[root.SubNode.Length];
                for (int i = 0; i < root.SubNode.Length; ++i)
                {
                    var child = new MTQuadTreeNode(nodes.Count);
                    nodes.Add(child);
                    node.children[i] = child.cellIdx;
                }
                for (int i = 0; i < root.SubNode.Length; ++i)
                {
                    var childIdx = node.children[i];
                    ExportTree(root.SubNode[i], nodes[childIdx], nodes);
                }
            }
        }
    }
    private void SerializeTrees(MemoryStream stream, List<MTQuadTreeNode> nodes)
    {
        MTFileUtils.WriteInt(stream, nodes.Count);
        foreach (var node in nodes)
        {
            node.Serialize(stream);
        }
    }
    private void ExportHeightMap(MTData dataHeader, Terrain curentTarget, string topFulllPath, string folder0)
    {
        EditorUtility.DisplayProgressBar("saving height map", "processing", 0);
        dataHeader.HeightmapResolution = curentTarget.terrainData.heightmapResolution;
        dataHeader.HeightmapScale = curentTarget.terrainData.heightmapScale;
        float[,] heightData = curentTarget.terrainData.GetHeights(0, 0, dataHeader.HeightmapResolution, dataHeader.HeightmapResolution);
        byte[] heightBytes = new byte[dataHeader.HeightmapResolution * dataHeader.HeightmapResolution * 2];
        for (int hy = 0; hy < dataHeader.HeightmapResolution; ++hy)
        {
            for (int hx = 0; hx < dataHeader.HeightmapResolution; ++hx)
            {
                float val = heightData[hy, hx] * 255f;
                byte h = (byte)Mathf.FloorToInt(val);
                byte l = (byte)Mathf.FloorToInt((val - h) * 255f);
                heightBytes[hy * dataHeader.HeightmapResolution * 2 + hx * 2] = h;
                heightBytes[hy * dataHeader.HeightmapResolution * 2 + hx * 2 + 1] = l;
            }
        }
        File.WriteAllBytes(string.Format("{0}/heightMap.bytes", topFulllPath), heightBytes);
        AssetDatabase.Refresh();
        dataHeader.HeightMap = AssetDatabase.LoadAssetAtPath(string.Format("{0}/heightMap.bytes", folder0), typeof(TextAsset)) as TextAsset;
    }
}
