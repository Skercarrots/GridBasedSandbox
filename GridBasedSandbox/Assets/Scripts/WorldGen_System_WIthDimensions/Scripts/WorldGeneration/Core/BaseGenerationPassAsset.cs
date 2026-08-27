using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  BaseGenerationPassAsset  —  Abstract ScriptableObject factory for
//  IGenerationPass instances.  Mirrors the pattern of BaseWorldGeneratorAsset.
//
//  HOW TO CREATE A NEW PASS SO
//  1. Create a class that inherits BaseGenerationPassAsset.
//  2. Store whatever config fields you need (ranges, block IDs, weights…).
//  3. Implement CreatePass() to return your IGenerationPass, passing `this`
//     as the config source.
//  4. Add [CreateAssetMenu(menuName = "WorldGen/Passes/YourName")].
//  5. Create the asset, configure it, and add it to a DimensionProfile's
//     passes list.
// ═══════════════════════════════════════════════════════════════════════════════

public abstract class BaseGenerationPassAsset : ScriptableObject
{
    /// <summary>
    /// Called when the dimension is activated (once per dimension load).
    /// Returns a configured IGenerationPass ready for repeated Apply() calls.
    /// </summary>
    public abstract IGenerationPass CreatePass();
}
