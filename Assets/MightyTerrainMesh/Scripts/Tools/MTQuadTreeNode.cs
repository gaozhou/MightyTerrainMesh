using System;

namespace MightyTerrainMesh
{
    using System.IO;
    using UnityEngine;

    public class MTQuadTreeNode
    {
        public Bounds Bounds;
        public int CellIdx;
        public int MeshIdx = -1;
        public byte LodLevel;
        public int[] Children = Array.Empty<int>();
        private float _diameter;

        public MTQuadTreeNode(int cid)
        {
            CellIdx = cid;
            InnerInit();
        }

        public float PixelSize(Vector3 viewCenter, float fov, float screenH)
        {
            var distance = Vector3.Distance(viewCenter, Bounds.center);
            return _diameter * Mathf.Rad2Deg * screenH / (distance * fov);
        }

        private void InnerInit()
        {
            var horizonSize = Bounds.size;
            horizonSize.y = 0;
            _diameter = horizonSize.magnitude;
        }

        public void Serialize(Stream stream)
        {
            MTFileUtils.WriteVector3(stream, Bounds.center);
            MTFileUtils.WriteVector3(stream, Bounds.size);
            MTFileUtils.WriteInt(stream, MeshIdx);
            MTFileUtils.WriteInt(stream, CellIdx);
            MTFileUtils.WriteByte(stream, LodLevel);
            MTFileUtils.WriteInt(stream, Children.Length);
            foreach (var child in Children)
            {
                MTFileUtils.WriteInt(stream, child);
            }
        }

        public void Deserialize(Stream stream, Vector3 offset)
        {
            var center = MTFileUtils.ReadVector3(stream);
            var size = MTFileUtils.ReadVector3(stream);
            MeshIdx = MTFileUtils.ReadInt(stream);
            CellIdx = MTFileUtils.ReadInt(stream);
            LodLevel = MTFileUtils.ReadByte(stream);
            var len = MTFileUtils.ReadInt(stream);
            Bounds = new Bounds(center + offset, size);
            Children = new int[len];
            for (var i = 0; i < len; ++i)
            {
                Children[i] = MTFileUtils.ReadInt(stream);
            }

            InnerInit();
        }
    }

    public class MTQuadTreeUtil
    {
        public int NodeCount => TreeNodes.Length;

        public Bounds Bound => TreeNodes[0].Bounds;

        public MTArray<MTQuadTreeNode> ActiveNodes => ActiveMeshes;

        public float MinCellSize { get; private set; }
        protected MTQuadTreeNode[] TreeNodes;
        protected MTArray<MTQuadTreeNode> Candidates;
        protected MTArray<MTQuadTreeNode> ActiveMeshes;
        protected MTArray<MTQuadTreeNode> VisibleMeshes;

        public MTQuadTreeUtil(byte[] data, Vector3 offset)
        {
            var stream = new MemoryStream(data);
            var treeLen = MTFileUtils.ReadInt(stream);
            InnerInit(treeLen, stream, offset);
            stream.Close();
        }

        public MTQuadTreeUtil(int treeLen, Stream stream, Vector3 offset)
        {
            InnerInit(treeLen, stream, offset);
        }

        public void InnerInit(int treeLen, Stream stream, Vector3 offset)
        {
            TreeNodes = new MTQuadTreeNode[treeLen];
            MinCellSize = float.MaxValue;
            for (var i = 0; i < treeLen; ++i)
            {
                var node = new MTQuadTreeNode(-1);
                node.Deserialize(stream, offset);
                TreeNodes[i] = node;
                var size = Mathf.Min(node.Bounds.size.x, node.Bounds.size.z);
                if (size < MinCellSize)
                {
                    MinCellSize = size;
                }
            }

            Candidates = new MTArray<MTQuadTreeNode>(TreeNodes.Length);
            ActiveMeshes = new MTArray<MTQuadTreeNode>(TreeNodes.Length);
            VisibleMeshes = new MTArray<MTQuadTreeNode>(TreeNodes.Length);
        }

        public void ResetRuntimeCache()
        {
            Candidates.Reset();
            ActiveMeshes.Reset();
            VisibleMeshes.Reset();
        }

        public void CullQuadtree(Vector3 viewCenter, float fov, float screenH, float screenW, Matrix4x4 world2Cam,
            Matrix4x4 projectMatrix,
            MTArray<MTQuadTreeNode> activeCmd, MTArray<MTQuadTreeNode> deactivateCmd, MTLODPolicy lodPolicy)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(projectMatrix * world2Cam);
            VisibleMeshes.Reset();
            Candidates.Reset();
            Candidates.Add(TreeNodes[0]);
            //此处仅是限制最多循环次数
            var loop = 0;
            var nextStartIdx = 0;
            for (; loop < TreeNodes.Length; ++loop)
            {
                var cIdx = nextStartIdx;
                nextStartIdx = Candidates.Length;
                for (; cIdx < nextStartIdx; ++cIdx)
                {
                    var node = Candidates.Data[cIdx];
                    var stopChild = false;
                    if (node.MeshIdx >= 0)
                    {
                        var pixelSize = node.PixelSize(viewCenter, fov, screenH);
                        var lodLv = lodPolicy.GetLODLevel(pixelSize, screenW);
                        if (node.LodLevel <= lodLv)
                        {
                            VisibleMeshes.Add(node);
                            //此级以下全部隐藏
                            stopChild = true;
                        }
                    }

                    if (stopChild || node.Children.Length <= 0) continue;
                    foreach (var c in node.Children)
                    {
                        var childNode = TreeNodes[c];
                        if (GeometryUtility.TestPlanesAABB(planes, childNode.Bounds))
                        {
                            Candidates.Add(childNode);
                        }
                    }
                }

                if (Candidates.Length == nextStartIdx)
                    break;
            }

