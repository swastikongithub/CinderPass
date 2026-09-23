using System;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// The level design as data. The route is authored first (it is gameplay infrastructure) and every
    /// terrain/biome/hazard feature is placed relative to it. Rebuild the world after editing this asset.
    /// All positions are world metres; the playable terrain spans [0, size] on X and Z.
    /// </summary>
    [CreateAssetMenu(menuName = "Cinder Pass/World Design", fileName = "WorldDesign")]
    public sealed class WorldDesign : ScriptableObject
    {
        [Serializable]
        public struct RouteKnot
        {
            public string label;
            [Tooltip("x, road surface height, z")]
            public Vector3 position;
            [Tooltip("Keep surrounding terrain high (steep canyon walls) instead of conforming it to the road.")]
            public bool canyon;
        }

        [Serializable]
        public struct LavaPool
        {
            public string label;
            public Vector2 centre;
            [Min(1f)] public float radius;
            [Tooltip("How far the lava sits below the surrounding ground.")]
            [Min(0f)] public float sunken;
            [Range(0f, 2f)] public float activity;
        }

        [Header("Terrain")]
        public int seed = 20260923;
        public float size = 1024f;
        public float heightScale = 330f;
        public int heightmapResolution = 1025;
        public int outerHeightmapResolution = 257;
        public int alphamapResolution = 1024;
        public int detailResolution = 1024;
        [Tooltip("Base elevation of the rolling lowlands.")]
        public float baseHeight = 26f;

        [Header("Route")]
        [Min(3f)] public float roadHalfWidth = 5f;
        [Tooltip("Index of the knot where the introduction hands control to the player.")]
        public int introHandoverKnot = 10;
        public RouteKnot[] route =
        {
            K("Pine Hollow basecamp", 232, 24, 196),
            K("Camp track", 292, 25, 226),
            K("Forest", 346, 29, 268),
            K("Forest climb", 380, 34, 328),
            K("Forest edge", 396, 40, 398),
            K("Highlands", 430, 47, 462),
            K("Boulder field", 482, 54, 514),
            K("Canyon mouth", 528, 55, 560, true),
            K("Canyon", 560, 57, 608, true),
            K("Canyon exit", 598, 58, 652, true),
            K("Ember Overlook", 640, 58, 694),
            K("Descent", 690, 54, 712),
            K("Descent bend", 732, 47, 738),
            K("Basin floor", 770, 41, 768),
            K("Lava field", 816, 39, 774),
            K("Cauldron approach", 862, 40, 764),
            K("Ash flats", 902, 43, 740),
            K("Ash plains", 924, 48, 692),
            K("East saddle", 914, 56, 630),
            K("Saddle descent", 892, 52, 556),
            K("Dry basin", 850, 45, 480),
            K("Scorched fringe", 790, 38, 408),
            K("Grass plains", 712, 32, 338),
            K("Rolling hills", 620, 30, 272),
            K("Southern meadow", 526, 28, 214),
            K("Forest south", 432, 26, 166),
            K("Hollow approach", 340, 25, 146),
            K("Camp return", 276, 24, 160),
        };

        [Header("Landforms")]
        public Vector2 basecamp = new Vector2(232, 196);
        public float basecampRadius = 58f;
        public Vector2 volcano = new Vector2(882, 1004);
        public float volcanoRadius = 275f;
        public float volcanoHeight = 215f;
        public float craterRadius = 44f;
        public float craterDepth = 34f;
        public Vector2 basin = new Vector2(800, 782);
        public float basinRadius = 205f;
        public float basinFloor = 38f;
        [Tooltip("Crest line of the Great Ridge separating the forest from the volcanic basin.")]
        public Vector2[] ridge = { new Vector2(-120, 700), new Vector2(160, 652), new Vector2(380, 612), new Vector2(560, 604), new Vector2(720, 592), new Vector2(830, 560), new Vector2(880, 520) };
        [Tooltip("Crest height above the lowlands at each ridge vertex (the canyon cuts a saddle).")]
        public float[] ridgeHeights = { 70f, 105f, 118f, 72f, 110f, 78f, 40f };
        public float ridgeWidth = 82f;

        [Header("Volcanic features")]
        public LavaPool[] lavaPools =
        {
            new LavaPool { label = "The Cauldron", centre = new Vector2(742, 812), radius = 19f, sunken = 2.4f, activity = 1.3f },
            new LavaPool { label = "Vent A", centre = new Vector2(795, 748), radius = 6.5f, sunken = 1.4f, activity = 1.1f },
            new LavaPool { label = "Vent B", centre = new Vector2(846, 740), radius = 4.5f, sunken = 1.2f, activity = 0.9f },
            new LavaPool { label = "Vent C", centre = new Vector2(893, 777), radius = 8.5f, sunken = 1.6f, activity = 1.2f },
            new LavaPool { label = "Vent D", centre = new Vector2(724, 766), radius = 5f, sunken = 1.3f, activity = 0.8f },
            new LavaPool { label = "Vent E", centre = new Vector2(944, 722), radius = 6f, sunken = 1.4f, activity = 1f },
        };
        [Tooltip("Lava river from the volcano's flank down into the Cauldron (upstream first).")]
        public Vector2[] lavaRiver = { new Vector2(872, 830), new Vector2(846, 812), new Vector2(818, 802), new Vector2(790, 800), new Vector2(764, 806), new Vector2(752, 810) };
        public float lavaRiverHalfWidth = 3.6f;
        public Vector2[] fumaroles = { new Vector2(936, 700), new Vector2(906, 664), new Vector2(946, 648), new Vector2(880, 812), new Vector2(962, 760) };

        static RouteKnot K(string label, float x, float y, float z, bool canyon = false) =>
            new RouteKnot { label = label, position = new Vector3(x, y, z), canyon = canyon };
    }
}
