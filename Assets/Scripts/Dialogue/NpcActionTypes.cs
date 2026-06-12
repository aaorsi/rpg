namespace Rpg.Dialogue
{
    /// <summary>
    /// Canonical NPC proposed-action type strings. Keep in sync with
    /// <c>PolicyRegistry</c> allow-lists in
    /// <c>services/policy_orchestrator/app/policies.py</c>.
    /// </summary>
    public static class NpcActionTypes
    {
        public const string MoveToLocation = "move_to_location";
        public const string MoveToNpc = "move_to_npc";
        public const string GiveObject = "give_object";
        public const string ReceiveObject = "receive_object";
        public const string Trade = "trade";
        public const string ActivateObject = "activate_object";
        public const string FindObject = "find_object";
        public const string InspectLocation = "inspect_location";
        public const string ReferToNpc = "refer_to_npc";
        public const string FollowHero = "follow_hero";
    }
}
