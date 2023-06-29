using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace MightyTerrainMesh
{
    public static class MTMatUtils
    {
        private static Texture2D ExportAlphaMap(string path, string dataName, Terrain t, int matIdx)
        {
#if UNITY_EDITOR
            if (matIdx >= t.terrainData.alphamapTextureCount)
                return null;
            //alpha map
            var alphaMapData = t.terrainData.alphamapTextures[matIdx].EncodeToTGA();
            var alphaMapSavePath = $"{path}/{dataName}_alpha{matIdx}.tga";
            if (File.Exists(alphaMapSavePath))
                File.Delete(alphaMapSavePath);
            var stream = File.Open(alphaMapSavePath, FileMode.Create);
            stream.Write(alphaMapData, 0, alphaMapData.Length);
            stream.Close();
            AssetDatabase.Refresh();
            var alphaMapPath = $"{path}/{dataName}_alpha{matIdx}.tga";
            //the alpha map texture has to be set to best compression quality, otherwise the black spot may
            //show on the ground
            var importer = AssetImporter.GetAtPath(alphaMapPath) as TextureImporter;
            if (importer == null)
            {
                MTLog.LogError("export terrain alpha map failed");
                return null;
            }

            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = false; //数据贴图，千万别srgb
            importer.mipmapEnabled = false;
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(alphaMapPath);
#else
            return null;
#endif
        }

        private static void SaveMixMaterial(string path, string dataName, Terrain t, int matIdx, int layerStart,
            string shaderName, ICollection<string> assetPath)
        {
#if UNITY_EDITOR
            var alphaMap = ExportAlphaMap(path, dataName, t, matIdx);
            if (alphaMap == null)
                return;
            //
            var mathPath = $"{path}/{dataName}_{matIdx}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mathPath);
            if (mat != null)
                AssetDatabase.DeleteAsset(mathPath);
            var tMat = new Material(Shader.Find(shaderName));
            tMat.SetTexture("_Control", alphaMap);
            if (tMat == null)
            {
                MTLog.LogError("export terrain material failed");
                return;
            }

            for (var l = layerStart; l < layerStart + 4 && l < t.terrainData.terrainLayers.Length; ++l)
            {
                var idx = l - layerStart;
                var layer = t.terrainData.terrainLayers[l];
                var tiling = new Vector2(t.terrainData.size.x / layer.tileSize.x,
                    t.terrainData.size.z / layer.tileSize.y);
                tMat.SetTexture($"_Splat{idx}", layer.diffuseTexture);
                tMat.SetTextureOffset($"_Splat{idx}", layer.tileOffset);
                tMat.SetTextureScale($"_Splat{idx}", tiling);
                tMat.SetTexture($"_Normal{idx}", layer.normalMapTexture);
                tMat.SetFloat($"_NormalScale{idx}", layer.normalScale);
                tMat.SetFloat($"_Metallic{idx}", layer.metallic);
                tMat.SetFloat($"_Smoothness{idx}", layer.smoothness);
                tMat.EnableKeyword("_NORMALMAP");
                if (layer.maskMapTexture != null)
                {
                    tMat.EnableKeyword("_MASKMAP");
                    tMat.SetFloat($"_LayerHasMask{idx}", 1f);
                    tMat.SetTexture($"_Mask{idx}", layer.maskMapTexture);
                }
                else
                {
                    tMat.SetFloat($"_LayerHasMask{idx}", 0f);
                }
            }

            AssetDatabase.CreateAsset(tMat, mathPath);
            assetPath?.Add(mathPath);
#endif
        }

        public static void SaveMixMaterials(string path, string dataName, Terrain t, List<string> assetPath)
        {
#if UNITY_EDITOR
            if (t.terrainData == null)
            {
                MTLog.LogError("terrain data doesn't exist");
                return;
            }

            var matCount = t.terrainData.alphamapTextureCount;
            if (matCount <= 0)
                return;
            //base pass
            SaveMixMaterial(path, dataName, t, 0, 0, "MT/TerrainLit", assetPath);
            for (var i = 1; i < matCount; ++i)
            {
                SaveMixMaterial(path, dataName, t, i, i * 4, "MT/TerrainLitAdd", assetPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
        }

        private static void SaveVTMaterial(string path, string dataName, Terrain t, int matIdx, int layerStart,
            string shaderPostfix,
            ICollection<string> albedoPath, ICollection<string> bumpPath)
        {
#if UNITY_EDITOR
            var alphaMap = ExportAlphaMap(path, dataName, t, matIdx);
            if (alphaMap == null)
                return;
            //
            var mathPath = $"{path}/VTDiffuse_{matIdx}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mathPath);
            if (mat != null)
                AssetDatabase.DeleteAsset(mathPath);
            var tMat = new Material(Shader.Find("MT/VTDiffuse" + shaderPostfix));
            tMat.SetTexture("_Control", alphaMap);
            if (tMat == null)
            {
                MTLog.LogError("export terrain vt diffuse material failed");
                return;
            }

            var bumpMatPath = $"{path}/VTBump_{matIdx}.mat";
            var bMat = AssetDatabase.LoadAssetAtPath<Material>(bumpMatPath);
            if (bMat != null)
                AssetDatabase.DeleteAsset(bumpMatPath);
            var bumpMat = new Material(Shader.Find("MT/VTBump" + shaderPostfix));
            bumpMat.SetTexture("_Control", alphaMap);
            if (bumpMat == null)
            {
                MTLog.LogError("export terrain vt bump material failed");
                return;
            }

            for (var l = layerStart; l < layerStart + 4 && l < t.terrainData.terrainLayers.Length; ++l)
            {
                var idx = l - layerStart;
                var layer = t.terrainData.terrainLayers[l];
                var tiling = new Vector2(t.terrainData.size.x / layer.tileSize.x,
                    t.terrainData.size.z / layer.tileSize.y);
                tMat.SetTexture($"_Splat{idx}", layer.diffuseTexture);
                tMat.SetTextureOffset($"_Splat{idx}", layer.tileOffset);
                tMat.SetTextureScale($"_Splat{idx}", tiling);
                var diffuseRemapScale = layer.diffuseRemapMax - layer.diffuseRemapMin;
                if (diffuseRemapScale.magnitude > 0)
                    tMat.SetColor($"_DiffuseRemapScale{idx}", diffuseRemapScale);
                else
                    tMat.SetColor($"_DiffuseRemapScale{idx}", Color.white);
                if (layer.maskMapTexture != null)
                {
                    tMat.SetFloat($"_HasMask{idx}", 1f);
                    tMat.SetTexture($"_Mask{idx}", layer.maskMapTexture);
                }
                else
                {
                    tMat.SetFloat($"_HasMask{idx}", 0f);
                }

                tMat.SetFloat($"_Smoothness{idx}", layer.smoothness);

                bumpMat.SetTexture($"_Normal{idx}", layer.normalMapTexture);
                bumpMat.SetFloat($"_NormalScale{idx}", layer.normalScale);
                bumpMat.SetTextureOffset($"_Normal{idx}", layer.tileOffset);
                bumpMat.SetTextureScale($"_Normal{idx}", tiling);
                if (layer.maskMapTexture != null)
                {
                    bumpMat.SetFloat($"_HasMask{idx}", 1f);
                    bumpMat.SetTexture($"_Mask{idx}", layer.maskMapTexture);
                }
                else
                {
                    bumpMat.SetFloat($"_HasMask{idx}", 0f);
                }

                bumpMat.SetFloat($"_Metallic{idx}", layer.metallic);
            }

            AssetDatabase.CreateAsset(tMat, mathPath);
            albedoPath?.Add(mathPath);
            AssetDatabase.CreateAsset(bumpMat, bumpMatPath);
            bumpPath?.Add(bumpMatPath);
#endif
        }

        public static void SaveVTMaterials(string path, string dataName, Terrain t,
            List<string> albedoPath, List<string> bumpPath)
        {
#if UNITY_EDITOR
            if (t.terrainData == null)
            {
                MTLog.LogError("terrain data doesn't exist");
                return;
            }

            var matCount = t.terrainData.alphamapTextureCount;
            if (matCount <= 0)
                return;
            //base pass
            SaveVTMaterial(path, dataName, t, 0, 0, "", albedoPath, bumpPath);
            for (var i = 1; i < matCount; ++i)
            {
                SaveVTMaterial(path, dataName, t, i, i * 4, "Add", albedoPath, bumpPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
        }

        private static Material GetBakeAlbedo(Terrain t, int matIdx, int layerStart, string shaderName)
        {
#if UNITY_EDITOR
            var tMat = new Material(Shader.Find(shaderName));
            if (matIdx < t.terrainData.alphamapTextureCount)
            {
                var alphaMap = t.terrainData.alphamapTextures[matIdx];
                tMat.SetTexture("_Control", alphaMap);
            }

            for (var l = layerStart; l < layerStart + 4 && l < t.terrainData.terrainLayers.Length; ++l)
            {
                var idx = l - layerStart;
                var layer = t.terrainData.terrainLayers[l];
                var tiling = new Vector2(t.terrainData.size.x / layer.tileSize.x,
                    t.terrainData.size.z / layer.tileSize.y);
                tMat.SetTexture($"_Splat{idx}", layer.diffuseTexture);
                tMat.SetTextureOffset($"_Splat{idx}", layer.tileOffset);
                tMat.SetTextureScale($"_Splat{idx}", tiling);
                if (layer.maskMapTexture != null)
                {
                    tMat.SetFloat($"_HasMask{idx}", 1f);
                    tMat.SetTexture($"_Mask{idx}", layer.maskMapTexture);
                }
                else
                {
                    tMat.SetFloat($"_HasMask{idx}", 0f);
                }

                tMat.SetFloat($"_Smoothness{idx}", layer.smoothness);
            }

            return tMat;
#else
            return null;
#endif
        }

        private static Material GetBakeNormal(Terrain t, int matIdx, int layerStart, string shaderName)
        {
#if UNITY_EDITOR
            var tMat = new Material(Shader.Find(shaderName));
            if (matIdx < t.terrainData.alphamapTextureCount)
            {
                var alphaMap = t.terrainData.alphamapTextures[matIdx];
                tMat.SetTexture("_Control", alphaMap);
            }

            for (var l = layerStart; l < layerStart + 4 && l < t.terrainData.terrainLayers.Length; ++l)
            {
                var idx = l - layerStart;
                var layer = t.terrainData.terrainLayers[l];
                var tiling = new Vector2(t.terrainData.size.x / layer.tileSize.x,
                    t.terrainData.size.z / layer.tileSize.y);
                tMat.SetTexture($"_Normal{idx}", layer.normalMapTexture);
                tMat.SetFloat($"_NormalScale{idx}", layer.normalScale);
                tMat.SetTextureOffset($"_Normal{idx}", layer.tileOffset);
                tMat.SetTextureScale($"_Normal{idx}", tiling);
                if (layer.maskMapTexture != null)
                {
                    tMat.SetFloat($"_HasMask{idx}", 1f);
                    tMat.SetTexture($"_Mask{idx}", layer.maskMapTexture);
                }
                else
                {
                    tMat.SetFloat($"_HasMask{idx}", 0f);
                }

                tMat.SetFloat($"_Metallic{idx}", layer.metallic);
            }

            return tMat;
#else
            return null;
#endif
        }

        public static void GetBakeMaterials(Terrain t, Material[] albedoList, Material[] bumps)
        {
#if UNITY_EDITOR
            if (t.terrainData == null)
            {
                MTLog.LogError("terrain data doesn't exist");
                return;
            }

            var matCount = t.terrainData.alphamapTextureCount;
            if (matCount <= 0 || albedoList == null || albedoList.Length < 1 || bumps == null || bumps.Length < 1)
                return;
            //base pass
            albedoList[0] = GetBakeAlbedo(t, 0, 0, "MT/VTDiffuse");
            for (var i = 1; i < matCount && i < albedoList.Length; ++i)
            {
                albedoList[i] = GetBakeAlbedo(t, i, i * 4, "MT/VTDiffuseAdd");
            }

            bumps[0] = GetBakeNormal(t, 0, 0, "MT/VTBump");
            for (var i = 1; i < matCount && i < albedoList.Length; ++i)
            {
                bumps[i] = GetBakeNormal(t, i, i * 4, "MT/VTBumpAdd");
            }
#endif
        }
    }
}