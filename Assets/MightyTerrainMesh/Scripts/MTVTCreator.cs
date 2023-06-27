using UnityEngine;
using System.Collections.Generic;

namespace MightyTerrainMesh
{
    public class MTVTCreateCmd
    {
        private static readonly Queue<MTVTCreateCmd> QPool = new Queue<MTVTCreateCmd>();

        public static MTVTCreateCmd Pop()
        {
            return QPool.Count > 0 ? QPool.Dequeue() : new MTVTCreateCmd();
        }

        public static void Push(MTVTCreateCmd p)
        {
            p.BakeDiffuse = null;
            p.BakeNormal = null;
            p.Receiver = null;
            QPool.Enqueue(p);
        }

        public static void Clear()
        {
            QPool.Clear();
        }

        private static long _cmdIDSeed;

        public static long GenerateID()
        {
            ++_cmdIDSeed;
            return _cmdIDSeed;
        }


        public long CmdId = 0;
        public int Size = 64;
        public Material[] BakeDiffuse;
        public Material[] BakeNormal;
        public Vector2 UVMin;
        public Vector2 UVMax;
        public IMTVirtualTextureReceiver Receiver;
    }

    public interface IMTVirtualTexture
    {
        int Size { get; }
        Texture Tex { get; }
    }

    public interface IVTCreator
    {
        void AppendCmd(MTVTCreateCmd cmd);
        void DisposeTextures(IMTVirtualTexture[] textures);
    }

    public class MTVTCreator : MonoBehaviour, IVTCreator
    {
        public enum TextureQuality
        {
            Full,
            Half,
            Quarter,
        }

        public TextureQuality texQuality = TextureQuality.Full;
        public int maxBakeCountPerFrame = 8;
        private readonly Queue<MTVTCreateCmd> _qVTCreateCommands = new Queue<MTVTCreateCmd>();

        private readonly Dictionary<int, Queue<IMTVirtualTexture[]>> _texturePools =
            new Dictionary<int, Queue<IMTVirtualTexture[]>>();

        private readonly List<IMTVirtualTexture> _activeTextures = new List<IMTVirtualTexture>();
        private readonly Queue<VTRenderJob> _bakedJobs = new Queue<VTRenderJob>();

        private RuntimeBakeTexture[] PopTexture(int size)
        {
            var texSize = texQuality switch
            {
                TextureQuality.Half => size >> 1,
                TextureQuality.Quarter => size >> 2,
                _ => size
            };

            RuntimeBakeTexture[] ret;
            if (!_texturePools.ContainsKey(texSize))
                _texturePools.Add(texSize, new Queue<IMTVirtualTexture[]>());
            var q = _texturePools[texSize];
            if (q.Count > 0)
            {
                ret = q.Dequeue() as RuntimeBakeTexture[];
            }
            else
            {
                ret = new[] { new RuntimeBakeTexture(texSize), new RuntimeBakeTexture(texSize) };
            }

            return ret;
        }

        void IVTCreator.AppendCmd(MTVTCreateCmd cmd)
        {
            _qVTCreateCommands.Enqueue(cmd);
        }

        void IVTCreator.DisposeTextures(IMTVirtualTexture[] ts)
        {
            var size = ts[0].Size;
            _activeTextures.Remove(ts[0]);
            _activeTextures.Remove(ts[1]);
            if (_texturePools.TryGetValue(size, out var pool))
            {
                pool.Enqueue(ts);
            }
            else
            {
                Debug.LogWarning("DisposeTextures Invalid texture size : " + size);
            }
        }

        private void OnDestroy()
        {
            foreach (var q in _texturePools.Values)
            {
                while (q.Count > 0)
                {
                    if (!(q.Dequeue() is RuntimeBakeTexture[] rbt)) continue;
                    rbt[0].Clear();
                    rbt[1].Clear();
                }
            }

            _texturePools.Clear();
            MTVTCreateCmd.Clear();
        }

        // Update is called once per frame
        private void Update()
        {
            while (_bakedJobs.Count > 0)
            {
                var job = _bakedJobs.Dequeue();
                job.SendTexturesReady();
                _activeTextures.Add(job.Textures[0]);
                _activeTextures.Add(job.Textures[1]);
                VTRenderJob.Push(job);
            }

            var bakeCount = 0;
            while (_qVTCreateCommands.Count > 0 && bakeCount < maxBakeCountPerFrame)
            {
                var cmd = _qVTCreateCommands.Dequeue();
                if (cmd.Receiver.WaitCmdId == cmd.CmdId)
                {
                    var ts = PopTexture(cmd.Size);
                    ts[0].Reset(cmd.UVMin, cmd.UVMax, cmd.BakeDiffuse);
                    ts[1].Reset(cmd.UVMin, cmd.UVMax, cmd.BakeNormal);
                    var job = VTRenderJob.Pop();
                    job.Reset(cmd.CmdId, ts, cmd.Receiver);
                    job.DoJob();
                    _bakedJobs.Enqueue(job);
                    MTVTCreateCmd.Push(cmd);
                    ++bakeCount;
                }
                else
                {
                    MTVTCreateCmd.Push(cmd);
                }
            }

            for (var count = _activeTextures.Count - 1; count >= 0; --count)
            {
                var tex = _activeTextures[count] as RuntimeBakeTexture;
                var needRender = tex != null && tex.Validate();
                if (needRender)
                {
                    tex.Bake();
                }
            }
        }
    }
}