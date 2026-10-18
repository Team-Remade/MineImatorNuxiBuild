using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Delegate representing a recording action to be executed on a dedicated worker CommandList.
/// </summary>
public delegate void RenderRecordAction(CommandList cl, int threadIndex);

/// <summary>
/// Multi-threaded scene render pass recorder that uses a pool of CommandLists across worker threads
/// to record draw commands in parallel and submit them sequentially to the GraphicsDevice.
/// </summary>
public class VeldridMultiThreadedRecorder : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly int _workerCount;
    private readonly CommandList[] _commandLists;
    private readonly Action<int>[] _threadActions;
    private readonly List<RenderRecordAction>[] _threadWorkQueues;

    public int WorkerCount => _workerCount;

    public VeldridMultiThreadedRecorder(GraphicsDevice gd, int? workerCount = null)
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
        
        // Default to processor count clamped between 2 and 16 (or 1 if single core)
        _workerCount = workerCount ?? Math.Clamp(Environment.ProcessorCount, 1, 16);

        _commandLists = new CommandList[_workerCount];
        _threadActions = new Action<int>[_workerCount];
        _threadWorkQueues = new List<RenderRecordAction>[_workerCount];

        for (int i = 0; i < _workerCount; i++)
        {
            _commandLists[i] = _gd.ResourceFactory.CreateCommandList();
            _threadWorkQueues[i] = new List<RenderRecordAction>();
        }
    }

    /// <summary>
    /// Executes recorded actions across worker command lists in parallel,
    /// waits for all tasks to complete recording, and submits the command lists to the GPU in order.
    /// </summary>
    /// <param name="recordings">An array or collection of recording actions grouped by worker thread index.</param>
    public void RecordAndSubmitParallel(Action<CommandList, int>[] recordings)
    {
        if (recordings == null || recordings.Length == 0)
            return;

        int activeWorkers = Math.Min(recordings.Length, _workerCount);

        Parallel.For(0, activeWorkers, i =>
        {
            var cl = _commandLists[i];
            cl.Begin();
            try
            {
                recordings[i](cl, i);
            }
            finally
            {
                cl.End();
            }
        });

        // Submit recorded command lists in order
        for (int i = 0; i < activeWorkers; i++)
        {
            _gd.SubmitCommands(_commandLists[i]);
        }
    }

    /// <summary>
    /// Splits an item list evenly across worker threads to record draw calls in parallel.
    /// </summary>
    /// <typeparam name="T">Type of scene render item/object.</typeparam>
    /// <param name="items">The items to render.</param>
    /// <param name="setupAction">Optional setup per command list (e.g. SetFramebuffer, SetPipeline).</param>
    /// <param name="renderItemAction">Action to draw each item.</param>
    public void RecordAndSubmitParallelChunked<T>(
        IReadOnlyList<T> items,
        Action<CommandList>? setupAction,
        Action<CommandList, T, int> renderItemAction)
    {
        if (items == null || items.Count == 0)
            return;

        int totalItems = items.Count;
        int activeWorkers = Math.Min(totalItems, _workerCount);
        int chunkSize = (totalItems + activeWorkers - 1) / activeWorkers;

        Parallel.For(0, activeWorkers, workerIdx =>
        {
            var cl = _commandLists[workerIdx];
            cl.Begin();
            try
            {
                setupAction?.Invoke(cl);

                int start = workerIdx * chunkSize;
                int end = Math.Min(start + chunkSize, totalItems);

                for (int i = start; i < end; i++)
                {
                    renderItemAction(cl, items[i], workerIdx);
                }
            }
            finally
            {
                cl.End();
            }
        });

        for (int i = 0; i < activeWorkers; i++)
        {
            _gd.SubmitCommands(_commandLists[i]);
        }
    }

    /// <summary>
    /// Executes a parallel pass against a shared framebuffer without automatic submit,
    /// returning the recorded command lists for custom submission sequencing.
    /// </summary>
    public CommandList[] RecordParallelChunked<T>(
        IReadOnlyList<T> items,
        Action<CommandList, int>? setupAction,
        Action<CommandList, T, int> renderItemAction,
        out int activeWorkerCount)
    {
        if (items == null || items.Count == 0)
        {
            activeWorkerCount = 0;
            return Array.Empty<CommandList>();
        }

        int totalItems = items.Count;
        int activeWorkers = Math.Min(totalItems, _workerCount);
        activeWorkerCount = activeWorkers;
        int chunkSize = (totalItems + activeWorkers - 1) / activeWorkers;

        Parallel.For(0, activeWorkers, workerIdx =>
        {
            var cl = _commandLists[workerIdx];
            cl.Begin();
            try
            {
                setupAction?.Invoke(cl, workerIdx);

                int start = workerIdx * chunkSize;
                int end = Math.Min(start + chunkSize, totalItems);

                for (int i = start; i < end; i++)
                {
                    renderItemAction(cl, items[i], workerIdx);
                }
            }
            finally
            {
                cl.End();
            }
        });

        return _commandLists;
    }

    public void Dispose()
    {
        for (int i = 0; i < _workerCount; i++)
        {
            _commandLists[i]?.Dispose();
        }
    }
}
