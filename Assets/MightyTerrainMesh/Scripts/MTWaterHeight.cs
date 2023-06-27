using UnityEngine;
using System.Collections.Generic;

namespace MightyTerrainMesh
{
    public interface IMTWaterHeightProvider
    {
        bool Contains(Vector3 worldPos);
        float GetHeight(Vector3 worldPos);
    }

    public class MTWaterHeight
    {
        private static readonly List<IMTWaterHeightProvider> Providers = new List<IMTWaterHeightProvider>();

        public static void RegProvider(IMTWaterHeightProvider provider)
        {
            Providers.Add(provider);
        }

        public static void UnRegProvider(IMTWaterHeightProvider provider)
        {
            Providers.Remove(provider);
        }

        public static float GetWaterHeight(Vector3 groundWorldPos)
        {
            var h = groundWorldPos.y;
            foreach (var water in Providers)
            {
                if (!water.Contains(groundWorldPos)) continue;
                var wh = water.GetHeight(groundWorldPos);
                if (wh > h)
                    return wh;
            }

            return h;
        }
    }
}