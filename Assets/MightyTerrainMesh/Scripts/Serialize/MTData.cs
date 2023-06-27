using UnityEngine;

namespace MightyTerrainMesh
{
    public class MTData : ScriptableObject
    {
        public Material[] DetailMats;
        public Material[] BakeDiffuseMats;
        public Material[] BakeNormalMats;
        public Material BakedMat;
        public TextAsset TreeData;
        public int MeshDataPack;
        public string MeshPrefix;
        public TextAsset HeightMap;
        public Vector3 HeightmapScale;
        public int HeightmapResolution;
    }
}