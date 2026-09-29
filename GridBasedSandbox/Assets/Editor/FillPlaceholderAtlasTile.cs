#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  FillPlaceholderTile — One-shot editor utility that fills the reserved
//  placeholder tile (3,3) in the block atlas with white pixels so vertex-
//  color tinting works correctly on placeholder blocks.
//
//  Menu: GridBasedSandbox > Voxel World > Fill Placeholder Atlas Tile
//
//  This writes directly to the atlas texture asset and reimports it.
//  Run once after initial setup or whenever the atlas is replaced.
// ─────────────────────────────────────────────────────────────────────────────

public static class FillPlaceholderAtlasTile
{
    private const string AtlasPath = "Assets/Textures/Block Atlas/Block Atlast.png";
    private const int TileCol = 3;
    private const int TileRow = 3;

    [MenuItem("GridBasedSandbox/Voxel World/Fill Placeholder Atlas Tile")]
    public static void Fill()
    {
        // Make the texture readable
        var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[FillPlaceholderTile] Texture not found at {AtlasPath}");
            return;
        }

        bool wasReadable = importer.isReadable;
        if (!wasReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        if (texture == null)
        {
            Debug.LogError($"[FillPlaceholderTile] Failed to load texture at {AtlasPath}");
            return;
        }

        // Determine tile size from atlas dimensions (assumes square atlas, square tiles)
        // Atlas is NxN tiles. We need to figure out N. For a 4x4 grid on a 64x64 texture,
        // each tile is 16x16. Detect automatically from VoxelBlockType.PlaceholderTile coords.
        int atlasWidth  = texture.width;
        int atlasHeight = texture.height;

        // Assume 4 tiles per row/col (matching VoxelWorldSettings.atlasSize default)
        int atlasTileCount = 4;
        int tileW = atlasWidth / atlasTileCount;
        int tileH = atlasHeight / atlasTileCount;

        // The atlas uses top-left origin for tile rows, but Texture2D uses bottom-left.
        // Tile (3,3) in atlas space (col=3, row=3 from top) → pixel Y starts at bottom.
        int pixelX = TileCol * tileW;
        int pixelY = atlasHeight - (TileRow + 1) * tileH; // flip Y

        // Fill with white
        Color white = Color.white;
        for (int x = pixelX; x < pixelX + tileW; x++)
        for (int y = pixelY; y < pixelY + tileH; y++)
        {
            texture.SetPixel(x, y, white);
        }

        texture.Apply();

        // Write back to disk
        byte[] pngData = texture.EncodeToPNG();
        System.IO.File.WriteAllBytes(
            System.IO.Path.GetFullPath(AtlasPath), pngData);

        // Restore read/write setting
        if (!wasReadable)
        {
            importer.isReadable = false;
        }

        // Ensure point filtering for pixel art
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        Debug.Log($"[FillPlaceholderTile] Filled tile ({TileCol},{TileRow}) with white in {AtlasPath}.");
    }
}
#endif
