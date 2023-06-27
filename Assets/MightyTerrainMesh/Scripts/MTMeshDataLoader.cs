using UnityEngine;

namespace MightyTerrainMesh
{
    public interface IMeshDataLoader
    {
        byte[] LoadMeshData(string path);
        void UnloadAsset(string path);
    }

    public class MeshDataResLoader : IMeshDataLoader
    {
        public byte[] LoadMeshData(string path)
        {
            return (Resources.Load($"MeshData/{path}") as TextAsset).bytes;
        }

        public void UnloadAsset(string path)
        {
        }
    }
}