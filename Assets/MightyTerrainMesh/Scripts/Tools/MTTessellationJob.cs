using System.Collections.Generic;
using UnityEngine;
using TriangleNet.Geometry;

namespace MightyTerrainMesh
{
    //
    public class TessellationJob
    {
        public MTMeshData[] Mesh;
        protected readonly MTTerrainScanner[] Scanners;

        public bool IsDone => CurIdx >= Mesh.Length;

        public float Progress => CurIdx / (float)Mesh.Length;

        public TessellationJob(MTTerrainScanner[] s, float minTriArea)
        {
            Scanners = s;
            MinTriArea = minTriArea;
            Mesh = new MTMeshData[Scanners[0].Trees.Length];
        }

        protected float MinTriArea { get; }
        protected int CurIdx;

        protected void RunTessellation(List<SampleVertexData> lVerts, MTMeshData.LOD lod, float minTriArea)
        {
            if (lVerts.Count < 3)
            {
                ++CurIdx;
                return;
            }

            var geometry = new InputGeometry();
            foreach (var vert in lVerts)
            {
                geometry.AddPoint(vert.Position.x, vert.Position.z, 0);
            }

            var meshRepresentation = new TriangleNet.Mesh();
            meshRepresentation.Triangulate(geometry);
            if (meshRepresentation.Vertices.Count != lVerts.Count)
            {
                Debug.LogError("trianglate seems failed");
            }

            var vIdx = 0;
            lod.vertices = new Vector3[meshRepresentation.Vertices.Count];
            lod.normals = new Vector3[meshRepresentation.Vertices.Count];
            lod.uvs = new Vector2[meshRepresentation.Vertices.Count];
            lod.faces = new int[meshRepresentation.triangles.Count * 3];
            foreach (var v in meshRepresentation.Vertices)
            {
                lod.vertices[vIdx] = new Vector3(v.x, lVerts[vIdx].Position.y, v.y);
                lod.normals[vIdx] = lVerts[vIdx].Normal;
                var uv = lVerts[vIdx].UV;
                lod.uvs[vIdx] = uv;
                ++vIdx;
            }

            vIdx = 0;
            foreach (var t in meshRepresentation.triangles.Values)
            {
                var p = new[]
                {
                    new Vector2(lod.vertices[t.P0].x, lod.vertices[t.P0].z),
                    new Vector2(lod.vertices[t.P1].x, lod.vertices[t.P1].z),
                    new Vector2(lod.vertices[t.P2].x, lod.vertices[t.P2].z)
                };
                var triArea = Mathf.Abs((p[2].x - p[0].x) * (p[1].y - p[0].y) -
                                        (p[1].x - p[0].x) * (p[2].y - p[0].y)) / 2.0f;
                if (triArea < minTriArea)
                    continue;
                lod.faces[vIdx] = t.P2;
                lod.faces[vIdx + 1] = t.P1;
                lod.faces[vIdx + 2] = t.P0;
                vIdx += 3;
            }
        }

        public virtual void Update()
        {
            if (IsDone)
                return;
            Mesh[CurIdx] = new MTMeshData(CurIdx, Scanners[0].Trees[CurIdx].Bounds)
            {
                lods = new MTMeshData.LOD[Scanners.Length]
            };
            for (var lod = 0; lod < Scanners.Length; ++lod)
            {
                var lodData = new MTMeshData.LOD();
                var tree = Scanners[lod].Trees[CurIdx];
                RunTessellation(tree.Vertices, lodData, MinTriArea);
                lodData.uvmin = tree.UVMin;
                lodData.uvmax = tree.UVMax;
                Mesh[CurIdx].lods[lod] = lodData;
            }

            //update idx
            ++CurIdx;
        }
    }

    public class TessellationDataJob : TessellationJob
    {
        private readonly List<SamplerTree> _subTrees = new List<SamplerTree>();
        private readonly List<int> _lodLvArr = new List<int>();

        public TessellationDataJob(MTTerrainScanner[] s, float minTriArea) : base(s, minTriArea)
        {
            var totalLen = 0;
            foreach (var scanner in Scanners)
            {
                totalLen += scanner.Trees.Length;
                _lodLvArr.Add(totalLen);
                _subTrees.AddRange(scanner.Trees);
            }

            Mesh = new MTMeshData[_subTrees.Count];
        }

        private int GetLodLv(int idx)
        {
            for (var i = 0; i < _lodLvArr.Count; ++i)
            {
                if (idx < _lodLvArr[i])
                    return i;
            }

            return 0;
        }

        public override void Update()
        {
            if (IsDone)
                return;
            var lodLv = GetLodLv(CurIdx);
            Mesh[CurIdx] = new MTMeshData(CurIdx, _subTrees[CurIdx].Bounds, lodLv)
            {
                lods = new MTMeshData.LOD[1]
            };
            var lodData = new MTMeshData.LOD();
            var tree = _subTrees[CurIdx];
            RunTessellation(tree.Vertices, lodData, MinTriArea);
            Mesh[CurIdx].lods[0] = lodData;
            //update idx
            ++CurIdx;
        }
    }
}