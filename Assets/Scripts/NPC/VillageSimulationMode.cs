namespace Rpg.Npc
{
    /// <summary>
    /// Controls how village autonomy runs. Option A uses <see cref="SystemicOnly"/>.
    /// </summary>
    public enum VillageSimulationMode
    {
        LegacyInteractionFsm = 0,
        SystemicOnly = 1,
    }
}
