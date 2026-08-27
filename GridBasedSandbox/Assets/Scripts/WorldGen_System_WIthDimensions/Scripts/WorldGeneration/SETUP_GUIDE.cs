// ═══════════════════════════════════════════════════════════════════════════════
//  BackroomsWorldSettings — README
//
//  This file describes what to configure on a VoxelWorldSettings SO that is
//  dedicated to the Backrooms dimension.  It is NOT a code file — read it
//  alongside VoxelWorldSettings.cs and create the SO in the Unity Editor.
//
//  ── RECOMMENDED SETTINGS ──────────────────────────────────────────────────
//
//  chunkWidth        : 16     (or 32 for larger rooms — larger = less seaming)
//  chunkHeight       : 9      (set minHeight = 0, maxHeight = 8 for flat world)
//  minHeight         : 0      (Backrooms are flat — no terrain elevation)
//  maxHeight         : 8      (gives 9 vertical slices, enough for 5-tall rooms)
//  viewDistanceInChunks : 4   (adjust based on target platform performance)
//  maxChunkBuildsPerFrame : 2
//  seed              : 0      (random each session — or pick a fixed number)
//  noiseScale        : 60     (unused by BackroomsGenerator but keep it set)
//  atlasSize         : 4      (4×4 = 16 block slots — more than enough)
//  chunkMaterial     : <your backrooms material with atlas texture>
//
//  ── RECOMMENDED BLOCK IDS ─────────────────────────────────────────────────
//
//  Create one VoxelBlockType SO per row below and add all to a VoxelBlockRegistry:
//
//  ID | Name              | isSolid | Notes
//  ───┼───────────────────┼─────────┼────────────────────────────────────────
//   0 | Air               | false   | Required
//  10 | BackroomsFloor    | true    | Yellow linoleum / carpet; tile (0,0) in atlas
//  11 | BackroomsCeiling  | true    | Drop ceiling / white panels; tile (1,0)
//  12 | BackroomsWall     | true    | Yellow wallpaper; tile (2,0) (all faces same)
//  13 | BackroomsPillar   | true    | Concrete; tile (3,0)
//  14 | BackroomsVoid     | true    | Very dark fill between rooms; tile (0,1)
//
//  ── ATLAS LAYOUT (4×4, 16 tiles) ──────────────────────────────────────────
//
//   col→  0          1         2        3
//  row↓
//   0     Floor     Ceiling   Wall     Pillar
//   1     Void      <free>    <free>   <free>
//   2     <free>    <free>    <free>   <free>
//   3     <free>    <free>    <free>   <free>
//
//  ── STRUCTURE CONFIGS TO CREATE ───────────────────────────────────────────
//
//  Create each via Assets > Create > WorldGen > Backrooms > Structure Config,
//  then add all to BackroomsGeneratorAsset.structureConfigs.
//
//  Name            | Type       | Weight | Length  | Width  | Height | Notes
//  ────────────────┼────────────┼────────┼─────────┼────────┼────────┼────────
//  MainCorridor    | Corridor   |  30    | 10–24   | 3–5    | 3–4    | Common hallway
//  WideCorridor    | Corridor   |  10    |  8–16   | 5–8    | 4–5    | Wider variant
//  SmallRoom       | Room       |  20    |  6–10   | 6–10   | 3–4    | Typical room
//  LargeRoom       | Room       |  10    | 12–20   | 12–20  | 4–5    | Open area
//  PillarRoom      | PillarRoom |  10    | 12–24   | 12–24  | 4–5    | Colonnades
//  CrawlSpace      | CrawlSpace |   5    |  6–14   | 2–3    | 1–2    | Low passage
//
//  ── DimensionProfile SETUP ────────────────────────────────────────────────
//
//  Create via Assets > Create > WorldGen > Dimension Profile
//    dimensionId       : "backrooms_level0"
//    displayName       : "Backrooms — Level 0"
//    worldSettings     : <your BackroomsWorldSettings SO>
//    generatorAsset    : <your BackroomsGeneratorAsset SO>
//    passes[0]         : <BackroomsConnectorPassAsset SO>  ← ensures cross-chunk doors
//    defaultSpawnPosition : (8, 1, 8)   ← lands player inside a generated room
//
//  ── DimensionRegistry SETUP ───────────────────────────────────────────────
//
//  Create via Assets > Create > WorldGen > Dimension Registry
//    dimensions[0] : <OverworldDimensionProfile>
//    dimensions[1] : <BackroomsDimensionProfile>
//    startingDimensionId : "overworld"   (or "backrooms_level0" to start there)
//
// ═══════════════════════════════════════════════════════════════════════════════
