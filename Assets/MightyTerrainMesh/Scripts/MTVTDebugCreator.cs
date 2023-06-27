using UnityEngine;
using System.Collections.Generic;

namespace MightyTerrainMesh
{
    public class MTDebugTexture : IMTVirtualTexture
    {
        int IMTVirtualTexture.Size => _mSize;

        Texture IMTVirtualTexture.Tex => _tex;

        private readonly int _mSize;
        private readonly Texture2D _tex;

        public MTDebugTexture(int n, Texture2D t)
        {
            _mSize = n;
            _tex = t;
        }
    }

    public class MTVTDebugCreator : MonoBehaviour, IVTCreator
    {
        public Texture2D tex64;
        public Texture2D tex128;
        public Texture2D tex256;
        public Texture2D tex512;
        public Texture2D tex1024;
        public Texture2D tex2048;
        private readonly Queue<MTVTCreateCmd> _commands = new Queue<MTVTCreateCmd>();

        void IVTCreator.AppendCmd(MTVTCreateCmd cmd)
        {
            _commands.Enqueue(cmd);
        }

        void IVTCreator.DisposeTextures(IMTVirtualTexture[] textures)
        {
        }

        private void Update()
        {
            while (_commands.Count > 0)
            {
                var cmd = _commands.Dequeue();
                switch (cmd.Size)
                {
                    case 64:
                        cmd.Receiver.OnTextureReady(cmd.CmdId,
                            new IMTVirtualTexture[] { new MTDebugTexture(64, tex64) });
                        break;
                    case 128:
                        cmd.Receiver.OnTextureReady(cmd.CmdId,
                            new IMTVirtualTexture[] { new MTDebugTexture(128, tex128) });
                        break;
                    case 256:
                        cmd.Receiver.OnTextureReady(cmd.CmdId,
                            new IMTVirtualTexture[] { new MTDebugTexture(256, tex256) });
                        break;
                    case 512:
                        cmd.Receiver.OnTextureReady(cmd.CmdId,
                            new IMTVirtualTexture[] { new MTDebugTexture(512, tex512) });
                        break;
                    case 1024:
                        cmd.Receiver.OnTextureReady(cmd.CmdId,
                            new IMTVirtualTexture[] { new MTDebugTexture(1024, tex1024) });
                        break;
                    case 2048:
                        cmd.Receiver.OnTextureReady(cmd.CmdId,
                            new IMTVirtualTexture[] { new MTDebugTexture(2048, tex2048) });
                        break;
                    default:
                        if (cmd.Size > 2048)
                        {
                            cmd.Receiver.OnTextureReady(cmd.CmdId,
                                new IMTVirtualTexture[] { new MTDebugTexture(2048, tex2048) });
                        }
                        else if (cmd.Size < 64)
                        {
                            cmd.Receiver.OnTextureReady(cmd.CmdId,
                                new IMTVirtualTexture[] { new MTDebugTexture(64, tex64) });
                        }

                        break;
                }

                MTVTCreateCmd.Push(cmd);
            }
        }
    }
}