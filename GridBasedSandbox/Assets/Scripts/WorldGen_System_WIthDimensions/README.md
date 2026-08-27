# WorldGen System — Modular Procedural World Generation

> **Target audience:** Game developers and technical designers.  
> **Prerequisite:** Familiarity with the existing `GridBasedSandbox` voxel system.

---

## Overview

This system adds a fully modular, **dimension-aware**, **infinite** world generation pipeline on top of the existing voxel engine.  No existing scripts are modified — the new code is additive.

The architecture is intentionally shaped like Minecraft's dimension system:  
one **active dimension** drives everything, and switching dimensions flushes the world and reloads with a completely different generator and pass stack.

---

## File Map

```
Assets/Scripts/WorldGeneration/
│
├── Core/
│   ├── IWorldGenerator.cs            ← Contract for all generators
│   ├── IGenerationPass.cs            ← Contract for all post-process passes
│   ├── BaseWorldGeneratorAsset.cs    ← Abstract SO bridge for generators
│   ├── BaseGenerationPassAsset.cs    ← Abstract SO bridge for passes
│   ├── WorldGenNoiseUtils.cs         ← Shared noise helpers (FBM, hashing)
│   ├── VoxelWorldManagerExtended.cs  ← Drop-in upgrade of VoxelWorldManager
│   └── WorldGenDebugOverlay.cs       ← Runtime GUI + dimension switch buttons
│
├── Dimensions/
│   ├── DimensionProfile.cs           ← SO: one profile per dimension
│   └── DimensionRegistry.cs          ← SO: catalogue of all profiles; handles switching
│
├── Generators/
│   ├── OverworldGeneratorAsset.cs    ← Wraps existing VoxelTerrainGenerator
│   └── BackroomsGeneratorAsset.cs    ← Backrooms world generator
│
├── Structures/
│   ├── BackroomsStructureConfig.cs   ← SO: one config per structure variant
│   └── BackroomsLayout.cs            ← Pure layout engine (no Unity API)
│
└── Passes/
    └── BackroomsConnectorPassAsset.cs ← Pass that carves cross-chunk doorways
```

---

## Architecture

```
DimensionRegistry (SO)
    │
    ├── dimensions[0]: DimensionProfile "overworld"
    │       worldSettings  ──► VoxelWorldSettings (existing SO)
    │       generatorAsset ──► OverworldGeneratorAsset
    │       passes         ──► []  (none)
    │
    └── dimensions[1]: DimensionProfile "backrooms_level0"
            worldSettings  ──► BackroomsVoxelWorldSettings
            generatorAsset ──► BackroomsGeneratorAsset
            passes         ──► [BackroomsConnectorPassAsset]

                            ▼  (active at runtime)
                    IWorldGenerator.Generate()    ← fills blank chunk
                    IGenerationPass[0].Apply()    ← punches cross-chunk doors
                    IGenerationPass[1].Apply()    ← (future: prop placement, etc.)
                            ▼
                    VoxelMeshBuilder.Build()      ← existing, untouched
                            ▼
                    VoxelChunk.ApplyMesh()        ← existing, untouched


VoxelWorldManagerExtended
    │  subscribes to DimensionRegistry.OnDimensionChanged
    │
    ├── Streaming (identical to VoxelWorldManager)
    ├── Chunk pool + rebuild queue (identical)
    ├── IChunkNeighbourSampler (identical)
    └── NEW: calls registry.ActiveGenerator.Generate()
             then foreach pass in registry.ActivePasses: pass.Apply()
             then notifies GridSystem of occupied cells
```

### GridSystem Integration

Every solid block written during generation is automatically registered with `GridSystem.PlaceObjectInCell()`.  When a block is added/removed at runtime via `TrySetBlock()`, the GridSystem cell is updated accordingly.  This means:

- `GridSystem.IsCellOccupied()` reflects voxel world state accurately.
- You can query `GetTopSolidY(wx, wz)` then `GridSystem.GridToWorldPosition()` to place items on top of terrain, exactly as documented in the existing README.

---

## Infinite World & Determinism

Every dimension is infinite.  Chunk generation is **seeded and deterministic**:

- The same `(chunkX, chunkZ, seed)` triple always produces the same chunk.
- Load order does not affect output.
- The `BackroomsConnectorPass` derives doorway positions from the **shared edge chunk coordinate** so adjacent chunks always carve matching openings — even if they're generated seconds apart.

---

## Switching Dimensions

```csharp
// From any MonoBehaviour:
worldManagerExtended.SwitchDimension("backrooms_level0");

// Or directly through the registry:
dimensionRegistry.SetActiveDimension("overworld");
```

On switch:
1. `OnDimensionWillChange` UnityEvent fires (hook your loading screen here).
2. All active chunks are returned to the pool.
3. All cached chunk data is **cleared** (new dimension = new world).
4. The new generator + passes are instantiated from the profile.
5. The player is teleported to `DimensionProfile.defaultSpawnPosition`.
6. Streaming restarts around the player.
7. `OnDimensionChanged` UnityEvent fires.

---

## Adding a New Dimension

