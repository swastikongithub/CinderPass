namespace CinderPass.EditorTools
{
    /// <summary>Every folder the tooling reads from or writes to, in one place.</summary>
    public static class CinderPaths
    {
        public const string Root = "Assets/CinderPass";
        public const string Scene = Root + "/Scenes/CinderPass.unity";

        public const string ThirdParty = Root + "/Art/ThirdParty/PolyHaven";
        public const string GroundTextures = ThirdParty + "/Textures/Ground";
        public const string FoliageTextures = ThirdParty + "/Textures/Foliage";
        public const string Models = ThirdParty + "/Models";
        public const string SkyHdri = ThirdParty + "/Sky/kloofendal_38d_partly_cloudy_puresky_4k.hdr";

        public const string Shaders = Root + "/Art/Shaders";
        public const string Generated = Root + "/Art/Generated";
        public const string GenTextures = Generated + "/Textures";
        public const string GenMeshes = Generated + "/Meshes";
        public const string GenTerrain = Generated + "/Terrain";
        public const string Materials = Root + "/Art/Materials";
        public const string TerrainLayers = Root + "/Art/TerrainLayers";

        public const string Prefabs = Root + "/Prefabs";
        public const string EnvPrefabs = Prefabs + "/Environment";
        public const string VfxPrefabs = Prefabs + "/VFX";
        public const string Data = Root + "/Data";
        public const string Audio = Root + "/Audio";
        public const string Fonts = Root + "/UI/Fonts";
        public const string Settings = Root + "/Settings";
    }
}
