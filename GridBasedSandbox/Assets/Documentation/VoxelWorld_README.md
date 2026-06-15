# Voxel World System — Setup & Usage Guide

This system adds a Minecraft-style, chunked, infinite-ish voxel world (Y range −4 to 4 by default) that runs **alongside** your existing `GridSystem` / `SimpleObjectPlacer` / `InventoryManager` setup without modifying any of them.

---

## 1. The Big Picture — How the Pieces Fit Together

```
VoxelWorldSettings (ScriptableObject)
        │  (holds all tunables + reference to registry)
        ▼
VoxelBlockRegistry (ScriptableObject)
        │  (list of VoxelBlockType assets, id → definition)
        ▼
VoxelBlockType (ScriptableObject, one per block species)

VoxelWorldManager (MonoBehaviour, lives in scene)
   ├─ owns VoxelChunkData  (pure data, byte[,,] per chunk)
   ├─ owns VoxelChunk      (MonoBehaviour: MeshFilter/Renderer/Collider)
   ├─ calls VoxelTerrainGenerator.Generate()  → fills chunk data
   ├─ calls VoxelMeshBuilder.Build()          → turns data into a mesh
   └─ implements IChunkNeighbourSampler       → lets mesh builder see across chunks

VoxelBlockPlacer (MonoBehaviour, player-facing)
   └─ raycasts → calls VoxelWorldManager.TrySetBlock()
```

Nothing here touches `GridSystem.cs`, `SimpleObjectPlacer.cs`, or `InventoryManager.cs`. The voxel system is a **parallel, independent layer** that shares the same 1-unit-per-cell coordinate convention so the two can coexist spatially.

### Why this architecture was chosen
- **New parallel scripts** (not a second `GridSystem` instance, not editing the original): your grid system is 2D-cell-based and object-oriented (one GameObject per placed item). A voxel world needs dense 3D block storage and per-chunk meshes — fundamentally different data shape. Reusing `GridSystem` would mean bolting 3D voxel storage onto a class never designed for it. A clean, purpose-built set of classes is easier to scale, test, and hand to other engineers.
- **Data/View separation**: `VoxelChunkData` (pure data, no Unity calls) is separate from `VoxelChunk` (the MonoBehaviour view). This lets terrain generation and mesh building run on background threads later if you need it, without touching scene objects.
- **ScriptableObject-driven config** (`VoxelWorldSettings`, `VoxelBlockRegistry`, `VoxelBlockType`): designers/engineers can add new block types or tune world generation without touching code.

---

## 2. Script-by-Script Reference

### VoxelBlockType.cs
A ScriptableObject describing **one block species** (Stone, Dirt, Grass, Sand, etc.).

Key fields:
- `blockId` (byte) — unique numeric ID stored in chunk data. **`0` is reserved for Air.**
- `blockName` — label for editor/debug.
- `isSolid` — if false, no mesh is generated for this block and it doesn't block neighbour faces (Air should be `isSolid = false`).
- `isTransparent` — for things like glass/water (not deeply used yet, but reserved for future culling rules).
- `defaultTile`, `tileTop/Bottom/Front/Back/Left/Right` — atlas tile coordinates (column, row). Per-face overrides let grass have a green top, dirt sides, dirt bottom, etc. Leave a face override at `(-1, 0)` to fall back to `defaultTile`.

**You create one of these per block type** via `Assets > Create > VoxelWorld > Block Type`.

---

### VoxelBlockRegistry.cs
A ScriptableObject that's basically a lookup table: `byte id → VoxelBlockType`.

- Holds a `List<VoxelBlockType> blocks` you fill in the Inspector.
- `Initialize()` builds an internal dictionary — called once by `VoxelWorldManager.Awake()`.
- `GetBlock(id)` — returns the `VoxelBlockType`, or null for Air/unknown.
- `IsSolid(id)` — quick check used heavily by the mesh builder for face culling.

**You create exactly ONE of these per project** via `Assets > Create > VoxelWorld > Block Registry`, then drag all your `VoxelBlockType` assets into its `blocks` list. **Block ID 0 (Air) must be in this list.**

---

### VoxelWorldSettings.cs
The single ScriptableObject that holds every tunable knob. Create via `Assets > Create > VoxelWorld > World Settings`.

Important fields and what they mean:

