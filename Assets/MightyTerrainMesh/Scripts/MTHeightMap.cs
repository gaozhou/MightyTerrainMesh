using UnityEngine;
using System.Collections.Generic;

namespace MightyTerrainMesh
{
    public class MTHeightMap
    {
        //static interface
        private static readonly Dictionary<uint, MTHeightMap> DictMaps = new Dictionary<uint, MTHeightMap>();
        private static int _mapWidth = 512;
        private static int _mapHeight = 512;
        private static float _halfRange;

        private static uint FormatId(Vector3 pos)
        {
            //transform to (0 ~ short.MaxValue * _mapWidth)
            var x = Mathf.CeilToInt(pos.x + _halfRange) / _mapWidth;
            var y = Mathf.CeilToInt(pos.z + _halfRange) / _mapHeight;
            var id = (uint)x;
            id = (id << 16) | (uint)y;
            return id;
        }

        private static void RegisterMap(MTHeightMap map)
        {
            var width = Mathf.FloorToInt(map.Bounds.size.x);
            var height = Mathf.FloorToInt(map.Bounds.size.z);
            if (DictMaps.Count == 0)
            {
                _mapWidth = width;
                _mapHeight = height;
                _halfRange = Mathf.Max(_mapWidth, _mapHeight) * short.MaxValue;
            }

            if (_mapWidth != width || _mapHeight != height)
            {
                Debug.LogError($"height map size is not valid : {width}, {height}");
                return;
            }

            var id = FormatId(map.Bounds.min);
            //Debug.Log(map.BND.min + ", " + id);
            if (DictMaps.ContainsKey(id))
            {
                Debug.LogError($"height map id overlapped : {map.Bounds.min.x}, {map.Bounds.min.z}");
                return;
            }

            DictMaps.Add(id, map);
        }

        public static void UnregisterMap(MTHeightMap map)
        {
            var id = FormatId(map.Bounds.min);
            if (!DictMaps.ContainsKey(id))
            {
                Debug.LogError($"height map not exist : {map.Bounds.center.x}, {map.Bounds.center.z}");
                return;
            }

            DictMaps.Remove(id);
        }

        public static bool GetHeightInterpolated(Vector3 pos, ref float h)
        {
            var id = FormatId(pos);
            return DictMaps.ContainsKey(id) && DictMaps[id].GetInterpolatedHeight(pos, ref h);
        }

        public static bool GetHeightSimple(Vector3 pos, ref float h)
        {
            var id = FormatId(pos);
            return DictMaps.TryGetValue(id, out var map) && map.GetHeight(pos, ref h);
        }

        private Bounds Bounds { get; }
        private readonly int _heightResolution;
        private readonly byte[] _heights;
        private readonly Vector3 _heightScale;

        public MTHeightMap(Bounds bounds, int resolution, Vector3 scale, byte[] data)
        {
            Bounds = bounds;
            _heightResolution = resolution;
            _heightScale = scale;
            _heights = data;
            RegisterMap(this);
        }

        private float SampleHeightMapData(int x, int y)
        {
            var idx = y * _heightResolution * 2 + x * 2;
            var h = _heights[idx];
            var l = _heights[idx + 1];
            return h + l / 255f;
        }

        private float GetInterpolatedHeightVal(Vector3 pos)
        {
            var localX = Mathf.Clamp01((pos.x - Bounds.min.x) / Bounds.size.x) * (_heightResolution - 1);
            var localY = Mathf.Clamp01((pos.z - Bounds.min.z) / Bounds.size.z) * (_heightResolution - 1);
            var x = Mathf.FloorToInt(localX);
            var y = Mathf.FloorToInt(localY);
            var tx = localX - x;
            var ty = localY - y;
            var y00 = SampleHeightMapData(x, y);
            var y10 = SampleHeightMapData(x + 1, y);
            var y01 = SampleHeightMapData(x, y + 1);
            var y11 = SampleHeightMapData(x + 1, y + 1);
            return Mathf.Lerp(Mathf.Lerp(y00, y10, tx), Mathf.Lerp(y01, y11, tx), ty);
        }

        private bool GetInterpolatedHeight(Vector3 pos, ref float h)
        {
            var checkPos = pos;
            checkPos.y = Bounds.center.y;
            if (!Bounds.Contains(checkPos))
                return false;
            var val = GetInterpolatedHeightVal(pos);
            h = val * _heightScale.y / 255f + Bounds.min.y;
            return true;
        }

        private bool GetHeight(Vector3 pos, ref float h)
        {
            var localX = pos.x - Bounds.min.x;
            var localY = pos.z - Bounds.min.z;
            var x = Mathf.FloorToInt(localX);
            var y = Mathf.FloorToInt(localY);
            if (x < 0 || x >= _heightResolution || y < 0 || y >= _heightResolution) return false;
            var val = SampleHeightMapData(x, y) * _heightScale.y / 255f;
            h = val + Bounds.min.y;
            return true;
        }
    }
}