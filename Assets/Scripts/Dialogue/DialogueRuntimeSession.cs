namespace Rpg.Dialogue
{
    /// <summary>Stable id for one Unity play (enter Play Mode → exit). Used to tell "same session" from a relaunch.</summary>
    public static class DialogueRuntimeSession
    {
        public static readonly string PlayInstanceId = System.Guid.NewGuid().ToString("N");
    }
}
