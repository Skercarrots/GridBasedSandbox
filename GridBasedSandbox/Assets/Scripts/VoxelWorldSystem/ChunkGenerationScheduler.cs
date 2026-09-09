using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  ChunkGenerationScheduler — Plain C# class (no MonoBehaviour) that runs
//  WorldGenerator.Generate() on background threads so it never blocks a frame.
//
//  Owned and driven by VoxelWorldManager:
//    • Enqueue(coord)                  — main thread, request generation
//    • DrainCompleted(results, max)    — main thread, pick up finished chunks
//
//  Mesh building still happens on the main thread afterwards (Unity API
//  requirement) — this class only ever produces raw VoxelChunkData.
//
//  THREAD SAFETY
//  Generate() must only read plain data — no Unity API calls (see
//  WorldGenerator's threading note). AnimationCurve.Evaluate() specifically is
//  NOT safe here; that's why WorldGenerator.Prepare() exists and is always
//  called (once, main thread) before this scheduler dispatches anything.
//
//  CONCURRENCY
//  A SemaphoreSlim caps how many Generate() calls run at once, so a big
//  render-distance jump (e.g. teleporting) doesn't spawn hundreds of threads
//  at once — it queues them and works through the queue at a steady rate.
//
//  PLATFORM NOTE
//  WebGL builds are single-threaded; Task.Run there won't give real
//  parallelism. This scheduler still works correctly, it just won't be any
//  faster than generating inline — no code changes needed if you target WebGL,
//  just don't expect the concurrency win.
// ─────────────────────────────────────────────────────────────────────────────

public class ChunkGenerationScheduler
{
    private readonly WorldGenerator _generator;
    private readonly VoxelWorldSettings _settings;
    private readonly int _seed;
    private readonly SemaphoreSlim _concurrencyLimiter;

    // Coords currently queued, generating, or generated-but-not-yet-drained.
    // Prevents the same coord being dispatched twice by overlapping Enqueue() calls.
    private readonly ConcurrentDictionary<Vector2Int, byte> _tracked = new();

    // Finished results waiting to be picked up by the main thread.
    private readonly ConcurrentQueue<(Vector2Int coord, VoxelChunkData data)> _completed = new();

    public ChunkGenerationScheduler(WorldGenerator generator, VoxelWorldSettings settings, int seed, int maxConcurrency)
    {
        _generator = generator;
        _settings  = settings;
        _seed      = seed;

        int workers = maxConcurrency > 0 ? maxConcurrency : Mathf.Max(1, SystemInfo.processorCount - 1);
        _concurrencyLimiter = new SemaphoreSlim(workers, workers);
    }

    /// <summary>True if coord is queued, generating, or generated-but-not-drained.</summary>
    public bool IsTracked(Vector2Int coord) => _tracked.ContainsKey(coord);

    /// <summary>Requests background generation of coord. No-op if already tracked.</summary>
    public void Enqueue(Vector2Int coord)
    {
        if (!_tracked.TryAdd(coord, 0)) return;
        _ = GenerateAsync(coord);
    }

    private async Task GenerateAsync(Vector2Int coord)
    {
        await _concurrencyLimiter.WaitAsync();
        try
        {
            VoxelChunkData data = await Task.Run(() =>
            {
                var d = new VoxelChunkData(coord, _settings.chunkWidth, _settings.TotalWorldHeight);
                _generator.Generate(d, _settings, _seed);
                return d;
            });

            _completed.Enqueue((coord, data));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ChunkGenerationScheduler] Failed generating chunk {coord}: {e}");
            _tracked.TryRemove(coord, out _); // let a future Enqueue() retry it
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>Main thread only. Moves up to maxCount completed chunks into
    /// results and returns how many were drained.</summary>
    public int DrainCompleted(List<(Vector2Int coord, VoxelChunkData data)> results, int maxCount)
    {
        int count = 0;
        while (count < maxCount && _completed.TryDequeue(out var result))
        {
            _tracked.TryRemove(result.coord, out _);
            results.Add(result);
            count++;
        }
        return count;
    }
}