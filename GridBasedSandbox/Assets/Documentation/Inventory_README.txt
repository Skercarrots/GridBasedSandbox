==========================================
  INVENTORY SYSTEM — DEVELOPER GUIDE
  Last updated: June 2026
==========================================

This guide covers everything you need to know to use, extend, and maintain
the modular inventory system. Keep this file in the /Inventory folder
alongside the scripts it documents.


------------------------------------------
TABLE OF CONTENTS
------------------------------------------
  1. Overview & Design Philosophy
  2. File Map
  3. Core Concepts
  4. Scene Setup (Step-by-Step)
  5. ScriptableObject Setup
  6. How Item Granting Works
  7. Save & Load
  8. Adding a New Item Grant Source
  9. Public API Reference
  10. Execution Order & Architecture
  11. Common Mistakes & Troubleshooting
  12. Future Extension Points


==========================================
1. OVERVIEW & DESIGN PHILOSOPHY
==========================================

The inventory system is split into three distinct responsibilities:

  [A] DATA — InventoryManager holds all slot/item data in memory.
  [B] PRESENTATION — InventoryUI renders the hotbar on screen.
  [C] POPULATION — PlayerInventoryLoader decides what items the player
      starts with, delegating to pluggable IItemGrantSource implementations.

This separation means you can change how the player gets items (save data,
level design, default loadout) without ever touching InventoryManager or
InventoryUI. It also solves the Unity Awake/Start race condition: the
inventory always initialises before items are ever added.


==========================================
2. FILE MAP
==========================================

  IItemGrantSource.cs         Interface + ItemGrant struct. The contract
                              every item source must implement.

  ItemData.cs                 ScriptableObject defining an item. Also holds
                              the ItemStack runtime class.

  InventoryManager.cs         Owns the List<InventorySlot> data. Exposes
                              methods to add items and query state.

  InventoryUI.cs              Renders the hotbar. Listens to InventoryManager
                              indirectly via method calls — no direct coupling
                              to save or loading logic.

  PlayerInventoryLoader.cs    Orchestrator. Runs Awake/Start in the right
                              order. Gathers all IItemGrantSource instances,
                              picks the highest-priority available one, and
                              populates the inventory.

  InventorySaveService.cs     IItemGrantSource implementation. Reads/writes
                              inventory to PlayerPrefs using JSON.

  LevelGrantSource.cs         IItemGrantSource implementation. A MonoBehaviour
                              you drop in any scene to grant level-specific
                              starting items.

  DefaultLoadoutSource.cs     IItemGrantSource implementation. A ScriptableObject
                              that defines the fallback starting gear for a
                              new game with no save data.


==========================================
3. CORE CONCEPTS
==========================================

--- InventorySlot ---
  A plain data container:
    • slotID    (int)       — fixed index, matches the UI slot position
    • itemStack (ItemStack) — null if the slot is empty

--- ItemStack ---
  Runtime pairing of an ItemData reference and a quantity:
    • itemData  (ItemData)  — points to the ScriptableObject asset
    • amount    (int)       — current count in this stack

--- ItemData ---
  ScriptableObject asset (one per unique item type). Key fields:
    • id              — unique string ID used for serialisation
    • itemName        — display name
    • icon            — Sprite shown in the hotbar
    • maxStackAmount  — how many fit in one slot (default 64)
    • itemPrefab      — 3D GameObject to spawn in the world
    • isPlaceable     — flag for placement logic
    • isUsable        — flag for use/consume logic

--- IItemGrantSource ---
  Any class that implements this interface can provide items to the player
  at load time. The interface has three members:

    int Priority        Lower = higher priority. Save data = 0, Level = 50,
                        Default loadout = 100.

    bool IsAvailable()  Return true if this source has something to give.
                        PlayerInventoryLoader skips sources that return false.

    IEnumerable<ItemGrant> GetItemGrants()
                        Return the list of items to grant.

--- ItemGrant ---
  Simple struct passed between sources and the loader:
    • item    (ItemData)
    • amount  (int)


==========================================
4. SCENE SETUP (STEP-BY-STEP)
==========================================

REQUIRED GAMEOBJECTS IN EVERY SCENE
-------------------------------------

