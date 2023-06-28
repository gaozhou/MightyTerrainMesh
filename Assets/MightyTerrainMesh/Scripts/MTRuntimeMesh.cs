using System;
using UnityEngine;
using System.Collections.Generic;
using Mono.Cecil;
using Object = UnityEngine.Object;

namespace MightyTerrainMesh
{
    public interface IMTVirtualTextureReceiver
    {
        long WaitCmdId { get; }
        void OnTextureReady(long cmdId, IMTVirtualTexture[] textures);
    }

    public class ImtPooledRenderMesh : IMTVirtualTextureReceiver
    {
        private static readonly Queue<ImtPooledRenderMesh> QPool = new Queue<ImtPooledRenderMesh>();

        public static ImtPooledRenderMesh Pop()
        {
            return QPool.Count > 0 ? QPool.Dequeue() : new ImtPooledRenderMesh();
        }

        public static void Push(ImtPooledRenderMesh p)
        {
            p.OnPushBackPool();
            QPool.Enqueue(p);
        }

        public static void Clear()
        {
            while (QPool.Count > 0)
            {
                QPool.Dequeue().DestroySelf();
            }
        }

        private MTData _mDataHeader;
        private MTRenderMesh _mRm;
        private GameObject _mGo;
        private MeshFilter _mMesh;
        private readonly MeshRenderer _mRenderer;
        private Material[] _mMats;
        private IVTCreator _mVTCreator;
        private float _mDiameter;
        private Vector3 _mCenter = Vector3.zero;

        private int _mTextureSize = -1;

        //this is the texture for rendering, till the baking texture ready, this can be push back to pool
        private IMTVirtualTexture[] _mTextures;

        //baking parameters
        private long _waitBackCmdId;

        private MTVTCreateCmd _lastPendingCreateCmd;
        private static readonly int Diffuse = Shader.PropertyToID("_Diffuse");
        private static readonly int Normal = Shader.PropertyToID("_Normal");

        //
        private ImtPooledRenderMesh()
        {
            _mGo = new GameObject("_mtpatch");
            _mMesh = _mGo.AddComponent<MeshFilter>();
            _mRenderer = _mGo.AddComponent<MeshRenderer>();
            _mRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Reset(MTData header, IVTCreator vtCreator, MTRenderMesh m, Vector3 offset)
        {
            _mDataHeader = header;
            _mVTCreator = vtCreator;
            _mRm = m;
            _mGo.SetActive(true);
            _mGo.transform.position = offset;
            //mesh and material
            _mMesh.mesh = _mRm.Mesh;
            if (_mMats == null)
            {
                _mMats = new Material[1];
                _mMats[0] = Object.Instantiate(_mDataHeader.bakedMat);
            }

            ClearRendererMaterial();
            _mRenderer.materials = _mDataHeader.detailMats;
            //
            _mDiameter = _mRm.Mesh.bounds.size.magnitude;
            _mCenter = _mRm.Mesh.bounds.center + offset;
            _mTextureSize = -1;
            _waitBackCmdId = 0;
        }

        private void ClearRendererMaterial()
        {
            if (_mRenderer == null || _mRenderer.materials == null) return;
            foreach (var mat in _mRenderer.materials)
            {
                Object.Destroy(mat);
            }
        }

        private void OnPushBackPool()
        {
            _mRenderer.materials = Array.Empty<Material>();
            _mMats[0].SetTexture(Diffuse, null);
            _mMats[0].SetTexture(Normal, null);
            if (_mGo != null)
                _mGo.SetActive(false);
            if (_mTextures != null)
            {
                _mVTCreator.DisposeTextures(_mTextures);
                _mTextures = null;
            }

            _waitBackCmdId = 0;
            if (_lastPendingCreateCmd != null)
            {
                MTVTCreateCmd.Push(_lastPendingCreateCmd);
                _lastPendingCreateCmd = null;
            }

            _mTextureSize = -1;
            _mRm = null;
        }

        private void RequestTexture(int size)
        {
            size = Mathf.Clamp(size, 128, 2048);
            //use size to fixed the render texture format, otherwise the texture will always receate
            if (size == _mTextureSize) return;
            _mTextureSize = size;
            var cmd = MTVTCreateCmd.Pop();
            cmd.CmdId = MTVTCreateCmd.GenerateID();
            cmd.Size = size;
            cmd.UVMin = _mRm.UVMin;
            cmd.UVMax = _mRm.UVMax;
            cmd.BakeDiffuse = _mDataHeader.bakeDiffuseMats;
            cmd.BakeNormal = _mDataHeader.bakeNormalMats;
            cmd.Receiver = this;
            if (_waitBackCmdId > 0)
            {
                if (_lastPendingCreateCmd != null)
                {
                    MTVTCreateCmd.Push(_lastPendingCreateCmd);
                }

                _lastPendingCreateCmd = cmd;
            }
            else
            {
                _waitBackCmdId = cmd.CmdId;
                _mVTCreator.AppendCmd(cmd);
            }
        }

        private void ApplyTextures()
        {
            var size = _mRm.UVMax - _mRm.UVMin;
            var scale = new Vector2(1f / size.x, 1f / size.y);
            var offset = -new Vector2(scale.x * _mRm.UVMin.x, scale.y * _mRm.UVMin.y);
            _mMats[0].SetTexture(Diffuse, _mTextures[0].Tex);
            _mMats[0].SetTextureScale(Diffuse, scale);
            _mMats[0].SetTextureOffset(Diffuse, offset);
            if (_mTextures.Length > 1)
                _mMats[0].SetTexture(Normal, _mTextures[1].Tex);
            _mMats[0].SetTextureScale(Normal, scale);
            _mMats[0].SetTextureOffset(Normal, offset);
        }

        long IMTVirtualTextureReceiver.WaitCmdId => _waitBackCmdId;

        void IMTVirtualTextureReceiver.OnTextureReady(long cmdId, IMTVirtualTexture[] textures)
        {
            if (_mRm == null || cmdId != _waitBackCmdId)
            {
                _mVTCreator.DisposeTextures(textures);
                return;
            }

            if (_mTextures != null)
            {
                _mVTCreator.DisposeTextures(_mTextures);
                _mTextures = null;
            }

            _mTextures = textures;
            ApplyTextures();
            ClearRendererMaterial();
            _mRenderer.materials = _mMats;
            _waitBackCmdId = 0;
            if (_lastPendingCreateCmd == null) return;
            _waitBackCmdId = _lastPendingCreateCmd.CmdId;
            _mVTCreator.AppendCmd(_lastPendingCreateCmd);
            _lastPendingCreateCmd = null;
        }

        private void DestroySelf()
        {
            ClearRendererMaterial();
            if (_mMats != null)
            {
                foreach (var m in _mMats)
                    Object.Destroy(m);
            }

            _mMats = null;
            if (_mGo != null)
                Object.Destroy(_mGo);
            _mGo = null;
            _mMesh = null;
        }

        public void UpdatePatch(Vector3 viewCenter, float fov, float screenH, float screenW)
        {
            var curTexSize = _mVTCreator.CalculateTextureSize(viewCenter, fov, screenH, _mDiameter, _mCenter);
            if (curTexSize != _mTextureSize)
            {
                RequestTexture(curTexSize);
            }
        }
    }

    public class MTRenderMesh
    {
        public Mesh Mesh;
        public Vector2 UVMin;
        public Vector2 UVMax;

        public void Clear()
        {
            Object.Destroy(Mesh);
            Mesh = null;
        }
    }
}