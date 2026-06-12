using UnityEngine;

namespace Rpg.Core
{
    /// <summary>
    /// Default hero prefabs for player builds when nothing lives under Resources/StylizedCharacterPack/Characters.
    /// Loaded from Resources/Bootstrap/StandaloneAvatarLineup.
    /// </summary>
    public sealed class StandaloneAvatarLineup : ScriptableObject
    {
        public GameObject[] lineupPrefabs;
    }
}