Step 1 — InventoryUI GameObject
  • Create a Canvas → add a child Panel for the hotbar.
  • Attach InventoryUI.cs to the Panel (or any persistent UI object).
  • Assign in the Inspector:
      InventoryBarUI    → the hotbar Panel GameObject
      SlotUIPrefab      → a prefab with an Image (background) and one
                          child Image (item icon). See note below.
      SlotsParent       → the Transform that slot prefabs are instantiated under
      DefaultSlotSprite → sprite for an unselected slot
      SelectedSlotSprite→ sprite for the currently selected slot

  SLOT PREFAB STRUCTURE:
    Slot (Image — background)
      └── Icon (Image — item icon, child index 0)

  The system uses GetChild(0) to find the icon Image, so the icon
  MUST be the first child of the slot prefab.

Step 2 — InventoryManager GameObject
  • Attach InventoryManager.cs.
  • Assign in the Inspector:
      InventoryUI   → the InventoryUI component from Step 1
      InventorySize → number of hotbar slots (default 10, max 10 in UI)

  IMPORTANT: Do NOT call InitializeInventoryBar() manually. PlayerInventoryLoader
  calls it in its own Awake(). If InventoryManager's private Awake() also calls it,
  remove that call to avoid double-initialisation (see Section 10).

Step 3 — PlayerInventoryLoader GameObject
  • Attach PlayerInventoryLoader.cs.
  • Assign in the Inspector:
      InventoryManager    → from Step 2
      DefaultLoadout      → the DefaultLoadoutSource ScriptableObject asset

  Optional (leave null to auto-discover):
      SaveService         → InventorySaveService component (see Step 4)
      LevelGrants         → LevelGrantSource component in this scene

Step 4 — InventorySaveService (optional but recommended)
  • Add InventorySaveService.cs as a component on any persistent GameObject
    (e.g. the same one as PlayerInventoryLoader).
  • Assign it to the SaveService slot on PlayerInventoryLoader.
  • No additional configuration needed.

Step 5 — LevelGrantSource (optional, per-scene)
  • Add LevelGrantSource.cs to any GameObject in scenes where the player
    receives items specific to that level.
  • Fill the Level Start Items list in the Inspector with ItemGrant entries.
  • If not assigned on PlayerInventoryLoader, it will be auto-discovered
    via FindAnyObjectByType at runtime.


==========================================
5. SCRIPTABLEOBJECT SETUP
==========================================

--- Creating an ItemData asset ---
  Right-click in the Project window →
    Create → Inventory → ItemData

  Fill in:
    id              (e.g. "wood_plank") — MUST be unique across all items
    itemName        (e.g. "Wood Plank")
    icon            drag a Sprite asset
    maxStackAmount  how many per slot
    itemPrefab      drag the 3D prefab (can be null if item is not placeable)
    isPlaceable / isUsable  — toggle as needed

--- Creating a DefaultLoadoutSource asset ---
  Right-click in the Project window →
    Create → Inventory → Default Loadout Source

  • Add entries to the Default Items list.
  • Each entry is an ItemGrant: pick an ItemData asset and set an amount.
  • Assign this asset to PlayerInventoryLoader → Default Loadout in the Inspector.
  • One asset can be reused across multiple scenes (e.g. a "NewGameLoadout" asset).


==========================================
6. HOW ITEM GRANTING WORKS
==========================================

At Start(), PlayerInventoryLoader runs this sequence:

  1. GatherSources()
       Collects all configured IItemGrantSource instances:
         - InventorySaveService (if assigned or found)
         - LevelGrantSource (if assigned, otherwise auto-discovered)
         - DefaultLoadoutSource (if assigned)

  2. PickSource()
       Filters to only sources where IsAvailable() == true.
       Sorts by Priority (lowest number wins).
       Returns the single highest-priority available source.

  3. For each ItemGrant in the chosen source:
       Calls inventoryManager.TryAddItem(grant.item, grant.amount)

PRIORITY TABLE (default values):
  Priority 0   → InventorySaveService  (existing save data)
  Priority 50  → LevelGrantSource      (level-specific items)
  Priority 100 → DefaultLoadoutSource  (new game fallback)

RESULT: If the player has a save, they get their saved inventory.
        If not but the level grants items, they get those.
        If neither, they get the default loadout.
        If none are available, inventory starts empty (a warning is logged).

NOTE — MERGING SOURCES:
  By default, only ONE source is used (the highest-priority available one).
  If you need to combine sources (e.g. save data AND level grants simultaneously),
  change PickSource() in PlayerInventoryLoader to iterate all available sources
  instead of taking the first. This is intentional and documented in the method.


==========================================
7. SAVE & LOAD
==========================================

Saving:
  Call PlayerInventoryLoader.SaveInventory() at any point — on scene exit,
  on application quit, on a save button press, etc.

  Example (attach to a quit button):
    [SerializeField] private PlayerInventoryLoader loader;
    void OnApplicationQuit() { loader.SaveInventory(); }

