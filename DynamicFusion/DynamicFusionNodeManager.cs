using System;
using System.Collections.Generic;
using System.Numerics;

namespace YMM4_shape_fusion_plugin.DynamicFusion
{
    /// <summary>
    /// 「動的融合:対象」エフェクトが自分の位置・半径を書き込み、
    /// 「動的融合:制御」エフェクトがそれを読み取るための共有ストア。
    /// </summary>
    public struct DynamicFusionNodeData
    {
        public Vector2 Position;
        public float Radius;
    }

    public static class DynamicFusionNodeManager
    {
        // id -> (providerGuid -> 最新データ)
        private static readonly Dictionary<int, Dictionary<Guid, DynamicFusionNodeData>> cache = new();
        private static readonly object sync = new();

        public static void UpdateNode(int id, Guid providerId, DynamicFusionNodeData data)
        {
            lock (sync)
            {
                if (!cache.TryGetValue(id, out var dict))
                {
                    dict = new Dictionary<Guid, DynamicFusionNodeData>();
                    cache[id] = dict;
                }
                dict[providerId] = data;
            }
        }

        public static void ClearNode(int id, Guid providerId)
        {
            lock (sync)
            {
                if (cache.TryGetValue(id, out var dict))
                    dict.Remove(providerId);
            }
        }

        /// <summary>
        /// 指定IDに登録されているデータを1件返す。
        /// 同じIDに複数の対象アイテムが登録されている場合、どれが返るかは不定(v1では1IDにつき1個の登録を想定)。
        /// </summary>
        public static bool TryGetFirstNode(int id, out DynamicFusionNodeData data)
        {
            lock (sync)
            {
                if (cache.TryGetValue(id, out var dict))
                {
                    foreach (var kv in dict)
                    {
                        data = kv.Value;
                        return true;
                    }
                }
            }
            data = default;
            return false;
        }
    }
}
