// ─────────────────────────────────────────────────────────────────────────────
//  DimensionSeed — Deterministic per-dimension seed derivation.
//
//  WHY NOT string.GetHashCode()?
//  Its result is not guaranteed stable between runtimes / processes, which would
//  make a dimension generate a different layout after a restart (or, worse, after
//  leaving and re-entering it within one session).
//
//  RULE
//  • seedSalt == 0  → the dimension uses the base seed untouched. This keeps the
//                     first/primary dimension (Overworld) bit-identical every time
//                     it is re-entered, so cached chunks and freshly generated
//                     chunks always agree (no seams).
//  • seedSalt != 0  → seed = Hash(baseSeed, FNV-1a(dimensionId), salt).
// ─────────────────────────────────────────────────────────────────────────────

public static class DimensionSeed
{
    /// <summary>32-bit FNV-1a hash of a string. Stable across platforms and runs.</summary>
    public static int StableHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        unchecked
        {
            uint hash = 2166136261u;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }

    /// <summary>Derives the world seed for a dimension from the session base seed.</summary>
    public static int Derive(int baseSeed, string dimensionId, int salt)
    {
        if (salt == 0) return baseSeed;

        int derived = DeterministicRng.Hash(baseSeed, StableHash(dimensionId), salt);
        return derived == 0 ? 1337 : derived;
    }
}

