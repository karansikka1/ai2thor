using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Priority_Queue;
using System;

namespace Thor.Procedural {
    public class ProceduralAssetDatabase : MonoBehaviour {
        public static ProceduralAssetDatabase Instance { get; private set; }

        [SerializeField] public List<Material> materials;
        // TODO: move to not use this list
        [SerializeField] public List<GameObject> prefabs;
        [SerializeField] public int totalMats;

        [SerializeField] public ProceduralLRUCacheAssetMap<GameObject> assetMap;

        void Awake() {
            if (Instance != null) {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            this.assetMap = new ProceduralLRUCacheAssetMap<GameObject>(prefabs.GroupBy(p => p.name).ToDictionary(p => p.Key, p => p.First()));
            DontDestroyOnLoad(gameObject);
        }

        public void addAsset(GameObject asset, bool procedural = false) {
            // prefabs.Add(asset);
            assetMap.addAsset(asset.name, asset, procedural);
        }

        public void addAssets(IEnumerable<GameObject> assets, bool procedural = false) {
            foreach (var asset in assets) {
                assetMap.addAsset(asset.name, asset, procedural);
            }
        }

        public void touchProceduralLRUCache(IEnumerable<string> ids) {
            this.assetMap.touch(ids);
        }

        public void removeLRUItems(int limit) {
            this.assetMap.removeLRU(limit: limit);
        }

        public IEnumerable<GameObject> GetPrefabs() {
            return this.assetMap.Values();
        }
    }


    public class ProceduralLRUCacheAssetMap<T> : AssetMap<T> {

        public SimplePriorityQueue<string, int> proceduralAssetQueue {
            get; private set;
        }
        public int priorityMinValue {
            get; private set;
        }
        public int priorityMaxValue {
            get; private set;
        }
        private int originalPriorityMinValue;
        private int originalPriorityMaxValue;
        public ProceduralLRUCacheAssetMap(int priorityMinValue = 0, int piorityMaxValue = 1) : this(new Dictionary<string, T>(), priorityMinValue, piorityMaxValue) {
        }
        public ProceduralLRUCacheAssetMap(Dictionary<string, T> assetMap, int rankingMinValue = 0, int rankingMaxValue = 1) : base(assetMap) {
            this.priorityMinValue = this.originalPriorityMinValue = rankingMinValue;
            this.priorityMaxValue = this.originalPriorityMaxValue = rankingMaxValue;
            proceduralAssetQueue = new SimplePriorityQueue<string, int>();
        }

        // TODO: If we want an in unity LRU, not driven by python hooks call use here
        // python hooks is desired because asset-dynamic-creation is driven by hooks
        // so there can be times if we internally drive the LRU cache that we may delete
        // assets and not have the hook to create them

        public override T getAsset(string name) {
            return assetMap[name];
        }

        public void addAsset(string id, T asset, bool procedural = true) {
            if (procedural) {
                proceduralAssetQueue.Enqueue(id, this.priorityMaxValue);
            }
            this.assetMap.Add(id, asset);
        }

        public void touch(IEnumerable<string> ids) {
            this.advanceExpiration();
            this.use(ids);
        }
        public void touch(string id) {
            this.advanceExpiration();
            this.use(id);
        }

        public AsyncOperation removeLRU(int limit, bool deleteWithHighestPriority = true) {
            if (proceduralAssetQueue.Count == 0) {
                return null;
            }

            var current = proceduralAssetQueue.First;
            var toDequeuePrio = proceduralAssetQueue.GetPriority(current);
            int dequeueCount = 0;
            // Do not delete items with the highest priority if !deleteWithHighestPriority
            while (proceduralAssetQueue.Count > limit && (deleteWithHighestPriority || toDequeuePrio < this.priorityMaxValue)) {
                var removed = proceduralAssetQueue.Dequeue();
                this.assetMap.Remove(removed);
                dequeueCount++;
                current = proceduralAssetQueue.First;
                toDequeuePrio = proceduralAssetQueue.GetPriority(current);
            }
            AsyncOperation asyncOp = null;
            if (dequeueCount > 0) { 
                // WARNING: Async operation, should be ok for deleting assets if using the same creation-deletion hook
                // cache should be all driven within one system, currently python driven
                asyncOp = Resources.UnloadUnusedAssets();
                asyncOp.completed += (op) => GC.Collect();
            }
            return asyncOp;
        }

        protected void use(IEnumerable<string> ids) {
            foreach (var id in ids) {
                this.use(id);
            }
        }

        protected void use(string name) {
            if (proceduralAssetQueue.Contains(name)) {
                var currentPriority = proceduralAssetQueue.GetPriority(name);
                if (currentPriority < priorityMaxValue) {
                    proceduralAssetQueue.UpdatePriority(name, priorityMaxValue);
                }
            }
        }

        // Amortized O(n)
        protected void advanceExpiration() { 
            if (this.priorityMaxValue+1 != int.MaxValue) {
                this.priorityMinValue++;
                this.priorityMaxValue++;
            }
            else {
                foreach (var item in proceduralAssetQueue) { 
                    var currentPriority = proceduralAssetQueue.GetPriority(item);
                    var distance = currentPriority - this.priorityMinValue;
                    proceduralAssetQueue.UpdatePriority(item, this.originalPriorityMinValue + distance);
                }
                this.priorityMinValue = this.originalPriorityMinValue;
                this.priorityMaxValue = this.originalPriorityMaxValue;
            }
           
        }

        // O(n) every time
        // public void advanceExpiration() {
        //     foreach (var item in proceduralAssetQueue) {
        //         var currentPriority = proceduralAssetQueue.GetPriority(item);
        //         if (currentPriority < this.rankingMaxValue) {
        //             proceduralAssetQueue.UpdatePriority(item, currentPriority + 1);
        //         }
        //     }
        // }
    }
}