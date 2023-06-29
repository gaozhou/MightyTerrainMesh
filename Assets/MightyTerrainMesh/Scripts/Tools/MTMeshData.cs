using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;

namespace MightyTerrainMesh
{
    public static class MTMeshUtils
    {
        public static void Serialize(Stream stream, MTMeshData.LOD lod)
        {
            MTFileUtils.WriteVector2(stream, lod.UVMin);
            MTFileUtils.WriteVector2(stream, lod.UVMax);
            //vertices
            var uBuff = BitConverter.GetBytes(lod.Vertices.Length);
            stream.Write(uBuff, 0, uBuff.Length);
            foreach (var v in lod.Vertices)
                MTFileUtils.WriteVector3(stream, v);
            //normals
            uBuff = BitConverter.GetBytes(lod.Normals.Length);
            stream.Write(uBuff, 0, uBuff.Length);
            foreach (var n in lod.Normals)
                MTFileUtils.WriteVector3(stream, n);
            //uvs
            uBuff = BitConverter.GetBytes(lod.Uvs.Length);
            stream.Write(uBuff, 0, uBuff.Length);
            foreach (var uv in lod.Uvs)
                MTFileUtils.WriteVector2(stream, uv);
            //faces
            uBuff = BitConverter.GetBytes(lod.Faces.Length);
            stream.Write(uBuff, 0, uBuff.Length);
            foreach (var face in lod.Faces)
            {
                //强转为ushort
                var val = (ushort)face;
                uBuff = BitConverter.GetBytes(val);
                stream.Write(uBuff, 0, uBuff.Length);
            }
        }

        public static void Deserialize(Stream stream, MTRenderMesh rm)
        {
            rm.Mesh = new Mesh();
            rm.UVMin = MTFileUtils.ReadVector2(stream);
            rm.UVMax = MTFileUtils.ReadVector2(stream);
            //vertices
            var vec3Cache = new List<Vector3>();
            var nBuff = new byte[sizeof(int)];
            stream.Read(nBuff, 0, sizeof(int));
            var len = BitConverter.ToInt32(nBuff, 0);
            for (var i = 0; i < len; ++i)
                vec3Cache.Add(MTFileUtils.ReadVector3(stream));
            rm.Mesh.SetVertices(vec3Cache.ToArray());
            //normals
            vec3Cache.Clear();
            stream.Read(nBuff, 0, sizeof(int));
            len = BitConverter.ToInt32(nBuff, 0);
            for (var i = 0; i < len; ++i)
                vec3Cache.Add(MTFileUtils.ReadVector3(stream));
            rm.Mesh.SetNormals(vec3Cache.ToArray());
            //uvs
            var vec2Cache = new List<Vector2>();
            stream.Read(nBuff, 0, sizeof(int));
            len = BitConverter.ToInt32(nBuff, 0);
            for (var i = 0; i < len; ++i)
                vec2Cache.Add(MTFileUtils.ReadVector2(stream));
            rm.Mesh.SetUVs(0, vec2Cache.ToArray());
            //faces
            var intCache = new List<int>();
            stream.Read(nBuff, 0, sizeof(int));
            len = BitConverter.ToInt32(nBuff, 0);
            var fBuff = new byte[sizeof(ushort)];
            for (var i = 0; i < len; ++i)
            {
                stream.Read(fBuff, 0, sizeof(ushort));
                intCache.Add(BitConverter.ToUInt16(fBuff, 0));
            }

            rm.Mesh.SetTriangles(intCache.ToArray(), 0);
        }
    }

    public class MTMeshData
    {
        public class LOD
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public int[] Faces;
            public Vector2 UVMin;
            public Vector2 UVMax;
        }

        public int MeshId { get; private set; }
        public Bounds Bounds { get; private set; }
        public LOD[] LODS;
        public readonly int LodLevel = -1;

        public MTMeshData(int id, Bounds bounds)
        {
            MeshId = id;
            Bounds = bounds;
        }

        public MTMeshData(int id, Bounds bounds, int level)
        {
            MeshId = id;
            Bounds = bounds;
            LodLevel = level;
        }
    }
}