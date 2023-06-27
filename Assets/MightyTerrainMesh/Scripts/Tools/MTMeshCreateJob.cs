using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MightyTerrainMesh
{
    public class MeshLODCreate
    {
        public int Subdivision = 3;
        public float SlopeAngleError = 5f;
    }

    public class CreateMeshJob
    {
        public readonly MTTerrainScanner[] Lods;
        private int _curLodIdx;

        public bool IsDone => _curLodIdx >= Lods.Length;

        public float Progress
        {
            get
            {
                if (_curLodIdx < Lods.Length)
                {
                    return (_curLodIdx + Lods[_curLodIdx].Progress) / Lods.Length;
                }

                return 1;
            }
        }

        public CreateMeshJob(Terrain t, Bounds volumeBound, int mx, int mz, IReadOnlyList<MeshLODCreate> setting)
        {
            Lods = new MTTerrainScanner[setting.Count];
            for (var i = 0; i < setting.Count; ++i)
            {
                var s = setting[i];
                //only first lod stitch borders, other lod use the most detailed border to avoid 
                //tearing on the border
                Lods[i] = new MTTerrainScanner(t, volumeBound, s.Subdivision, s.SlopeAngleError, mx, mz,
                    i == 0);
            }
        }

        public void Update()
        {
            if (Lods == null || IsDone)
                return;
            Lods[_curLodIdx].Update();
            if (Lods[_curLodIdx].IsDone)
                ++_curLodIdx;
        }

        public void EndProcess()
        {
            //copy borders
            var detail = Lods[0];
            detail.FillData();
            for (var i = 1; i < Lods.Length; ++i)
            {
                var scanner = Lods[i];
                for (var t = 0; t < detail.Trees.Length; ++t)
                {
                    var dt = detail.Trees[t];
                    var lt = scanner.Trees[t];
                    foreach (var b in dt.Boundaries)
                    {
                        lt.Boundaries.Add(b.Key, b.Value);
                    }
                }

                scanner.FillData();
            }
        }
    }

    public class CreateDataJob
    {
        public readonly MTTerrainScanner[] Lods;
        private readonly float _minEdgeLen;
        private int _curLodIdx;

        public bool IsDone => _curLodIdx >= Lods.Length;

        public float Progress
        {
            get
            {
                if (_curLodIdx < Lods.Length)
                {
                    return (_curLodIdx + Lods[_curLodIdx].Progress) / Lods.Length;
                }

                return 1;
            }
        }

        public CreateDataJob(Terrain t, Bounds volumeBound, int depth, IReadOnlyList<MeshLODCreate> setting,
            float minEdge)
        {
            Lods = new MTTerrainScanner[setting.Count];
            _minEdgeLen = minEdge;
            var depthStride = Mathf.Max(1, depth / setting.Count);
            for (var i = 0; i < setting.Count; ++i)
            {
                var subDiv = setting[i].Subdivision;
                var angleErr = setting[i].SlopeAngleError;
                var subDepth = Mathf.Max(1, depth - i * depthStride);
                var gridCount = 1 << subDepth;
                //use last lod stitch borders to avoid tearing on the border
                Lods[i] = new MTTerrainScanner(t, volumeBound, subDiv, angleErr, gridCount, gridCount, i == 0);
            }
        }

        public void Update()
        {
            if (Lods == null || IsDone)
                return;
            Lods[_curLodIdx].Update();
            if (Lods[_curLodIdx].IsDone)
                ++_curLodIdx;
        }

        public void EndProcess()
        {
            Lods[0].FillData();
            for (var i = 1; i < Lods.Length; ++i)
            {
                //copy borders
                var detail = Lods[i - 1];
                var scanner = Lods[i];
                foreach (var t in scanner.Trees)
                {
                    t.InitBoundary();
                    //Debug.Log("start collect boundary : ");
                    foreach (var dt in detail.Trees)
                    {
                        if (t.Bounds.Contains(dt.Bounds.center))
                        {
                            AddBoundaryFromDetail(t, dt, _minEdgeLen / 2f);
                        }
                    }
                }

                scanner.FillData();
            }
        }

        private byte GetBorderType(Bounds container, Bounds child)
        {
            var type = byte.MaxValue;
            var lBorder = container.center.x - container.extents.x;
            var rBorder = container.center.x + container.extents.x;
            var lChildBorder = child.center.x - child.extents.x;
            var rChildBorder = child.center.x + child.extents.x;
            if (Mathf.Abs(lBorder - lChildBorder) < 0.01f)
                type = SamplerTree.LBorder;
            if (Mathf.Abs(rBorder - rChildBorder) < 0.01f)
                type = SamplerTree.RBorder;
            var bBorder = container.center.z - container.extents.z;
            var tBorder = container.center.z + container.extents.z;
            var bChildBorder = child.center.z - child.extents.z;
            var tChildBorder = child.center.z + child.extents.z;
            if (Mathf.Abs(tBorder - tChildBorder) < 0.01f)
            {
                type = type switch
                {
                    SamplerTree.LBorder => SamplerTree.LTCorner,
                    SamplerTree.RBorder => SamplerTree.RTCorner,
                    _ => SamplerTree.TBorder
                };
            }

            if (!(Mathf.Abs(bBorder - bChildBorder) < 0.01f)) return type;
            type = type switch
            {
                SamplerTree.LBorder => SamplerTree.LBCorner,
                SamplerTree.RBorder => SamplerTree.RBCorner,
                _ => SamplerTree.BBorder
            };

            return type;
        }

        private void AddBoundaryFromDetail(SamplerTree container, SamplerTree detail, float minDis)
        {
            var bt = GetBorderType(container.Bounds, detail.Bounds);
            //Debug.Log("detail type : " + bt);
            switch (bt)
            {
                case SamplerTree.LBorder:
                    container.MergeBoundary(SamplerTree.LBorder, minDis, detail.Boundaries[SamplerTree.LTCorner]);
                    container.MergeBoundary(SamplerTree.LBorder, minDis, detail.Boundaries[SamplerTree.LBorder]);
                    container.MergeBoundary(SamplerTree.LBorder, minDis, detail.Boundaries[SamplerTree.LBCorner]);
                    break;
                case SamplerTree.LTCorner:
                    container.MergeBoundary(SamplerTree.TBorder, minDis, detail.Boundaries[SamplerTree.TBorder]);
                    container.MergeBoundary(SamplerTree.TBorder, minDis, detail.Boundaries[SamplerTree.RTCorner]);
                    container.MergeBoundary(SamplerTree.LTCorner, minDis, detail.Boundaries[SamplerTree.LTCorner]);
                    container.MergeBoundary(SamplerTree.LBorder, minDis, detail.Boundaries[SamplerTree.LBorder]);
                    container.MergeBoundary(SamplerTree.LBorder, minDis, detail.Boundaries[SamplerTree.LBCorner]);
                    break;
                case SamplerTree.LBCorner:
                    container.MergeBoundary(SamplerTree.BBorder, minDis, detail.Boundaries[SamplerTree.BBorder]);
                    container.MergeBoundary(SamplerTree.BBorder, minDis, detail.Boundaries[SamplerTree.RBCorner]);
                    container.MergeBoundary(SamplerTree.LBCorner, minDis, detail.Boundaries[SamplerTree.LBCorner]);
                    container.MergeBoundary(SamplerTree.LBorder, minDis, detail.Boundaries[SamplerTree.LBorder]);
                    container.MergeBoundary(SamplerTree.LBorder, minDis, detail.Boundaries[SamplerTree.LTCorner]);
                    break;
                case SamplerTree.BBorder:
                    container.MergeBoundary(SamplerTree.BBorder, minDis, detail.Boundaries[SamplerTree.BBorder]);
                    container.MergeBoundary(SamplerTree.BBorder, minDis, detail.Boundaries[SamplerTree.LBCorner]);
                    container.MergeBoundary(SamplerTree.BBorder, minDis, detail.Boundaries[SamplerTree.RBCorner]);
                    break;
                case SamplerTree.RBCorner:
                    container.MergeBoundary(SamplerTree.BBorder, minDis, detail.Boundaries[SamplerTree.BBorder]);
                    container.MergeBoundary(SamplerTree.BBorder, minDis, detail.Boundaries[SamplerTree.LBCorner]);
                    container.MergeBoundary(SamplerTree.RBCorner, minDis, detail.Boundaries[SamplerTree.RBCorner]);
                    container.MergeBoundary(SamplerTree.RBorder, minDis, detail.Boundaries[SamplerTree.RBorder]);
                    container.MergeBoundary(SamplerTree.RBorder, minDis, detail.Boundaries[SamplerTree.RTCorner]);
                    break;
                case SamplerTree.RBorder:
                    container.MergeBoundary(SamplerTree.RBorder, minDis, detail.Boundaries[SamplerTree.RTCorner]);
                    container.MergeBoundary(SamplerTree.RBorder, minDis, detail.Boundaries[SamplerTree.RBorder]);
                    container.MergeBoundary(SamplerTree.RBorder, minDis, detail.Boundaries[SamplerTree.RBCorner]);
                    break;
                case SamplerTree.RTCorner:
                    container.MergeBoundary(SamplerTree.TBorder, minDis, detail.Boundaries[SamplerTree.TBorder]);
                    container.MergeBoundary(SamplerTree.TBorder, minDis, detail.Boundaries[SamplerTree.LTCorner]);
                    container.MergeBoundary(SamplerTree.RTCorner, minDis, detail.Boundaries[SamplerTree.RTCorner]);
                    container.MergeBoundary(SamplerTree.RBorder, minDis, detail.Boundaries[SamplerTree.RBorder]);
                    container.MergeBoundary(SamplerTree.RBorder, minDis, detail.Boundaries[SamplerTree.RBCorner]);
                    break;
                case SamplerTree.TBorder:
                    container.MergeBoundary(SamplerTree.TBorder, minDis, detail.Boundaries[SamplerTree.RTCorner]);
                    container.MergeBoundary(SamplerTree.TBorder, minDis, detail.Boundaries[SamplerTree.TBorder]);
                    container.MergeBoundary(SamplerTree.TBorder, minDis, detail.Boundaries[SamplerTree.LTCorner]);
                    break;
            }
        }
    }

    public class MTTerrainScanner : ITerrainTreeScanner
    {
        private int MaxX { get; set; }
        private int MaxZ { get; set; }
        private int SubDivision { get; set; }
        private float SlopeAngleErr { get; set; }
        private Vector2 GridSize { get; set; }
        private readonly int _detailedSize;
        public SamplerTree[] Trees { get; private set; }

        public Vector3 Center => _volBounds.center;

        private int _curXIdx;
        private int _curZIdx;
        private readonly bool _stitchBorder;

        public bool IsDone => _curXIdx >= MaxX && _curZIdx >= MaxZ;

        public float Progress => (float)(_curXIdx + _curZIdx * MaxX) / (MaxX * MaxZ);

        private Bounds _volBounds;
        private readonly Terrain _terrain;
        private readonly Vector3 _checkStart;

        public MTTerrainScanner(Terrain t, Bounds volumeBound, int sub, float angleErr, int mx, int mz,
            bool stitchBorder)
        {
            _terrain = t;
            _volBounds = volumeBound;
            MaxX = mx;
            MaxZ = mz;
            SubDivision = Mathf.Max(1, sub);
            SlopeAngleErr = angleErr;
            _stitchBorder = stitchBorder;
            GridSize = new Vector2(volumeBound.size.x / mx, volumeBound.size.z / mz);

            _checkStart = new Vector3(volumeBound.center.x - volumeBound.size.x / 2,
                volumeBound.center.y + volumeBound.size.y / 2,
                volumeBound.center.z - volumeBound.size.z / 2);
            //
            _detailedSize = 1 << SubDivision;
            //
            Trees = new SamplerTree[MaxX * MaxZ];
        }

        private SamplerTree GetSubTree(int x, int z)
        {
            if (x < 0 || x >= MaxX || z < 0 || z >= MaxZ)
                return null;
            return Trees[x * MaxZ + z];
        }

        void ITerrainTreeScanner.Run(Vector3 center, out Vector3 hitPos, out Vector3 hitNormal)
        {
            hitPos = center;
            var fx = (center.x - _volBounds.min.x) / _volBounds.size.x;
            var fy = (center.z - _volBounds.min.z) / _volBounds.size.z;
            hitPos.y = _terrain.SampleHeight(center) + _terrain.gameObject.transform.position.y;
            hitNormal = _terrain.terrainData.GetInterpolatedNormal(fx, fy);
        }

        private void ScanTree(SamplerTree sampler)
        {
            sampler.RunSampler(this);
            if (!_stitchBorder)
                return;
            var detailedX = _curXIdx * _detailedSize;
            var detailedZ = _curZIdx * _detailedSize;
            //boundary
            var bfx = _curXIdx * GridSize[0];
            var bfz = _curZIdx * GridSize[1];
            float borderOffset = 0;
            if (_curXIdx == 0 || _curZIdx == 0 || _curXIdx == MaxX - 1 || _curZIdx == MaxZ - 1)
                borderOffset = 0.000001f;
            RayCastBoundary(bfx + borderOffset, bfz + borderOffset,
                detailedX, detailedZ, SamplerTree.LBCorner, sampler);
            RayCastBoundary(bfx + borderOffset, bfz + GridSize[1] - borderOffset,
                detailedX, detailedZ + _detailedSize - 1, SamplerTree.LTCorner, sampler);
            RayCastBoundary(bfx + GridSize[0] - borderOffset, bfz + GridSize[1] - borderOffset,
                detailedX + _detailedSize - 1, detailedZ + _detailedSize - 1, SamplerTree.RTCorner, sampler);
            RayCastBoundary(bfx + GridSize[0] - borderOffset, bfz + borderOffset,
                detailedX + _detailedSize - 1, detailedZ, SamplerTree.RBCorner, sampler);
            for (var u = 1; u < _detailedSize; ++u)
            {
                var fx = (_curXIdx + (float)u / _detailedSize) * GridSize[0];
                RayCastBoundary(fx, bfz + borderOffset, u + detailedX, detailedZ, SamplerTree.BBorder, sampler);
                RayCastBoundary(fx, bfz + GridSize[1] - borderOffset,
                    u + detailedX, detailedZ + _detailedSize - 1, SamplerTree.TBorder, sampler);
            }

            for (var v = 1; v < _detailedSize; ++v)
            {
                var fz = (_curZIdx + (float)v / _detailedSize) * GridSize[1];
                RayCastBoundary(bfx + borderOffset, fz, detailedX, v + detailedZ, SamplerTree.LBorder, sampler);
                RayCastBoundary(bfx + GridSize[0] - borderOffset, fz,
                    detailedX + _detailedSize - 1, v + detailedZ, SamplerTree.RBorder, sampler);
            }
        }

        private void RayCastBoundary(float fx, float fz, int x, int z, byte bk, SamplerTree sampler)
        {
            var hitPos = _checkStart + fx * Vector3.right + fz * Vector3.forward;
            hitPos.x = Mathf.Clamp(hitPos.x, _volBounds.min.x, _volBounds.max.x);
            hitPos.z = Mathf.Clamp(hitPos.z, _volBounds.min.z, _volBounds.max.z);

            var localX = (hitPos.x - _volBounds.min.x) / _volBounds.size.x;
            var localY = (hitPos.z - _volBounds.min.z) / _volBounds.size.z;
            hitPos.y = _terrain.SampleHeight(hitPos) + _terrain.gameObject.transform.position.y;
            var hitNormal = _terrain.terrainData.GetInterpolatedNormal(localX, localY);

            var vert = new SampleVertexData
            {
                Position = hitPos,
                Normal = hitNormal,
                UV = new Vector2(fx / MaxX / GridSize[0], fz / MaxZ / GridSize[1])
            };
            sampler.AddBoundary(SubDivision, x, z, bk, vert);
        }

        public void Update()
        {
            if (IsDone)
                return;
            var fx = (_curXIdx + 0.5f) * GridSize[0];
            var fz = (_curZIdx + 0.5f) * GridSize[1];
            var center = _checkStart + fx * Vector3.right + fz * Vector3.forward;
            var uv = new Vector2((_curXIdx + 0.5f) / MaxX, (_curZIdx + 0.5f) / MaxZ);
            var uvStep = new Vector2(1f / MaxX, 1f / MaxZ);
            if (Trees[_curXIdx * MaxZ + _curZIdx] == null)
            {
                var t = new SamplerTree(SubDivision, center, GridSize, uv, uvStep)
                {
                    Bounds = new Bounds(new Vector3(center.x, center.y, center.z),
                        new Vector3(GridSize.x, _volBounds.size.y / 2, GridSize.y))
                };
                Trees[_curXIdx * MaxZ + _curZIdx] = t;
            }

            ScanTree(Trees[_curXIdx * MaxZ + _curZIdx]);
            //update idx
            ++_curXIdx;
            if (_curXIdx < MaxX) return;
            if (_curZIdx < MaxZ - 1)
                _curXIdx = 0;
            ++_curZIdx;
        }

        private static Vector3 AverageNormal(IEnumerable<SampleVertexData> layers)
        {
            var normal = layers.Aggregate(Vector3.up, (current, t) => current + t.Normal);

            return normal.normalized;
        }

        private void MergeCorners(IReadOnlyList<SampleVertexData> l0, IReadOnlyList<SampleVertexData> l1,
            IReadOnlyList<SampleVertexData> l2,
            IReadOnlyList<SampleVertexData> l3)
        {
            var layers = new List<SampleVertexData>
            {
                //lb
                l0[0]
            };
            if (l1 != null)
                layers.Add(l1[0]);
            if (l2 != null)
                layers.Add(l2[0]);
            if (l3 != null)
                layers.Add(l3[0]);
            var normal = AverageNormal(layers);
            l0[0].Normal = normal;
            if (l1 != null)
                l1[0].Normal = normal;
            if (l2 != null)
                l2[0].Normal = normal;
            if (l3 != null)
                l3[0].Normal = normal;
        }

        private void StitchCorner(int x, int z)
        {
            var center = GetSubTree(x, z);
            if (!center.Boundaries.ContainsKey(SamplerTree.LBCorner))
            {
                MTLog.LogError("boundary data missing");
                return;
            }

            var right = GetSubTree(x + 1, z);
            var left = GetSubTree(x - 1, z);
            var rightTop = GetSubTree(x + 1, z + 1);
            var top = GetSubTree(x, z + 1);
            var leftTop = GetSubTree(x - 1, z + 1);
            var leftDown = GetSubTree(x - 1, z - 1);
            var down = GetSubTree(x, z - 1);
            var rightDown = GetSubTree(x + 1, z - 1);
            if (!center.StitchedBorders.Contains(SamplerTree.LBCorner))
            {
                MergeCorners(center.Boundaries[SamplerTree.LBCorner],
                    left?.Boundaries[SamplerTree.RBCorner],
                    leftDown?.Boundaries[SamplerTree.RTCorner],
                    down?.Boundaries[SamplerTree.LTCorner]);
                center.StitchedBorders.Add(SamplerTree.LBCorner);
                left?.StitchedBorders.Add(SamplerTree.RBCorner);
                leftDown?.StitchedBorders.Add(SamplerTree.RTCorner);
                if (down != null)
                {
                    left?.StitchedBorders.Add(SamplerTree.LTCorner);
                }
            }

            if (!center.StitchedBorders.Contains(SamplerTree.RBCorner))
            {
                MergeCorners(center.Boundaries[SamplerTree.RBCorner],
                    right?.Boundaries[SamplerTree.LBCorner],
                    rightDown?.Boundaries[SamplerTree.LTCorner],
                    down?.Boundaries[SamplerTree.RTCorner]);
                center.StitchedBorders.Add(SamplerTree.RBCorner);
                right?.StitchedBorders.Add(SamplerTree.LBCorner);
                rightDown?.StitchedBorders.Add(SamplerTree.LTCorner);
                down?.StitchedBorders.Add(SamplerTree.RTCorner);
            }

            if (!center.StitchedBorders.Contains(SamplerTree.LTCorner))
            {
                MergeCorners(center.Boundaries[SamplerTree.LTCorner],
                    left?.Boundaries[SamplerTree.RTCorner],
                    leftTop?.Boundaries[SamplerTree.RBCorner],
                    top?.Boundaries[SamplerTree.LBCorner]);
                center.StitchedBorders.Add(SamplerTree.LTCorner);
                left?.StitchedBorders.Add(SamplerTree.RTCorner);
                leftTop?.StitchedBorders.Add(SamplerTree.RBCorner);
                top?.StitchedBorders.Add(SamplerTree.LBCorner);
            }

            if (!center.StitchedBorders.Contains(SamplerTree.RTCorner))
            {
                MergeCorners(center.Boundaries[SamplerTree.RTCorner],
                    right?.Boundaries[SamplerTree.LTCorner],
                    rightTop?.Boundaries[SamplerTree.LBCorner],
                    top?.Boundaries[SamplerTree.RBCorner]);
                center.StitchedBorders.Add(SamplerTree.RTCorner);
                right?.StitchedBorders.Add(SamplerTree.LTCorner);
                rightTop?.StitchedBorders.Add(SamplerTree.LBCorner);
                top?.StitchedBorders.Add(SamplerTree.RBCorner);
            }
        }

        public void FillData()
        {
            foreach (var t in Trees)
            {
                t.FillData(SlopeAngleErr);
            }

            //stitch the border
            var minDis = Mathf.Min(GridSize.x, GridSize.y) / _detailedSize / 2f;
            for (var x = 0; x < MaxX; ++x)
            {
                for (var z = 0; z < MaxZ; ++z)
                {
                    var centerTree = GetSubTree(x, z);
                    //corners
                    StitchCorner(x, z);
                    //borders
                    centerTree.StitchBorder(SamplerTree.BBorder, SamplerTree.TBorder, minDis, GetSubTree(x, z - 1));
                    centerTree.StitchBorder(SamplerTree.LBorder, SamplerTree.RBorder, minDis, GetSubTree(x - 1, z));
                    centerTree.StitchBorder(SamplerTree.RBorder, SamplerTree.LBorder, minDis, GetSubTree(x + 1, z));
                    centerTree.StitchBorder(SamplerTree.TBorder, SamplerTree.BBorder, minDis, GetSubTree(x, z + 1));
                }
            }

            //merge boundary with verts for tessellation
            foreach (var t in Trees)
            {
                foreach (var l in t.Boundaries.Values)
                    t.Vertices.AddRange(l);
            }
        }
    }
}