| Field | Meaning |
|---|---|
| `blockRegistry` | Reference to your `VoxelBlockRegistry` asset |
| `chunkWidth` | Blocks per chunk on X/Z (16 is the Minecraft-like default) |
| `chunkHeight` | Total vertical slices = `maxHeight - minHeight + 1` |
| `minHeight` / `maxHeight` | World Y range. For your spec, **−4 to 4** (so `chunkHeight` should be **9**) |
| `viewDistanceInChunks` | Radius of chunks loaded around the player |
| `maxChunkBuildsPerFrame` | Throttle for mesh rebuilds — prevents frame hitches |
| `seed` | World seed; `0` = randomized at runtime |
| `noiseScale`, `octaves`, `persistence`, `lacunarity` | Perlin/fBm terrain shape controls |
| `atlasSize` | Number of tiles per row/column in your texture atlas (e.g. 4 = 4×4 = 16 tiles) |
| `chunkMaterial` | Material assigned to every chunk's MeshRenderer |

Helper methods (`TileUVSize`, `TotalWorldHeight`, `WorldYToLocal`, `LocalYToWorld`) are used internally — you don't need to call these yourself, but it's useful to know they exist if you extend the system.

**You create exactly ONE of these per "world profile"** (e.g. overworld vs. a test world) via `Assets > Create > VoxelWorld > World Settings`.

---

### VoxelChunkData.cs
Pure C# data class (no MonoBehaviour) — a 3D `byte[,,]` array for one chunk column, plus its chunk coordinate.

- `ChunkCoord` — (cx, cz) in chunk-space (NOT block-space).
- `WorldOriginX` / `WorldOriginZ` — the world block coordinate of this chunk's corner (`ChunkCoord * Width`).
- `GetBlock(lx, ly, lz)` / `SetBlock(lx, ly, lz, id)` — local-space access, bounds-checked.
- `WorldToLocal(wx, wy, wz, minHeight)` — converts world block coords to this chunk's local coords.
- `IsDirty` — flag set whenever a block changes; the world manager uses this to know a chunk needs remeshing.

You won't usually touch this directly — it's created and managed by `VoxelWorldManager`.

---

### VoxelTerrainGenerator.cs
Static class, no MonoBehaviour. `Generate(data, settings, seed)` fills a `VoxelChunkData` using layered Perlin noise (fractional Brownian motion).

How it works:
1. For each (x, z) column, samples fBm noise to get a `normalizedHeight` in [0,1].
2. Maps that to a `surfaceY` between `minHeight` and `maxHeight`.
3. Fills the column top-to-bottom: Air above surface (or Water if below sea level), Grass/Sand at the surface, a couple of Dirt layers below that, then Stone all the way down.

**Block ID constants are hardcoded** (`STONE=1, DIRT=2, GRASS=3, SAND=4, WATER=5`). These **must match the `blockId` values you set on your `VoxelBlockType` assets** — see Setup section below for the exact mapping you need to create.

To extend:
- **Biomes**: pass a biome SO and branch inside `AssignBlock`.
- **Caves**: add a 3D noise pass after the height pass.
- **Structures**: run a separate pass after `Generate()` returns.

---

### VoxelMeshBuilder.cs
Static class — converts `VoxelChunkData` into a `MeshData` struct using **greedy face culling** (only visible faces are emitted, exactly like Minecraft).

