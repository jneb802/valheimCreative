using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal sealed class BlueprintPieceEntry
    {
        internal BlueprintPieceEntry(
            string prefabName,
            string category,
            Vector3 localPosition,
            Quaternion localRotation,
            string data,
            Vector3 scale)
        {
            PrefabName = prefabName;
            Category = category;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            Data = data;
            Scale = scale;
        }

        internal string PrefabName { get; }
        internal string Category { get; }
        internal Vector3 LocalPosition { get; }
        internal Quaternion LocalRotation { get; }
        internal string Data { get; }
        internal Vector3 Scale { get; }
    }
}
