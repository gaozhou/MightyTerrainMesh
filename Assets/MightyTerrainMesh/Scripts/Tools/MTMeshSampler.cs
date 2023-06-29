using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KdTree;

namespace MightyTerrainMesh
{
    public class SampleVertexData
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;

        public void Merge(SampleVertexData other)
        {
            Position = 0.5f * (Position + other.Position);
            Normal = 0.5f * (Normal + other.Normal);
            UV = 0.5f * (UV + other.UV);
        }
    }

    public interface ITerrainTreeScanner
    {
        void Run(Vector3 center, out Vector3 hitPos, out Vector3 hitNormal);
    }

    public abstract class SamplerBase
    {
        public virtual void RunSample(ITerrainTreeScanner scanner)
        {
            scanner.Run(MVertex.Position, out MVertex.Position, out MVertex.Normal);
        }

        protected SampleVertexData MVertex;
        public Dictionary<byte, SampleVertexData> Boundaries = new Dictionary<byte, SampleVertexData>();
        public abstract Vector3 Pos { get; }
        public abstract void GetData(List<SampleVertexData> lPos, Dictionary<byte, List<SampleVertexData>> bd);
        public abstract void AddBoundary(int subdivision, int x, int z, byte bk, SampleVertexData vert);
    }

    public class SamplerLeaf : SamplerBase
    {
        public override Vector3 Pos => MVertex?.Position ?? Vector3.zero;

        public Vector3 Normal => MVertex?.Normal ?? Vector3.up;

        public Vector2 UV => MVertex?.UV ?? Vector2.zero;

        public SamplerLeaf(Vector3 center, Vector2 uv)
        {
            MVertex = new SampleVertexData
            {
                Position = center,
                UV = uv
            };
        }

        public SamplerLeaf(SampleVertexData vert)
        {
            MVertex = vert;
        }

        public override void GetData(List<SampleVertexData> lData, Dictionary<byte, List<SampleVertexData>> bd)
        {
            lData.Add(MVertex);
            foreach (var k in Boundaries.Keys)
            {
                if (!bd.ContainsKey(k))
                    bd.Add(k, new List<SampleVertexData>());
                bd[k].Add(Boundaries[k]);
            }
        }

        public override void AddBoundary(int subdivision, int x, int z, byte bk, SampleVertexData vert)
        {
            Boundaries.Add(bk, vert);
        }
    }

    public class SamplerNode : SamplerBase
    {
        public override Vector3 Pos => MVertex?.Position ?? Vector3.zero;

        private readonly SamplerBase[] _children = new SamplerBase[4];

        public bool IsFullLeaf => _children.All(t => t is SamplerLeaf _);


        //build a full tree
        public SamplerNode(int sub, Vector3 center, Vector2 size, Vector2 uv, Vector2 uvstep)
        {
            MVertex = new SampleVertexData
            {
                Position = center,
                UV = uv
            };
            var subSize = 0.5f * size;
            var subUVStep = 0.5f * uvstep;
            if (sub > 1)
            {
                _children[0] = new SamplerNode(sub - 1,
                    new Vector3(center.x - 0.5f * subSize.x, center.y, center.z - 0.5f * subSize.y), subSize,
                    new Vector2(uv.x - 0.5f * subUVStep.x, uv.y - 0.5f * subUVStep.y), subUVStep);
                _children[1] = new SamplerNode(sub - 1,
                    new Vector3(center.x + 0.5f * subSize.x, center.y, center.z - 0.5f * subSize.y), subSize,
                    new Vector2(uv.x + 0.5f * subUVStep.x, uv.y - 0.5f * subUVStep.y), subUVStep);
                _children[2] = new SamplerNode(sub - 1,
                    new Vector3(center.x - 0.5f * subSize.x, center.y, center.z + 0.5f * subSize.y), subSize,
                    new Vector2(uv.x - 0.5f * subUVStep.x, uv.y + 0.5f * subUVStep.y), subUVStep);
                _children[3] = new SamplerNode(sub - 1,
                    new Vector3(center.x + 0.5f * subSize.x, center.y, center.z + 0.5f * subSize.y), subSize,
                    new Vector2(uv.x + 0.5f * subUVStep.x, uv.y + 0.5f * subUVStep.y), subUVStep);
            }
            else
            {
                _children[0] = new SamplerLeaf(
                    new Vector3(center.x - 0.5f * subSize.x, center.y, center.z - 0.5f * subSize.y),
                    new Vector2(uv.x - 0.5f * subUVStep.x, uv.y - 0.5f * subUVStep.y));
                _children[1] = new SamplerLeaf(
                    new Vector3(center.x + 0.5f * subSize.x, center.y, center.z - 0.5f * subSize.y),
                    new Vector2(uv.x + 0.5f * subUVStep.x, uv.y - 0.5f * subUVStep.y));
                _children[2] = new SamplerLeaf(
                    new Vector3(center.x - 0.5f * subSize.x, center.y, center.z + 0.5f * subSize.y),
                    new Vector2(uv.x - 0.5f * subUVStep.x, uv.y + 0.5f * subUVStep.y));
                _children[3] = new SamplerLeaf(
                    new Vector3(center.x + 0.5f * subSize.x, center.y, center.z + 0.5f * subSize.y),
                    new Vector2(uv.x + 0.5f * subUVStep.x, uv.y + 0.5f * subUVStep.y));
            }
        }

        public override void GetData(List<SampleVertexData> lData, Dictionary<byte, List<SampleVertexData>> bd)
        {
            for (var i = 0; i < 4; ++i)
            {
                _children[i].GetData(lData, bd);
            }

            foreach (var k in Boundaries.Keys)
            {
                if (!bd.ContainsKey(k))
                    bd.Add(k, new List<SampleVertexData>());
                bd[k].Add(Boundaries[k]);
            }
        }

        public override void RunSample(ITerrainTreeScanner scanner)
        {
            base.RunSample(scanner);
            for (int i = 0; i < 4; ++i)
            {
                _children[i].RunSample(scanner);
            }
        }

        public override void AddBoundary(int subdivision, int x, int z, byte bk, SampleVertexData point)
        {
            //first grade
            var u = x >> subdivision; // x / power(2, subdivision);
            var v = z >> subdivision;
            var subX = x - u * (1 << subdivision);
            var subZ = z - v * (1 << subdivision);
            --subdivision;
            var idx = (subZ >> subdivision) * 2 + (subX >> subdivision);
            _children[idx].AddBoundary(subdivision, subX, subZ, bk, point);
        }

        public SamplerLeaf Combine(float angleErr)
        {
            if (_children.Any(t => !(t is SamplerLeaf _)))
            {
                return null;
            }

            foreach (var t in _children)
            {
                var l = (SamplerLeaf)t;
                var dot = Vector3.Dot(l.Normal.normalized, MVertex.Normal.normalized);
                if (Mathf.Rad2Deg * Mathf.Acos(dot) >= angleErr)
                    return null;
            }

            var leaf = new SamplerLeaf(MVertex);
            foreach (var t in _children)
            {
                var l = (SamplerLeaf)t;
                foreach (var k in l.Boundaries.Keys)
                {
                    if (Boundaries.TryGetValue(k, out var boundary))
                        boundary.Merge(l.Boundaries[k]);
                    else
                        Boundaries.Add(k, l.Boundaries[k]);
                }
            }

            leaf.Boundaries = Boundaries;
            return leaf;
        }

        public void CombineNode(float angleErr)
        {
            for (var i = 0; i < 4; ++i)
            {
                if (!(_children[i] is SamplerNode)) continue;
                var subNode = (SamplerNode)_children[i];
                subNode.CombineNode(angleErr);
                if (!subNode.IsFullLeaf) continue;
                var replacedLeaf = subNode.Combine(angleErr);
                if (replacedLeaf != null)
                    _children[i] = replacedLeaf;
            }
        }
    }

    public class SamplerTree
    {
        public const byte LBCorner = 0;
        public const byte LTCorner = 1;
        public const byte RTCorner = 2;
        public const byte RBCorner = 3;
        public const byte BBorder = 4;
        public const byte TBorder = 5;
        public const byte LBorder = 6;
        public const byte RBorder = 7;
        private SamplerBase _node;
        public readonly List<SampleVertexData> Vertices = new List<SampleVertexData>();

        public readonly Dictionary<byte, List<SampleVertexData>> Boundaries =
            new Dictionary<byte, List<SampleVertexData>>();

        public readonly HashSet<byte> StitchedBorders = new HashSet<byte>();

        public Vector3 Center => _node.Pos;

        public Bounds Bounds { get; set; }
        public Vector2 UVMin;
        public Vector2 UVMax;

        private readonly Dictionary<byte, KdTree<float, int>> _boundaryKdTree =
            new Dictionary<byte, KdTree<float, int>>();

        public SamplerTree(int sub, Vector3 center, Vector2 size, Vector2 uv, Vector2 uvStep)
        {
            _node = new SamplerNode(sub, center, size, uv, uvStep);
            UVMin = uv - 0.5f * uvStep;
            UVMax = uv + 0.5f * uvStep;
        }

        private void CombineTree(float angleErr)
        {
            if (!(_node is SamplerNode node)) return;
            node.CombineNode(angleErr);
            if (!node.IsFullLeaf) return;
            var leaf = node.Combine(angleErr);
            if (leaf != null)
                _node = leaf;
        }

        public void AddBoundary(int subdivision, int x, int z, byte bk, SampleVertexData vert)
        {
            if (!(_node is SamplerNode node)) return;
            node.AddBoundary(subdivision, x, z, bk, vert);
        }

        public void InitBoundary()
        {
            for (var flag = LBCorner; flag <= RBorder; ++flag)
            {
                Boundaries.Add(flag, new List<SampleVertexData>());
                var tree = new KdTree<float, int>(2, new KdTree.Math.FloatMath());
                _boundaryKdTree.Add(flag, tree);
            }
        }

        public void MergeBoundary(byte flag, float minDis, List<SampleVertexData> src)
        {
            if (!Boundaries.ContainsKey(flag) || !_boundaryKdTree.ContainsKey(flag))
            {
                Debug.LogError("the boundary need to merge not exists");
            }

            var tree = _boundaryKdTree[flag];
            foreach (var vt in src)
            {
                var nodes = tree.GetNearestNeighbours(new[] { vt.Position.x, vt.Position.z }, 1);
                if (nodes != null && nodes.Length > 0)
                {
                    var dis = Vector2.Distance(new Vector2(vt.Position.x, vt.Position.z),
                        new Vector2(nodes[0].Point[0], nodes[0].Point[1]));
                    if (dis <= minDis)
                        continue;
                }

                tree.Add(new[] { vt.Position.x, vt.Position.z }, 0);
                Boundaries[flag].Add(vt);
            }
        }

        public void RunSampler(ITerrainTreeScanner scanner)
        {
            _node.RunSample(scanner);
        }

        public void FillData(float angleErr)
        {
            if (angleErr > 0)
            {
                CombineTree(angleErr);
            }

            _node.GetData(Vertices, Boundaries);
        }

        public void StitchBorder(byte flag, byte nFlag, float minDis, SamplerTree neighbour)
        {
            if (neighbour == null)
                return;
            if (flag <= RBCorner || nFlag <= RBCorner)
            {
                return;
            }

            if (!Boundaries.ContainsKey(flag))
            {
                MTLog.LogError("SamplerTree boundary doesn't contains corner : " + flag);
                return;
            }

            if (!neighbour.Boundaries.ContainsKey(nFlag))
            {
                MTLog.LogError("SamplerTree neighbour boundary doesn't contains corner : " + nFlag);
                return;
            }

            if (StitchedBorders.Contains(flag) && neighbour.StitchedBorders.Contains(nFlag))
                return;
            if (Boundaries[flag].Count > neighbour.Boundaries[nFlag].Count)
            {
                neighbour.Boundaries[nFlag].Clear();
                neighbour.Boundaries[nFlag].AddRange(Boundaries[flag]);
            }
            else
            {
                Boundaries[flag].Clear();
                Boundaries[flag].AddRange(neighbour.Boundaries[nFlag]);
            }

            //
            StitchedBorders.Add(flag);
            neighbour.StitchedBorders.Add(nFlag);
        }
    }
}