            //new cells
            for (var i = 0; i < VisibleMeshes.Length; ++i)
            {
                var meshId = VisibleMeshes.Data[i];
                if (!ActiveMeshes.Contains(meshId))
                {
                    activeCmd.Add(meshId);
                }
            }

            //old cells
            for (var i = 0; i < ActiveMeshes.Length; ++i)
            {
                var meshId = ActiveMeshes.Data[i];
                if (!VisibleMeshes.Contains(meshId))
                {
                    deactivateCmd.Add(meshId);
                }
            }

            (ActiveMeshes, VisibleMeshes) = (VisibleMeshes, ActiveMeshes);
        }
    }

    /// <summary>
    /// utility classes
    /// </summary>
    public sealed class MTQuadTreeBuildNode
    {
        public Bounds Bound;
        public int MeshID = -1;
        public int LODLv = -1;
        public readonly MTQuadTreeBuildNode[] SubNode;
        public Vector2 UVMin;
        public Vector2 UVMax;

        public MTQuadTreeBuildNode(int depth, Vector3 min, Vector3 max, Vector2 uvMin, Vector2 uvMax)
        {
            var center = 0.5f * (min + max);
            var size = max - min;
            var uvCenter = 0.5f * (uvMin + uvMax);
            var uvSize = uvMax - uvMin;
            Bound = new Bounds(center, size);
            UVMin = uvMin;
            UVMax = uvMax;
            if (depth <= 0) return;
            SubNode = new MTQuadTreeBuildNode[4];
            var subMin = new Vector3(center.x - 0.5f * size.x, min.y, center.z - 0.5f * size.z);
            var subMax = new Vector3(center.x, max.y, center.z);
            var uvSubMin = new Vector2(uvCenter.x - 0.5f * uvSize.x, uvCenter.y - 0.5f * uvSize.y);
            var uvSubMax = new Vector2(uvCenter.x, uvCenter.y);
            SubNode[0] = CreateSubNode(depth - 1, subMin, subMax, uvSubMin, uvSubMax);
            subMin = new Vector3(center.x, min.y, center.z - 0.5f * size.z);
            subMax = new Vector3(center.x + 0.5f * size.x, max.y, center.z);
            uvSubMin = new Vector2(uvCenter.x, uvCenter.y - 0.5f * uvSize.y);
            uvSubMax = new Vector2(uvCenter.x + 0.5f * uvSize.x, uvCenter.y);
            SubNode[1] = CreateSubNode(depth - 1, subMin, subMax, uvSubMin, uvSubMax);
            subMin = new Vector3(center.x - 0.5f * size.x, min.y, center.z);
            subMax = new Vector3(center.x, max.y, center.z + 0.5f * size.z);
            uvSubMin = new Vector2(uvCenter.x - 0.5f * uvSize.x, uvCenter.y);
            uvSubMax = new Vector2(uvCenter.x, uvCenter.y + 0.5f * uvSize.y);
            SubNode[2] = CreateSubNode(depth - 1, subMin, subMax, uvSubMin, uvSubMax);
            subMin = new Vector3(center.x, min.y, center.z);
            subMax = new Vector3(center.x + 0.5f * size.x, max.y, center.z + 0.5f * size.z);
            uvSubMin = new Vector2(uvCenter.x, uvCenter.y);
            uvSubMax = new Vector2(uvCenter.x + 0.5f * uvSize.x, uvCenter.y + 0.5f * uvSize.y);
            SubNode[3] = CreateSubNode(depth - 1, subMin, subMax, uvSubMin, uvSubMax);
        }

        private static MTQuadTreeBuildNode CreateSubNode(int depth, Vector3 min, Vector3 max, Vector2 uvMin,
            Vector2 uvMax)
        {
            return new MTQuadTreeBuildNode(depth, min, max, uvMin, uvMax);
        }

        public bool AddMesh(MTMeshData data)
        {
            if (Bound.Contains(data.BND.center) && data.BND.size.x > 0.5f * Bound.size.x)
            {
                MeshID = data.meshId;
                LODLv = data.lodLv;
                data.lods[0].uvmin = UVMin;
                data.lods[0].uvmax = UVMax;
                return true;
            }

            if (SubNode == null) return false;
            for (var i = 0; i < 4; ++i)
            {
                if (SubNode[i].AddMesh(data))
                    return true;
            }

            return false;
        }

        public bool GetBounds(int meshId, ref Bounds bnd)
        {
            switch (SubNode)
            {
                case null when MeshID == meshId:
                    bnd = Bound;
                    return true;
                case null:
                    return false;
            }

            for (var i = 0; i < 4; ++i)
            {
                if (SubNode[i].GetBounds(meshId, ref bnd))
                {
                    return true;
                }
            }

            return false;
        }
    }
}