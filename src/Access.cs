using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EnhancedBoatTime
{
    /// <summary>Accessors for private game members.</summary>
    internal static class Access
    {
        public static readonly AccessTools.FieldRef<Ship, Rigidbody> ShipBody =
            AccessTools.FieldRefAccess<Ship, Rigidbody>("m_body");
        public static readonly AccessTools.FieldRef<Ship, ZNetView> ShipNView =
            AccessTools.FieldRefAccess<Ship, ZNetView>("m_nview");
        public static readonly AccessTools.FieldRef<Ship, List<Player>> ShipPlayers =
            AccessTools.FieldRefAccess<Ship, List<Player>>("m_players");

        public static readonly AccessTools.FieldRef<Inventory, int> InvWidth =
            AccessTools.FieldRefAccess<Inventory, int>("m_width");
        public static readonly AccessTools.FieldRef<Inventory, int> InvHeight =
            AccessTools.FieldRefAccess<Inventory, int>("m_height");

        public static readonly AccessTools.FieldRef<FishingFloat, ZNetView> FloatNView =
            AccessTools.FieldRefAccess<FishingFloat, ZNetView>("m_nview");
        public static readonly AccessTools.FieldRef<FishingFloat, float> FloatLineLength =
            AccessTools.FieldRefAccess<FishingFloat, float>("m_lineLength");
        public static readonly AccessTools.FieldRef<FishingFloat, Fish> FloatNibbler =
            AccessTools.FieldRefAccess<FishingFloat, Fish>("m_nibbler");
        public static readonly Func<FishingFloat, Character> FloatGetOwner =
            AccessTools.MethodDelegate<Func<FishingFloat, Character>>(AccessTools.Method(typeof(FishingFloat), "GetOwner"));

        public static readonly AccessTools.FieldRef<BaseAI, ZNetView> AINView =
            AccessTools.FieldRefAccess<BaseAI, ZNetView>("m_nview");
        public static readonly AccessTools.FieldRef<BaseAI, Character> AICharacter =
            AccessTools.FieldRefAccess<BaseAI, Character>("m_character");
    }
}
