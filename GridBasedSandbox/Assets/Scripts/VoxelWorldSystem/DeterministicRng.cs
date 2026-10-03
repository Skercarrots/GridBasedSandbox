// ─────────────────────────────────────────────────────────────────────────────
//  DeterministicRng — A seedable, no-allocation, thread-safe pseudo-random
//  number generator for use in deterministic terrain generation.
//
//  Based on the xoshiro128** algorithm — extremely fast, passes standard
//  statistical tests, and produces identical sequences from the same seed
//  regardless of call order from other chunks or threads.
//
//  USAGE
//    var rng = new DeterministicRng(Hash(worldSeed, chunkCoord, featureId));
//    int x = rng.NextInt(0, 16);
//    float f = rng.NextFloat();
//
//  WHY NOT System.Random?
//  System.Random allocates on construction and its output sequence can vary
//  between .NET versions. DeterministicRng is a struct (zero alloc), its
//  algorithm is fixed forever, and it's safe to create thousands per chunk
//  with zero GC pressure.
// ─────────────────────────────────────────────────────────────────────────────

public struct DeterministicRng
{
    private uint _s0, _s1, _s2, _s3;

    /// <summary>Creates a new RNG from a single integer seed.
    /// The seed is expanded into the 4×32-bit internal state via splitmix64.</summary>
    public DeterministicRng(int seed)
    {
        // SplitMix64-style seeding to fill all 4 state words from one int
        ulong s = (ulong)(uint)seed;
        s = SplitMix(s); _s0 = (uint)s;
        s = SplitMix(s); _s1 = (uint)s;
        s = SplitMix(s); _s2 = (uint)s;
        s = SplitMix(s); _s3 = (uint)s;

        // Ensure at least one state word is non-zero (xoshiro requirement)
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
    }

    /// <summary>Creates a deterministic seed by hashing multiple int keys together.
    /// Use for things like Hash(worldSeed, chunkX, chunkZ, oreIndex).</summary>
    public static int Hash(int a, int b)
    {
        unchecked
        {
            int h = a * 73856093;
            h ^= b * 19349663;
            h ^= h >> 16;
            h *= -2048144789; // 0x85EBCA6B
            h ^= h >> 13;
            h *= -1028477387; // 0xC2B2AE35
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>Creates a deterministic seed by hashing three int keys together.</summary>
    public static int Hash(int a, int b, int c)
        => Hash(Hash(a, b), c);

    /// <summary>Creates a deterministic seed by hashing four int keys together.</summary>
    public static int Hash(int a, int b, int c, int d)
        => Hash(Hash(a, b, c), d);

    /// <summary>Creates a deterministic seed by hashing five int keys together.</summary>
    public static int Hash(int a, int b, int c, int d, int e)
        => Hash(Hash(a, b, c, d), e);

    // ── Core generation ───────────────────────────────────────────────────────

    /// <summary>Returns the next raw 32-bit pseudo-random value (xoshiro128**).</summary>
    public uint NextRaw()
    {
        unchecked
        {
            uint result = RotateLeft(_s1 * 5, 7) * 9;
            uint t = _s1 << 9;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 11);

            return result;
        }
    }

    /// <summary>Returns a random int in [min, max) (exclusive upper bound).</summary>
    public int NextInt(int min, int max)
    {
        if (min >= max) return min;
        uint range = (uint)(max - min);
        return min + (int)(NextRaw() % range);
    }

    /// <summary>Returns a random float in [0, 1).</summary>
    public float NextFloat()
        => (NextRaw() >> 8) / 16777216f; // 2^24

    /// <summary>Returns a random float in [min, max).</summary>
    public float NextFloat(float min, float max)
        => min + NextFloat() * (max - min);

    /// <summary>Returns true with probability p (0 = never, 1 = always).</summary>
    public bool NextBool(float probability = 0.5f)
        => NextFloat() < probability;

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static uint RotateLeft(uint x, int k)
        => (x << k) | (x >> (32 - k));

    private static ulong SplitMix(ulong z)
    {
        unchecked
        {
            z += 0x9E3779B97F4A7C15UL;
            z  = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z  = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

