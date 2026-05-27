using System;
using System.Collections.Generic;

namespace ValheimCreative.Infrastructure.Routing
{
    internal enum RoutedRpcAction
    {
        Continue,
        Consume
    }

    internal delegate RoutedRpcAction RoutedRpcHandler(ZRoutedRpc.RoutedRPCData rpcData);

    internal static class RoutedRpcDispatcher
    {
        private static readonly Dictionary<int, List<RoutedRpcHandler>> Handlers = new();

        internal static void Clear()
        {
            Handlers.Clear();
        }

        internal static void Register(string methodName, RoutedRpcHandler handler)
        {
            int methodHash = methodName.GetStableHashCode();
            if (!Handlers.TryGetValue(methodHash, out List<RoutedRpcHandler> handlers))
            {
                handlers = new List<RoutedRpcHandler>();
                Handlers[methodHash] = handlers;
            }

            handlers.Add(handler);
        }

        internal static bool Process(ZRoutedRpc.RoutedRPCData rpcData)
        {
            if (!Handlers.TryGetValue(rpcData.m_methodHash, out List<RoutedRpcHandler> handlers))
            {
                return true;
            }

            foreach (RoutedRpcHandler handler in handlers)
            {
                try
                {
                    if (handler(rpcData) == RoutedRpcAction.Consume)
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning($"Routed RPC handler failed for method hash {rpcData.m_methodHash}: {ex}");
                }
            }

            return true;
        }
    }
}

