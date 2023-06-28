using UnityEngine;
using UnityEngine.Serialization;

namespace MightyTerrainMesh
{
    public class MTLODPolicy : ScriptableObject
    {
        [FormerlySerializedAs("ScreenCover")] public float[] screenCover;

        public int GetLODLevel(float screenSize, float screenW)
        {
            if (screenCover == null) return 0;
            var rate = screenSize / screenW;
            for (var lod = 0; lod < screenCover.Length; ++lod)
            {
                if (rate >= screenCover[lod])
                    return lod;
            }

            return 0;
        }
    }
}