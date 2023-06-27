using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MightyTerrainMesh
{
    public class RuntimeBakeTexture : IMTVirtualTexture
    {
        private static Mesh _fullscreenMesh;

        /// <summary>
        /// Returns a mesh that you can use with <see cref="CommandBuffer.DrawMesh(Mesh, Matrix4x4, Material)"/> to render full-screen effects.
        /// </summary>
        private static Mesh FullscreenMesh
        {
            get
            {
                if (_fullscreenMesh != null)
                    return _fullscreenMesh;

                const float topV = 1.0f;
                const float bottomV = 0.0f;

                _fullscreenMesh = new Mesh { name = "Fullscreen Quad" };
                _fullscreenMesh.SetVertices(new List<Vector3>
                {
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(-1.0f, 1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                    new Vector3(1.0f, 1.0f, 0.0f)
                });

                _fullscreenMesh.SetUVs(0, new List<Vector2>
                {
                    new Vector2(0.0f, bottomV),
                    new Vector2(0.0f, topV),
                    new Vector2(1.0f, bottomV),
                    new Vector2(1.0f, topV)
                });

                _fullscreenMesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
                _fullscreenMesh.UploadMeshData(true);
                return _fullscreenMesh;
            }
        }

        int IMTVirtualTexture.Size => _texSize;

        Texture IMTVirtualTexture.Tex => RTT;

        private Material[] Layers { get; set; }
        private RenderTexture RTT { get; set; }
        private readonly int _texSize;
        private Vector4 _scaleOffset;
        private CommandBuffer _cmdBuffer;
        private static readonly int BakeScaleOffset = Shader.PropertyToID("_BakeScaleOffset");

        public RuntimeBakeTexture(int size)
        {
            _texSize = size;
            _scaleOffset = new Vector4(1, 1, 0, 0);
            _cmdBuffer = new CommandBuffer();
            _cmdBuffer.name = "RuntimeBakeTexture";
            CreateRTT();
        }

        private void CreateRTT()
        {
            const RenderTextureFormat format = RenderTextureFormat.Default;
            RTT = new RenderTexture(_texSize, _texSize, 0, format, RenderTextureReadWrite.Default)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            RTT.Create();
            RTT.DiscardContents();
        }

        public void Reset(Vector2 uvMin, Vector2 uvMax, Material[] mats)
        {
            _scaleOffset.x = uvMax.x - uvMin.x;
            _scaleOffset.y = uvMax.y - uvMin.y;
            _scaleOffset.z = uvMin.x;
            _scaleOffset.w = uvMin.y;
            Layers = mats;
            Validate();
        }

        public void Bake()
        {
            foreach (var layer in Layers)
            {
                layer.SetVector(BakeScaleOffset, _scaleOffset);
            }

            RTT.DiscardContents();
            _cmdBuffer.Clear();
            _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            _cmdBuffer.SetViewport(new Rect(0, 0, RTT.width, RTT.height));
            _cmdBuffer.SetRenderTarget(RTT, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
            foreach (var layer in Layers)
            {
                _cmdBuffer.DrawMesh(FullscreenMesh, Matrix4x4.identity, layer);
            }

            Graphics.ExecuteCommandBuffer(_cmdBuffer);
        }

        public bool Validate()
        {
            if (RTT.IsCreated()) return false;
            RTT.Release();
            CreateRTT();
            return true;
        }

        public void Clear()
        {
            if (RTT != null)
            {
                RTT.Release();
                RTT = null;
            }

            Layers = null;
            _cmdBuffer.Clear();
            _cmdBuffer = null;
        }
    }

    public class VTRenderJob
    {
        private static readonly Queue<VTRenderJob> QPool = new Queue<VTRenderJob>();

        public static VTRenderJob Pop()
        {
            return QPool.Count > 0 ? QPool.Dequeue() : new VTRenderJob();
        }

        public static void Push(VTRenderJob p)
        {
            p.Textures = null;
            p._receiver = null;
            QPool.Enqueue(p);
        }

        public static void Clear()
        {
            QPool.Clear();
        }

        public IMTVirtualTexture[] Textures;
        private IMTVirtualTextureReceiver _receiver;
        private long _cmdId;

        public void Reset(long cmd, RuntimeBakeTexture[] ts, IMTVirtualTextureReceiver r)
        {
            _cmdId = cmd;
            Textures = ts;
            _receiver = r;
        }

        public void DoJob()
        {
            foreach (var tex in Textures)
            {
                (tex as RuntimeBakeTexture)?.Bake();
            }
        }

        public void SendTexturesReady()
        {
            _receiver.OnTextureReady(_cmdId, Textures);
            _receiver = null;
        }
    }
}