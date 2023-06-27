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
        private Queue<MTVTCreateCmd> qVTCreateCmds = new Queue<MTVTCreateCmd>();

        private Dictionary<int, Queue<IMTVirtualTexture[]>> texturePools =
            new Dictionary<int, Queue<IMTVirtualTexture[]>>();

        private List<IMTVirtualTexture> activeTextures = new List<IMTVirtualTexture>();
        private Queue<VTRenderJob> bakedJobs = new Queue<VTRenderJob>();

        private RuntimeBakeTexture[] PopTexture(int size)
        {
            var texSize = size;
            if (texQuality == TextureQuality.Half)
            {
                texSize = size >> 1;
            }
            else if (texQuality == TextureQuality.Quarter)
            {
                texSize = size >> 2;
            }

            RuntimeBakeTexture[] ret = null;
            if (!texturePools.ContainsKey(texSize))
                texturePools.Add(texSize, new Queue<IMTVirtualTexture[]>());
            var q = texturePools[texSize];
            if (q.Count > 0)
            {
                ret = q.Dequeue() as RuntimeBakeTexture[];
            }
            else
            {
                ret = new RuntimeBakeTexture[] { new RuntimeBakeTexture(texSize), new RuntimeBakeTexture(texSize) };
            }

            return ret;
        }

        void IVTCreator.AppendCmd(MTVTCreateCmd cmd)
        {
            qVTCreateCmds.Enqueue(cmd);
        }

        void IVTCreator.DisposeTextures(IMTVirtualTexture[] ts)
        {
            var size = ts[0].Size;
            activeTextures.Remove(ts[0]);
            activeTextures.Remove(ts[1]);
            if (texturePools.ContainsKey(size))
            {
                texturePools[size].Enqueue(ts);
            }
            else
            {
                Debug.LogWarning("DisposeTextures Invalid texture size : " + size);
            }
        }

        void OnDestroy()
        {
            foreach (var q in texturePools.Values)
            {
                while (q.Count > 0)
                {
                    var rbt = q.Dequeue() as RuntimeBakeTexture[];
                    rbt[0].Clear();
                    rbt[1].Clear();
                }
            }

            texturePools.Clear();
            MTVTCreateCmd.Clear();
        }

        // Update is called once per frame
        void Update()
        {
            while (bakedJobs.Count > 0)
            {
                var job = bakedJobs.Dequeue();
                job.SendTexturesReady();
                activeTextures.Add(job.textures[0]);
                activeTextures.Add(job.textures[1]);
                VTRenderJob.Push(job);
            }

            int bakeCount = 0;
            while (qVTCreateCmds.Count > 0 && bakeCount < maxBakeCountPerFrame)
            {
                var cmd = qVTCreateCmds.Dequeue();
                if (cmd.Receiver.WaitCmdId == cmd.CmdId)
                {
                    var ts = PopTexture(cmd.Size);
                    ts[0].Reset(cmd.UVMin, cmd.UVMax, cmd.BakeDiffuse);
                    ts[1].Reset(cmd.UVMin, cmd.UVMax, cmd.BakeNormal);
                    var job = VTRenderJob.Pop();
                    job.Reset(cmd.CmdId, ts, cmd.Receiver);
                    job.DoJob();
                    bakedJobs.Enqueue(job);
                    MTVTCreateCmd.Push(cmd);
                    ++bakeCount;
                }
                else
                {
                    MTVTCreateCmd.Push(cmd);
                }
            }

            for (int count = activeTextures.Count - 1; count >= 0; --count)
            {
                var tex = activeTextures[count] as RuntimeBakeTexture;
                bool needRender = tex.Validate();
                if (needRender)
                {
                    tex.Bake();
                }
            }
        }
    }
}