How it works internally:
  SaveInventory() → InventorySaveService.Save(slots)
    Iterates all slots, serialises non-empty stacks to JSON,
    writes to PlayerPrefs under the key "inventory_save".

Loading:
  Happens automatically on Start() if InventorySaveService.IsAvailable()
  returns true (i.e. PlayerPrefs contains the save key).

Clearing save data:
  Call InventorySaveService.ClearSave() to wipe the PlayerPrefs entry.
  Useful for "New Game" flows:

    [SerializeField] private InventorySaveService saveService;
    void StartNewGame() { saveService.ClearSave(); /* load scene */ }

IMPORTANT — ItemData serialisation:
  The save system serialises ItemData as a ScriptableObject reference via
  JsonUtility. This works in the Editor and in builds as long as the
  ItemData assets remain in the project. If you rename or move ItemData
  assets after shipping, existing saves may break.
  Consider adding a stable string `id` field to ItemData and serialising
  by id instead of by reference for production-grade persistence.


==========================================
8. ADDING A NEW ITEM GRANT SOURCE
==========================================

Example: grant items from a quest reward system.

Step 1 — Create the class:

    using System.Collections.Generic;
    using UnityEngine;

    public class QuestRewardSource : MonoBehaviour, IItemGrantSource
    {
        private List<ItemGrant> pendingRewards = new List<ItemGrant>();

        public int Priority => 25; // Between save data and level grants

        public bool IsAvailable() => pendingRewards.Count > 0;

        public IEnumerable<ItemGrant> GetItemGrants() => pendingRewards;

        public void AddReward(ItemData item, int amount)
        {
            pendingRewards.Add(new ItemGrant { item = item, amount = amount });
        }
    }

Step 2 — Register it in PlayerInventoryLoader.GatherSources():

    [SerializeField] private QuestRewardSource questRewards;
    // inside GatherSources():
    if (questRewards != null) sources.Add(questRewards);

That is all. No other file needs to change.


==========================================
9. PUBLIC API REFERENCE
==========================================

--- InventoryManager ---

  void InitializeInventoryBar()
    Sets up slots list and creates UI slot objects. Called by
    PlayerInventoryLoader in Awake. Do not call manually unless
    you need to fully reset the inventory at runtime.

  bool TryAddItem(ItemData item, int amount)
    Attempts to add `amount` of `item` to the inventory.
    Stacks on an existing slot of the same item if space allows.
    Falls back to the first empty slot.
    Returns false and logs a warning if the inventory is full.

  void ChangeCurrentSelectedSlot(int value)
    Changes the active hotbar slot. Value is clamped to [0, slotCount-1].
    Updates the UI highlight automatically.

  ItemData GetSelectedItem()
    Returns the ItemData of the currently selected slot, or null if empty.

  List<InventorySlot> GetSlots()
    Returns the raw slot list. Used by InventorySaveService to read
    inventory state for serialisation. Treat as read-only externally.

--- PlayerInventoryLoader ---

  void SaveInventory()
    Delegates to InventorySaveService.Save(). Safe to call even if
    saveService is null (does nothing).

--- InventorySaveService ---

  void Save(List<InventorySlot> slots)
    Serialises slot data to PlayerPrefs JSON.

  void ClearSave()
    Deletes the save entry from PlayerPrefs.

  bool IsAvailable()
    Returns true if a save entry exists in PlayerPrefs.

--- InventoryUI ---

  void UpdateSlotUI(int slotID, ItemStack itemStack)
    Refreshes the icon for one slot. Called automatically by TryAddItem.

  void UpdateSelectionVisual(int newSlot, int oldSlot)
    Swaps the background sprite between selected and default states.
    Pass -1 for oldSlot to skip deselecting (used on init).

  void ToggleInventoryBar(bool value)
    Shows or hides the hotbar GameObject.


==========================================
10. EXECUTION ORDER & ARCHITECTURE
==========================================

Unity lifecycle used by this system:

  AWAKE (all objects)
    InventoryUI.Awake()           → activates the hotbar Panel
    PlayerInventoryLoader.Awake() → calls InventoryManager.InitializeInventoryBar()
                                    (creates empty slots + UI objects)

  START (all objects)
    PlayerInventoryLoader.Start() → calls LoadInventory()
                                    (populates slots from chosen source)

WHY THIS MATTERS:
  The old StartItems.cs pattern added items in Start() while initialisation
  happened in InventoryManager's own Awake(). This was fragile because the
  slot selection was happening before items existed. The new pattern gives
  PlayerInventoryLoader full control: it initialises in Awake and populates
  in Start, so the order is guaranteed regardless of Script Execution Order
  settings.

