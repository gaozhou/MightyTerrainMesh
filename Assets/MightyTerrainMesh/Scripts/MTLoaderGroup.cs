using UnityEngine;

namespace MightyTerrainMesh
{
    public class MTLoaderGroup : MonoBehaviour
    {
        public MTLODPolicy lodPolicy;
        public Camera cullCamera;
        public GameObject virtualTextureCreator;
        public MTLoader[] mtLoaders;

        private void Awake()
        {
            foreach (var mtLoader in mtLoaders)
            {
                mtLoader.cullCamera = cullCamera;
                mtLoader.lodPolicy = lodPolicy;
                mtLoader.Init(virtualTextureCreator.GetComponent<IVTCreator>());
            }
        }
    }
}