using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  BaseWorldGeneratorAsset  —  Abstract ScriptableObject that acts as an
//  inspector-serializable factory for IWorldGenerator instances.
//
//  WHY THIS INDIRECTION?
//  Interfaces cannot be assigned in the Inspector.  By wrapping each generator
//  in a thin SO subclass, we can expose a field of type BaseWorldGeneratorAsset
//  on DimensionProfile and drag-and-drop any generator without code changes.
//
//  HOW TO CREATE A NEW GENERATOR SO
//  1. Create a class that inherits BaseWorldGeneratorAsset.
//  2. Implement CreateGenerator() to return your IWorldGenerator.
//  3. Add [CreateAssetMenu(menuName = "WorldGen/Generators/YourName")].
//  4. Create the asset and assign it to a DimensionProfile.
// ═══════════════════════════════════════════════════════════════════════════════

public abstract class BaseWorldGeneratorAsset : ScriptableObject
{
    /// <summary>
    /// Called once when the dimension is activated.
    /// Must return a fully-configured IWorldGenerator ready to call Generate().
    /// </summary>
    public abstract IWorldGenerator CreateGenerator();
}
