using UnityEditor;
using UnityEngine;

namespace Greenside.EditorTools
{
    /// <summary>
    /// Generates a starter catalog of 14 Club assets so we don't hand-author them.
    /// Idempotent: skips any club that already exists, so re-running it after you
    /// add new presets only creates the missing ones (and never clobbers tuning
    /// you've changed on existing assets).
    ///
    /// Run via: Greenside > Build Starter Club Catalog.
    /// </summary>
    public static class ClubCatalogBuilder
    {
        private const string ParentFolder = "Assets/ScriptableObjects";
        private const string CatalogFolder = ParentFolder + "/Clubs";

        private readonly struct Preset
        {
            public readonly string Name;
            public readonly ClubType Type;
            public readonly float Loft, MaxSpeed, MinSpeed, Spin;

            public Preset(string name, ClubType type, float loft, float maxSpeed, float minSpeed, float spin)
            {
                Name = name; Type = type; Loft = loft; MaxSpeed = maxSpeed; MinSpeed = minSpeed; Spin = spin;
            }
        }

        // Loft/speed/spin are arcade starting points — tune freely on the assets.
        private static readonly Preset[] Presets =
        {
            new Preset("Driver",         ClubType.Wood,   10.5f, 75f, 22f, 0.70f),
            new Preset("3 Wood",         ClubType.Wood,   15f,   70f, 21f, 0.75f),
            new Preset("5 Wood",         ClubType.Wood,   18f,   66f, 20f, 0.80f),
            new Preset("4 Hybrid",       ClubType.Hybrid, 22f,   62f, 19f, 0.85f),
            new Preset("5 Iron",         ClubType.Iron,   27f,   60f, 18f, 0.95f),
            new Preset("6 Iron",         ClubType.Iron,   30f,   57f, 17f, 1.00f),
            new Preset("7 Iron",         ClubType.Iron,   34f,   54f, 16f, 1.05f),
            new Preset("8 Iron",         ClubType.Iron,   38f,   51f, 15f, 1.10f),
            new Preset("9 Iron",         ClubType.Iron,   42f,   48f, 14f, 1.15f),
            new Preset("Pitching Wedge", ClubType.Wedge,  46f,   45f, 13f, 1.25f),
            new Preset("Gap Wedge",      ClubType.Wedge,  50f,   42f, 12f, 1.30f),
            new Preset("Sand Wedge",     ClubType.Wedge,  54f,   39f, 11f, 1.35f),
            new Preset("Lob Wedge",      ClubType.Wedge,  58f,   36f, 10f, 1.40f),
            new Preset("Putter",         ClubType.Putter, 0f,    14f,  3f, 0.00f),
        };

        [MenuItem("Greenside/Build Starter Club Catalog")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(ParentFolder))
                AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
            if (!AssetDatabase.IsValidFolder(CatalogFolder))
                AssetDatabase.CreateFolder(ParentFolder, "Clubs");

            int created = 0, skipped = 0;
            foreach (var p in Presets)
            {
                string path = $"{CatalogFolder}/{p.Name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Club>(path) != null) { skipped++; continue; }

                var club = ScriptableObject.CreateInstance<Club>();
                club.displayName = p.Name;
                club.type = p.Type;
                club.loftDegrees = p.Loft;
                club.maxLaunchSpeed = p.MaxSpeed;
                club.minLaunchSpeed = p.MinSpeed;
                club.spinFactor = p.Spin;
                AssetDatabase.CreateAsset(club, path);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Greenside] Club catalog: {created} created, {skipped} already existed, in {CatalogFolder}.");
        }
    }
}
