namespace Rpg.Core
{
    public static class GameConstants
    {
        public const string PlayerTag = "Player";
        public const string DefaultOllamaSettingsResource = "DefaultOllamaSettings";
        public const string DefaultNpcResource = "DefaultNpc";
        /// <summary>Resources path (no extension) to bootstrap player mesh when <see cref="RuntimeLevelBootstrap"/> has no override.</summary>
        public const string DefaultBootstrapPlayerResource = "RpgBootstrap/Tiger_001";
        /// <summary>Resources path (no extension) to bootstrap NPC mesh when <see cref="RuntimeLevelBootstrap"/> has no override.</summary>
        public const string DefaultBootstrapNpcResource = "RpgBootstrap/Kitty_001";
        /// <summary>Resources subfolder (no trailing slash) with copies of Animals FREE prefabs for runtime <see cref="Resources.LoadAll{T}"/>.</summary>
        public const string AnimalsFreeResourcesFolder = "AnimalsFree";
        /// <summary>Resources subfolder for Mixamo-derived character prefabs (random player when enabled in bootstrap).</summary>
        public const string MixamoCharactersResourcesFolder = "Mixamo/Characters";
        /// <summary>Resources subfolder for CityPeople gameplay character prefabs.</summary>
        public const string CityPeopleCharactersResourcesFolder = "CityPeopleCharacters";
        /// <summary>Resources subfolder for shared Mixamo animation clip assets (idle/walk FBX imports).</summary>
        public const string MixamoAnimationsResourcesFolder = "Mixamo/Animations";
        /// <summary>Resources subfolder with mesh FBX copies used to load embedded animation clips for casual human locomotion.</summary>
        public const string CasualHumanMeshBasesResourcesFolder = "CasualHumanMeshBases";
        /// <summary>Resources subfolder with <c>npc_csl_00_character_*</c> prefabs for runtime bootstrap.</summary>
        public const string NpcCasualCharactersResourcesFolder = "NpcCasualCharacters";
        /// <summary>Fallback casual character when the folder is empty.</summary>
        public const string DefaultCasualCharacterResource = "NpcCasualCharacters/npc_csl_00_character_01f_01";
        public const string DialogueTemplatesSubfolder = "Dialogue";
        /// <summary>Canonical inventory id for a collectible live chicken (world pickup + eat from inventory).</summary>
        public const string LiveChickenItemId = "live_chicken";
    }
}