1. **Create block types** (`Assets > Create > VoxelWorld > Block Type`) for any new block species.
2. **Create or reuse a Block Registry** (`VoxelWorld > Block Registry`).
3. **Create a World Settings SO** (`VoxelWorld > World Settings`) — tune chunk size, height range, material.
4. **Create a Generator SO** (`WorldGen > Generators > …`) — pick Overworld or Backrooms, or write your own.
5. **Create pass SOs** as needed (`WorldGen > Passes > …`).
6. **Create a Dimension Profile** (`WorldGen > Dimension Profile`) — wire everything together.
7. **Add the profile** to your DimensionRegistry asset's `dimensions` list.
8. **Switch to it** at runtime via `SwitchDimension("your_id")`.

---

## Adding a New Generator Type

```csharp
// 1. Implement the interface
public sealed class MyGenerator : IWorldGenerator
{
    public void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed)
    {
        // Fill data.SetBlock(lx, ly, lz, blockId) as needed.
        // No Unity API calls. Pure math only.
    }
}

// 2. Create the SO factory
[CreateAssetMenu(menuName = "WorldGen/Generators/My Generator")]
public class MyGeneratorAsset : BaseWorldGeneratorAsset
{
    [Header("Config")]
    public float mySetting = 1f;

    public override IWorldGenerator CreateGenerator()
        => new MyGenerator(mySetting);
}
```

That's it.  Create the asset, assign it to a DimensionProfile, done.

---

## Adding a New Generation Pass

```csharp
// 1. Implement the interface
public sealed class MyPass : IGenerationPass
{
    public string PassName => "My Pass";

    public void Apply(VoxelChunkData data, VoxelWorldSettings settings, int seed)
    {
        // Modify data after the generator runs.
        // No Unity API calls. Pure math only.
    }
}

// 2. Create the SO factory
[CreateAssetMenu(menuName = "WorldGen/Passes/My Pass")]
public class MyPassAsset : BaseGenerationPassAsset
{
    public float myParam = 0.5f;

    public override IGenerationPass CreatePass()
        => new MyPass(myParam);
}
```

Add the asset to `DimensionProfile.passes` in any position.  Passes run top-to-bottom.

---

## Backrooms Structure Config Fields

| Field                  | Meaning |
|------------------------|---------|
| `structureType`        | `Corridor`, `Room`, `PillarRoom`, `CrawlSpace` |
| `spawnWeight`          | Probability weight (relative to sum of all weights) |
| `lengthRange`          | Min/max blocks along primary axis |
| `widthRange`           | Min/max blocks along secondary axis |
| `heightRange`          | Min/max interior height (floor-to-ceiling, in blocks) |
| `pillarSpacing`        | (PillarRoom) Grid spacing between pillar centres |
| `pillarRadius`         | (PillarRoom) Pillar half-size in blocks |
| `floorBlockId`         | Block ID for floor slab |
| `ceilingBlockId`       | Block ID for ceiling slab |
| `wallBlockId`          | Block ID for perimeter walls |
| `pillarBlockId`        | Block ID for pillar columns |
| `allowRotation`        | If true, structure may be rotated 90° |
| `ceilingVariationChance` | Probability [0–1] of extra ceiling notch |
| `wallAlcoveChance`     | Probability [0–1] of recessed wall alcove |

---

## Recommended Backrooms Block IDs

| ID | Name              | Atlas Tile | Notes |
|----|-------------------|-----------|-------|
|  0 | Air               | —         | Required; `isSolid = false` |
| 10 | BackroomsFloor    | (0, 0)    | Yellow carpet / linoleum |
| 11 | BackroomsCeiling  | (1, 0)    | Drop-ceiling / fluorescent tiles |
| 12 | BackroomsWall     | (2, 0)    | Yellow wallpaper |
| 13 | BackroomsPillar   | (3, 0)    | Concrete column |
| 14 | BackroomsVoid     | (0, 1)    | Dark fill between rooms |

---

## Performance Notes

- All generation code (`IWorldGenerator.Generate`, `IGenerationPass.Apply`, `VoxelMeshBuilder.Build`) is pure C# with no Unity API calls.  These are eligible for `Task.Run` background threading when you're ready to add it — only `VoxelChunk.ApplyMesh` and `VoxelChunk.SetData` must stay on the main thread.
- The `BackroomsConnectorPass` runs after generation and is O(chunk perimeter) — negligible cost.
- The GridSystem notification in `NotifyGridSystemForChunk` is O(chunk volume) and runs once per newly generated chunk.  If this causes hitching with large chunks, it can be moved to a coroutine or background thread with a main-thread dispatch for the `PlaceObjectInCell` calls.

---

## Troubleshooting

| Symptom | Likely Cause |
|---------|-------------|
| Chunks are fully solid (no rooms) | `BackroomsGeneratorAsset.structureConfigs` is empty |
| Rooms don't connect between chunks | `BackroomsConnectorPassAsset` not added to passes list |
| Player falls through floor on dimension switch | `defaultSpawnPosition.y` too low — set to at least `1` |
| `DimensionRegistry` log: "Unknown dimension id" | `dimensionId` string mismatch — check for typos / extra spaces |
| Blocks are wrong colour / missing | Block IDs in structure configs don't match VoxelBlockRegistry entries |
| GridSystem not tracking voxel blocks | `gridSystem` field on VoxelWorldManagerExtended not assigned |
