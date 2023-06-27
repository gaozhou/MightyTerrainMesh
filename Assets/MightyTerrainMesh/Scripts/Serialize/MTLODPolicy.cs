using UnityEngine;

namespace MightyTerrainMesh
{
    public class MTLODPolicy : ScriptableObject
    {
        public float[] ScreenCover;

        public int GetLODLevel(float screenSize, float screenW)
        {
            if (ScreenCover == null) return 0;
            var rate = screenSize / screenW;
            for (var lod = 0; lod < ScreenCover.Length; ++lod)
            {
                if (rate >= ScreenCover[lod])
                    return lod;
            }

            return 0;
        }
    }
}