How it works:
1. Iterates every block in the chunk.
2. Skips Air and non-solid blocks.
3. For each of the 6 faces, checks the neighbouring block (possibly in an adjacent chunk via `IChunkNeighbourSampler`).
4. If the neighbour is non-solid, emits that face's quad with atlas UVs from `VoxelBlockType.GetTileForFace()`.
5. Returns `MeshData` (plain vertex/triangle/UV lists — no Unity API calls, so it's thread-safe).

`MeshData.ApplyToMesh(mesh)` must be called on the **main thread** — it pushes the arrays into a Unity `Mesh` and recalculates normals/bounds.

`IChunkNeighbourSampler` is the interface `VoxelWorldManager` implements so the builder can query "what's in the chunk next door" without a circular dependency.

---

### VoxelChunk.cs
MonoBehaviour — the **view** for one loaded chunk. Requires `MeshFilter`, `MeshRenderer`, `MeshCollider` (auto-added via `[RequireComponent]`).

- `Data` — the attached `VoxelChunkData` (read-only from outside).
- `SetData(data, settings, material)` — attaches data, positions the GameObject in world space, sets the material.
- `ApplyMesh(meshData)` — pushes mesh data onto the `MeshFilter`/`MeshCollider`. Main thread only.
- `Reset()` — clears everything for pooling/reuse.
- `TrySetBlockWorld(wx, wy, wz, id, settings)` — edits a block given **world** coordinates; converts to local, bounds-checks, marks dirty.
- `GetBlockWorld(...)` — reads a block at world coordinates.

This is the prefab component — see Setup below.

---

### VoxelWorldManager.cs
MonoBehaviour — the **brain**. One per scene.

Responsibilities:
1. **Streaming**: tracks the player's chunk coordinate (`WorldToChunkCoord`), and each frame checks if it changed. If so, computes the set of chunks that *should* be loaded (a square of radius `viewDistanceInChunks`), unloads chunks outside that set, and loads chunks newly inside it.
2. **Loading**: for a new chunk coordinate, either reuses cached `VoxelChunkData` (if the player previously visited and left) or generates new data via `VoxelTerrainGenerator.Generate()`. Then grabs a pooled `VoxelChunk` GameObject (or instantiates a new one) and calls `SetData()`.
3. **Unloading**: resets and disables the `VoxelChunk` GameObject, returns it to the pool. **Chunk data stays cached** (`_chunkDataCache`) so re-entering an area doesn't regenerate terrain.
4. **Rebuild queue**: `EnqueueRebuild(coord)` adds a chunk coordinate to a queue. Each frame, `ProcessRebuildQueue()` pops up to `maxChunkBuildsPerFrame` entries, calls `VoxelMeshBuilder.Build()`, and applies the result. This throttling avoids hitching when many chunks load at once (e.g. on spawn).
5. **Cross-chunk sampling** (`IChunkNeighbourSampler.GetBlockAt`): given any world block coordinate, finds the right chunk (active or cached) and returns its block id — used by the mesh builder for border faces.
6. **Block editing API** (`TrySetBlock(wx, wy, wz, id)`): edits a block in the relevant chunk, enqueues that chunk for rebuild, and **also enqueues neighbouring chunks if the edited block sits on a chunk border** (so seams update correctly).
7. **`GetTopSolidY(wx, wz)`**: returns the highest solid block's world Y at a given X/Z column — useful for placing GridSystem objects on top of voxel terrain.

Inspector fields you must assign:
- `settings` — your `VoxelWorldSettings` asset.
- `playerTransform` — the player or camera transform used for streaming.
- `chunkPrefab` — the chunk prefab (see Setup).

---

### VoxelBlockPlacer.cs
MonoBehaviour — **player input bridge**. A new, parallel placer separate from `SimpleObjectPlacer`. Both can run in the same scene.

- `PlaceBlock(byte blockId = 0)` — raycasts against chunk colliders, computes the position just outside the hit face (`hit.point + hit.normal * HIT_BIAS`), converts to block coordinates, and calls `worldManager.TrySetBlock(...)`. If `blockId` is 0, falls back to `defaultBlockId`.
- `RemoveBlock()` — raycasts, computes the position just inside the hit face (`hit.point - hit.normal * HIT_BIAS`), and sets that block to Air (id 0).
- `WorldPointToBlockCoord(point)` — static helper using `FloorToInt` on all axes (handles negative coordinates correctly — critical for an "infinite" world centered on origin).
- `IsActive` — toggle to enable/disable raycasting, e.g. from `GameInpurManager` when the player has a non-voxel item selected.

Inspector fields:
- `worldManager` — your `VoxelWorldManager`.
- `settings` — your `VoxelWorldSettings` (currently unused internally but reserved for future logic — keep it assigned).
- `playerCamera` — defaults to `Camera.main` if left empty.
- `maxReach` — raycast distance (default 6).
- `chunkLayer` — LayerMask that chunk colliders live on (**important** — see Setup).
- `defaultBlockId` — block placed when no inventory item provides one (default 3 = Grass).

**Inventory integration (zero changes to `InventoryManager`)**: if you add an optional `public byte voxelBlockId;` field to `ItemData.cs`, and the currently selected item has `voxelBlockId > 0`, pass that value into `PlaceBlock(blockId)`. This is the *only* place a one-line addition is needed, and it's additive/optional.

---

## 3. Step-by-Step Setup in Unity

### Step 1 — Create the Block Type assets
For each block species (Air, Stone, Dirt, Grass, Sand, Water — matching the generator's constants):

`Assets > Create > VoxelWorld > Block Type`

Set `blockId` to match `VoxelTerrainGenerator.cs`'s constants:

| Block | blockId | isSolid | Notes |
|---|---|---|---|
| Air | 0 | **false** | No tiles needed |
| Stone | 1 | true | |
| Dirt | 2 | true | |
| Grass | 3 | true | Set `tileTop` to a grass-top tile, `tileBottom`/sides to dirt tile |
| Sand | 4 | true | |
| Water | 5 | false or true* | *Set `isSolid=false` if you want it non-blocking, or true if you want it walkable-on for now. `isTransparent=true` either way. |

For each, set `defaultTile` (and any per-face overrides) to the (column, row) coordinates of that block's texture in your atlas.

### Step 2 — Create the Block Registry
`Assets > Create > VoxelWorld > Block Registry` → drag all 6 `VoxelBlockType` assets into the `blocks` list (including Air, id 0).

### Step 3 — Create the World Settings
`Assets > Create > VoxelWorld > World Settings`:
- `blockRegistry` → your registry asset.
- `chunkWidth` → 16 (or smaller like 8 for testing).
- `minHeight = -4`, `maxHeight = 4` → `chunkHeight` should be **9** to match (the field doesn't auto-calculate; set it manually to `maxHeight - minHeight + 1 = 9`).
- `viewDistanceInChunks` → start small (e.g. 2-3) while testing, raise later.
- `maxChunkBuildsPerFrame` → 2 is a safe default.
- `seed` → 0 for random, or a fixed number for reproducible worlds.
- `noiseScale/octaves/persistence/lacunarity` → defaults are fine to start (60 / 3 / 0.5 / 2).
- `atlasSize` → match your atlas texture's grid (e.g. 4 for a 4×4 atlas).
- `chunkMaterial` → create a Material using an unlit/lit shader with your atlas texture assigned to the main texture.

### Step 4 — Create the Chunk Prefab
1. Create an empty GameObject named `VoxelChunk`.
2. Add components: `MeshFilter`, `MeshRenderer`, `MeshCollider` (the script's `[RequireComponent]` will add these automatically if missing, but adding them explicitly avoids warnings).
3. Add the `VoxelChunk` script.
4. Set its layer to a dedicated layer, e.g. **"Chunk"** (create this layer in `Edit > Project Settings > Tags and Layers` if it doesn't exist).
5. Drag it into your Project window to make it a prefab, then delete the scene instance.

### Step 5 — Set up the World Manager
1. Create an empty GameObject, name it `VoxelWorldManager`.
2. Add the `VoxelWorldManager` script.
3. Assign:
   - `settings` → your `VoxelWorldSettings` asset.
   - `playerTransform` → your player/camera transform.
   - `chunkPrefab` → the prefab from Step 4.

### Step 6 — Set up the Block Placer
1. Add the `VoxelBlockPlacer` script to your player object (or a dedicated input-handling object).
2. Assign:
   - `worldManager` → the `VoxelWorldManager` from Step 5.
   - `settings` → your `VoxelWorldSettings` asset.
   - `playerCamera` → leave empty to auto-use `Camera.main`, or assign explicitly.
   - `maxReach` → 6 (or your preferred reach).
   - `chunkLayer` → set this to **only** the "Chunk" layer from Step 4 (so voxel raycasts don't hit your grid-placed objects, and vice versa).
   - `defaultBlockId` → 3 (Grass) or whatever you prefer as a fallback.

### Step 7 — Wire up input
Your existing `GameInpurManager.cs` currently calls `objectPlacer.PlaceObjectInCell()` / `RemoveObjectFromCell()` on the `SimpleObjectPlacer`. To add voxel placing as a parallel system, you have two options:

**Option A (simplest)** — add a second placer reference and call both, gated by `IsActive`:
```csharp
[SerializeField] private VoxelBlockPlacer voxelPlacer;

// in PlaceObjectsInput(), alongside existing logic:
if (Input.GetMouseButtonDown(0))
{
    objectPlacer.RemoveObjectFromCell();
    voxelPlacer.RemoveBlock();
}
else if (Input.GetMouseButtonDown(1))
{
    if (TryInteract()) return;
    if (inventoryManager.GetSelectedItem() != null)
    {
        objectPlacer.PlaceObjectInCell();
        voxelPlacer.PlaceBlock();
    }
}
```
Each placer raycasts against its own layer mask, so calling both is harmless even if only one hits something — `Raycast()` simply returns false for the other.

**Option B (cleaner long-term)** — decide which placer is "active" based on the selected inventory item (e.g. check `selectedItem.voxelBlockId > 0` to decide whether to call the voxel placer or the object placer), and toggle `IsActive` on each accordingly. This avoids both systems firing every click.

### Step 8 — Press Play
The world should stream in around the player. Right-click places `defaultBlockId`, left-click removes the hit block.

---

## 4. Optional: Inventory Integration

To let inventory items place specific voxel blocks, add one field to `ItemData.cs`:

```csharp
[Header("Voxel (optional)")]
[Tooltip("If > 0, this item places this voxel block id instead of the default.")]
public byte voxelBlockId = 0;
```

Then in `VoxelBlockPlacer.PlaceBlock()` call site (or in `GameInpurManager`), pass the selected item's `voxelBlockId`:

```csharp
ItemData selected = inventoryManager.GetSelectedItem();
byte blockId = (selected != null) ? selected.voxelBlockId : 0;
voxelPlacer.PlaceBlock(blockId);
```

This requires **zero changes** to `InventoryManager.cs`, `InventoryUI.cs`, or any save/load logic — `voxelBlockId` just defaults to 0 and is ignored by everything else.

---

## 5. Placing Grid Objects on Top of Voxel Terrain

`VoxelWorldManager.GetTopSolidY(wx, wz)` returns the world Y of the highest solid block at a given X/Z column (or `minHeight - 1` if the column is empty/ungenerated).

To place a `GridSystem` object on the surface:
```csharp
int surfaceY = voxelWorldManager.GetTopSolidY(gridPos.x, gridPos.z);
Vector3 worldPos = gridSystem.GridToWorldPosition(new Vector3Int(gridPos.x, surfaceY + 1, gridPos.z));
```
This keeps the two systems spatially consistent (1 unit = 1 cell) without either depending on the other's internals.

---

## 6. Tuning & Troubleshooting

- **Everything is pink** → `chunkMaterial` isn't assigned in `VoxelWorldSettings`.
- **"No Air block (id 0) found!" error** → add a `VoxelBlockType` with `blockId = 0` and `isSolid = false` to your registry.
- **Chunks pop in/out abruptly at edges** → lower `viewDistanceInChunks` while testing, or increase `maxChunkBuildsPerFrame` (at the cost of possible frame hitches).
- **Visible seams/holes between chunks** → check that `chunkLayer` includes ALL chunk objects and that `IChunkNeighbourSampler.GetBlockAt` is being reached (it falls back to "air" if a neighbouring chunk isn't loaded/cached yet — this is expected at the world's outer edge).
- **Placing/removing blocks does nothing** → confirm `VoxelBlockPlacer.chunkLayer` matches the layer assigned to your chunk prefab, and that `maxReach` is large enough.
- **Performance with large `viewDistanceInChunks`** → each chunk rebuild walks every block × 6 faces. Increasing `chunkWidth` reduces chunk count but increases per-chunk rebuild cost; tune `maxChunkBuildsPerFrame` to balance pop-in latency vs. frame time.
- **Terrain looks too "flat" or too "spiky"** → adjust `noiseScale` (higher = smoother/larger hills), `octaves`/`persistence`/`lacunarity` (more octaves = more small-scale detail).

---

## 7. Extension Points (for future work)

- **Caves**: add a 3D noise pass inside `VoxelTerrainGenerator.Generate()`, carving out Air where 3D noise exceeds a threshold, before/after the height-based layer pass.
- **Biomes**: pass a biome-selection SO into `Generate()`, branch `AssignBlock()` based on biome (different surface/sub-surface blocks, different noise params).
- **Structures**: run a separate `StructurePlacer.Place(data, settings, seed)` pass after `Generate()` returns, before the chunk is meshed.
- **Greedy meshing (face merging)**: `VoxelMeshBuilder` currently emits one quad per visible face (already culls hidden faces). A further optimization is merging adjacent same-block faces into larger quads — drop-in replacement for `Build()` since it returns the same `MeshData` shape.
- **Background threading**: `VoxelChunkData`, `VoxelTerrainGenerator`, and `VoxelMeshBuilder` are all pure-data/static and contain no Unity API calls, so `Generate()` and `Build()` can be moved to worker threads (e.g. via `Task.Run`) — only `ApplyMesh()` and `SetData()` must stay on the main thread.
