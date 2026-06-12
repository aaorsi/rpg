using UnityEngine;

namespace Rpg.Dialogue
{
    public static class DialogueTelemetry
    {
        public static void Log(string eventName, string details = null)
        {
            if (string.IsNullOrWhiteSpace(details))
                Debug.Log($"[DialogueTelemetry] {eventName}");
            else
                Debug.Log($"[DialogueTelemetry] {eventName} | {details}");
        }
    }
}