DIAGRAM:

  Awake()  ──► PlayerInventoryLoader.Awake()
                    └──► InventoryManager.InitializeInventoryBar()
                              └──► InventoryUI.CreateBarSlotUI() × N
                              └──► InventoryUI.UpdateSelectionVisual()

  Start()  ──► PlayerInventoryLoader.Start()
                    └──► GatherSources()
                    └──► PickSource()  (priority sort)
                    └──► TryAddItem() × N
                              └──► InventoryUI.UpdateSlotUI()

DONE:
  NOTE: Remove the private Awake() from InventoryManager if it still calls
  InitializeInventoryBar() — PlayerInventoryLoader already calls it. Leaving
  both will double-init and produce duplicate UI slots.


==========================================
11. COMMON MISTAKES & TROUBLESHOOTING
==========================================

PROBLEM: Duplicate slots appear in the hotbar.
  CAUSE:   Both InventoryManager.Awake() and PlayerInventoryLoader.Awake()
           are calling InitializeInventoryBar().
  FIX:     Remove the Awake() method from InventoryManager, or remove
           the InitializeInventoryBar() call from one of them.

PROBLEM: Inventory is always empty even with a DefaultLoadoutSource assigned.
  CAUSE A: The DefaultLoadoutSource asset has an empty Default Items list.
  CAUSE B: The asset is not assigned in the PlayerInventoryLoader Inspector slot.
  CAUSE C: IsAvailable() returns false because the list count is 0.
  FIX:     Ensure the asset has at least one ItemGrant entry with a valid
           ItemData reference and amount > 0.

PROBLEM: Save data is never loaded.
  CAUSE A: InventorySaveService is not assigned on PlayerInventoryLoader.
  CAUSE B: SaveInventory() was never called in a previous session so no
           PlayerPrefs key exists.
  CAUSE C: ClearSave() was called somewhere unintentionally.
  FIX:     Check PlayerPrefs via Editor → Edit → Clear All PlayerPrefs
           to verify. Add a debug log in InventorySaveService.IsAvailable().

PROBLEM: LevelGrantSource items are ignored even though the component is in the scene.
  CAUSE:   InventorySaveService IS available and has higher priority (0 < 50),
           so the save data source wins and level grants are skipped.
  FIX:     This is intentional behaviour. If you want BOTH, change PickSource()
           in PlayerInventoryLoader to merge all available sources.

PROBLEM: NullReferenceException in InventoryUI.UpdateSlotUI.
  CAUSE:   The slot prefab's icon Image is not the first child (index 0).
  FIX:     Ensure the icon Image is at child index 0 of the slot prefab root.

PROBLEM: Items are not stacking correctly.
  CAUSE:   The ItemData asset's maxStackAmount is 1, or two slots have
           different ItemData references for the same "logical" item
           (e.g. two separate assets with the same name).
  FIX:     Stacking uses reference equality (stack.itemData == item), so
           always use the same ScriptableObject asset for the same item.
           Never duplicate ItemData assets for the same item type.


==========================================
12. FUTURE EXTENSION POINTS
==========================================

These are natural next steps the architecture already supports:

  • JSON file save (instead of PlayerPrefs)
      Replace PlayerPrefs calls in InventorySaveService with
      File.ReadAllText / File.WriteAllText using Application.persistentDataPath.
      No other file changes needed.

  • Multiple save slots
      Parameterise SaveKey in InventorySaveService (e.g. "inventory_save_slot_1").
      Pass the slot index from PlayerInventoryLoader.

  • Item removal / dropping
      Add TryRemoveItem(ItemData item, int amount) to InventoryManager
      following the same pattern as TryAddItem.

  • Full inventory screen (not just hotbar)
      InventoryUI currently manages only the hotbar. A full-screen inventory
      panel can be a separate component that also calls InventoryManager.GetSlots()
      and subscribes to an OnInventoryChanged event (add a C# Action to
      InventoryManager and invoke it at the end of TryAddItem).

  • Drag-and-drop slot reordering
      Add a SwapSlots(int slotA, int slotB) method to InventoryManager and
      call UpdateSlotUI for both slots afterward.

  • Network / multiplayer sync
      Replace or wrap InventorySaveService with a network-aware implementation
      of IItemGrantSource. PlayerInventoryLoader does not need to change.

  • Item use / consumption
      Use GetSelectedItem() to retrieve the active item, then call TryRemoveItem
      after the use logic resolves.


==========================================
END OF GUIDE
==========================================