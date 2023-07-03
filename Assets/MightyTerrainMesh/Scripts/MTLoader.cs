using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace MightyTerrainMesh
{
    internal class MTRuntimeMeshPool
    {
        private class MeshStreamCache
        {
            public string Path { get; }

            public bool Obsolete => _offsets == null || _usedCount == _offsets.Length;

            private readonly MemoryStream _memStream;
            private readonly int[] _offsets;
            private int _usedCount;

            public MeshStreamCache(string path, int pack, byte[] data)
            {
                Path = path;
                _memStream = new MemoryStream(data);
                _offsets = new int[pack];
                for (var i = 0; i < pack; ++i)
                {
                    _offsets[i] = MTFileUtils.ReadInt(_memStream);
                }
            }

            public MTRenderMesh GetMesh(int meshId)
            {
                var offsetStride = meshId % _offsets.Length;
                var offset = _offsets[offsetStride];
                var rm = new MTRenderMesh();
                _memStream.Position = offset;
                MTMeshUtils.Deserialize(_memStream, rm);
                ++_usedCount;
                return rm;
            }

            public void Clear()
            {
                _memStream.Close();
            }
        }

        private readonly MTData _rawData;

        private readonly Dictionary<int, MTRenderMesh> _parsedMesh = new Dictionary<int, MTRenderMesh>();

        //memory data cache, once all data parsed into mesh, it will be destroied
        private readonly Dictionary<string, MeshStreamCache> _dataStreams = new Dictionary<string, MeshStreamCache>();
        private readonly IMeshDataLoader _loader;

        public MTRuntimeMeshPool(MTData data, IMeshDataLoader ld)
        {
            _rawData = data;
            _loader = ld;
        }

        public MTRenderMesh PopMesh(int meshId)
        {
            if (!_parsedMesh.ContainsKey(meshId) && meshId >= 0)
            {
                var startMeshId = meshId / _rawData.meshDataPack * _rawData.meshDataPack;
                var path = $"{_rawData.meshPrefix}_{startMeshId}";
                if (!_dataStreams.ContainsKey(path))
                {
                    var meshBytes = _loader.LoadMeshData(path);
                    var cache = new MeshStreamCache(path, _rawData.meshDataPack, meshBytes);
                    _dataStreams.Add(path, cache);
                }

                var streamCache = _dataStreams[path];
                var rm = streamCache.GetMesh(meshId);
                _parsedMesh.Add(meshId, rm);
                if (streamCache.Obsolete)
                {
                    _dataStreams.Remove(streamCache.Path);
                    _loader.UnloadAsset(streamCache.Path);
                    streamCache.Clear();
                }
            }

            return _parsedMesh.TryGetValue(meshId, out var mesh) ? mesh : null;
        }

        public void Clear()
        {
            foreach (var cache in _dataStreams.Values)
            {
                _loader.UnloadAsset(cache.Path);
                cache.Clear();
            }

            _dataStreams.Clear();
            foreach (var m in _parsedMesh.Values)
            {
                m.Clear();
            }

            _parsedMesh.Clear();
        }
    }

    public class MTLoader : MonoBehaviour
    {
        public MTData header;
        public MTLODPolicy lodPolicy;
        public Camera cullCamera;
        [FormerlySerializedAs("VTCreatorGo")] public GameObject vtCreatorGo;

        //
        private MTRuntimeMeshPool _meshPool;
        private MTQuadTreeUtil _quadtree;
        private MTHeightMap _heightMap;
        private MTArray<MTQuadTreeNode> _activeCmd;
        private MTArray<MTQuadTreeNode> _deactivateCmd;

        private readonly Dictionary<int, ImtPooledRenderMesh>
            _activeMeshes = new Dictionary<int, ImtPooledRenderMesh>();

        private IVTCreator _vtCreator;
        private Matrix4x4 _projM;
        private Matrix4x4 _prevWorld2Cam;
        private bool _init;

        private void ActiveMesh(MTQuadTreeNode node)
        {
            var patch = ImtPooledRenderMesh.Pop();
            var m = _meshPool.PopMesh(node.MeshIdx);
            patch.Reset(header, _vtCreator, m, transform.position);
            _activeMeshes.Add(node.MeshIdx, patch);
        }

        private void DeactivateMesh(MTQuadTreeNode node)
        {
            var p = _activeMeshes[node.MeshIdx];
            _activeMeshes.Remove(node.MeshIdx);
            ImtPooledRenderMesh.Push(p);
        }

        private void Awake()
        {
            if (vtCreatorGo)
                Init(vtCreatorGo.GetComponent<IVTCreator>());
            RenderPipelineManager.beginFrameRendering += OnFrameRendering;
        }

        public void Init(IVTCreator creator)
        {
            if (_init)
                return;
            IMeshDataLoader loader = new MeshDataResLoader();
            _quadtree = new MTQuadTreeUtil(header.treeData.bytes, transform.position);
            _heightMap = new MTHeightMap(_quadtree.Bound, header.heightmapResolution, header.heightmapScale,
                header.heightMap.bytes);
            _activeCmd = new MTArray<MTQuadTreeNode>(_quadtree.NodeCount);
            _deactivateCmd = new MTArray<MTQuadTreeNode>(_quadtree.NodeCount);
            _meshPool = new MTRuntimeMeshPool(header, loader);
            _vtCreator = creator;
            _prevWorld2Cam = Matrix4x4.identity;
            _init = true;
        }

        private void OnFrameRendering(ScriptableRenderContext context, Camera[] cameras)
        {
            if (_quadtree == null || cullCamera == null) return;
            var world2Cam = cullCamera.worldToCameraMatrix;
            if (_prevWorld2Cam == world2Cam) return;
            _projM = Matrix4x4.Perspective(cullCamera.fieldOfView, cullCamera.aspect, cullCamera.nearClipPlane,
                cullCamera.farClipPlane);
            _prevWorld2Cam = world2Cam;
            _activeCmd.Reset();
            _deactivateCmd.Reset();
            _quadtree.CullQuadtree(cullCamera.transform.position, cullCamera.fieldOfView, Screen.height,
                Screen.width, world2Cam, _projM,
                _activeCmd, _deactivateCmd, lodPolicy);
            for (var i = 0; i < _activeCmd.Length; ++i)
            {
                ActiveMesh(_activeCmd.Data[i]);
            }

            for (var i = 0; i < _deactivateCmd.Length; ++i)
            {
                DeactivateMesh(_deactivateCmd.Data[i]);
            }

            if (_quadtree.ActiveNodes.Length <= 0) return;
            for (var i = 0; i < _quadtree.ActiveNodes.Length; ++i)
            {
                var node = _quadtree.ActiveNodes.Data[i];
                var p = _activeMeshes[node.MeshIdx];
                p.UpdatePatch(cullCamera.transform.position, cullCamera.fieldOfView, Screen.height,
                    Screen.width);
            }
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginFrameRendering -= OnFrameRendering;
            _meshPool.Clear();
            ImtPooledRenderMesh.Clear();
            MTHeightMap.UnregisterMap(_heightMap);
        }
    }
}