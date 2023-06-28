using UnityEngine;
using UnityEngine.Serialization;

namespace MightyTerrainMesh
{
    public class MTData : ScriptableObject
    {
        [FormerlySerializedAs("DetailMats")] public Material[] detailMats;
        [FormerlySerializedAs("BakeDiffuseMats")] public Material[] bakeDiffuseMats;
        [FormerlySerializedAs("BakeNormalMats")] public Material[] bakeNormalMats;
        [FormerlySerializedAs("BakedMat")] public Material bakedMat;
        [FormerlySerializedAs("TreeData")] public TextAsset treeData;
        [FormerlySerializedAs("MeshDataPack")] public int meshDataPack;
        [FormerlySerializedAs("MeshPrefix")] public string meshPrefix;
        [FormerlySerializedAs("HeightMap")] public TextAsset heightMap;
        [FormerlySerializedAs("HeightmapScale")] public Vector3 heightmapScale;
        [FormerlySerializedAs("HeightmapResolution")] public int heightmapResolution;
    }
}