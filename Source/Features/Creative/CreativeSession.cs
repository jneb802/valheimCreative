using System;
using System.Globalization;
using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal sealed class CreativeSession
    {
        internal long PlayerId { get; }
        internal long PeerId { get; set; }
        internal string PlayerName { get; }
        internal string SlotId { get; }
        internal Vector3 CreativePosition { get; }
        internal Quaternion CreativeRotation { get; }
        internal Vector3 ReturnPosition { get; }
        internal Quaternion ReturnRotation { get; }
        internal bool AwaitingRespawn { get; set; }
        internal bool WasDead { get; set; }
        internal bool CreativeKeysSent { get; set; }

        internal CreativeSession(
            long playerId,
            long peerId,
            string playerName,
            string slotId,
            Vector3 creativePosition,
            Quaternion creativeRotation,
            Vector3 returnPosition,
            Quaternion returnRotation)
        {
            PlayerId = playerId;
            PeerId = peerId;
            PlayerName = playerName;
            SlotId = slotId;
            CreativePosition = creativePosition;
            CreativeRotation = creativeRotation;
            ReturnPosition = returnPosition;
            ReturnRotation = returnRotation;
        }

        internal string Serialize()
        {
            return string.Join(
                "\t",
                PlayerId.ToString(CultureInfo.InvariantCulture),
                PeerId.ToString(CultureInfo.InvariantCulture),
                Escape(PlayerName),
                Escape(SlotId),
                Format(CreativePosition),
                Format(CreativeRotation.eulerAngles),
                Format(ReturnPosition),
                Format(ReturnRotation.eulerAngles),
                AwaitingRespawn ? "1" : "0");
        }

        internal static bool TryDeserialize(string line, out CreativeSession? session)
        {
            session = null;
            string[] parts = line.Split('\t');
            if (parts.Length < 9 ||
                !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long playerId) ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long peerId) ||
                !TryParseVector(parts[4], out Vector3 creativePosition) ||
                !TryParseVector(parts[5], out Vector3 creativeEuler) ||
                !TryParseVector(parts[6], out Vector3 returnPosition) ||
                !TryParseVector(parts[7], out Vector3 returnEuler))
            {
                return false;
            }

            session = new CreativeSession(
                playerId,
                peerId,
                Unescape(parts[2]),
                Unescape(parts[3]),
                creativePosition,
                Quaternion.Euler(creativeEuler),
                returnPosition,
                Quaternion.Euler(returnEuler))
            {
                AwaitingRespawn = parts[8] == "1"
            };
            return true;
        }

        private static string Format(Vector3 value)
        {
            return string.Join(
                ",",
                value.x.ToString(CultureInfo.InvariantCulture),
                value.y.ToString(CultureInfo.InvariantCulture),
                value.z.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryParseVector(string raw, out Vector3 value)
        {
            value = Vector3.zero;
            string[] parts = raw.Split(',');
            if (parts.Length != 3 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return false;
            }

            value = new Vector3(x, y, z);
            return true;
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string Unescape(string value)
        {
            return value.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\\", "\\");
        }
    }
}

