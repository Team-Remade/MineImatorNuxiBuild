using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Disposable scope token for wrapping CommandList.Begin() and CommandList.End() safely.
/// </summary>
public readonly struct CommandRecordingScope : IDisposable
{
    private readonly CommandList _commandList;

    public CommandList CommandList => _commandList;

    public CommandRecordingScope(CommandList commandList)
    {
        _commandList = commandList ?? throw new ArgumentNullException(nameof(commandList));
        _commandList.Begin();
    }

    public void Dispose()
    {
        _commandList.End();
    }
}

/// <summary>
/// Context token passed to render passes providing access to recording operations.
/// </summary>
public readonly struct RenderPassContext
{
    public CommandList CommandList { get; }
    public GraphicsDevice GraphicsDevice { get; }
    public Framebuffer Framebuffer { get; }
    public uint Width { get; }
    public uint Height { get; }

    public RenderPassContext(
        CommandList commandList,
        GraphicsDevice graphicsDevice,
        Framebuffer framebuffer,
        uint width,
        uint height)
    {
        CommandList = commandList;
        GraphicsDevice = graphicsDevice;
        Framebuffer = framebuffer;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Interface representing a modular, recordable render pass.
/// </summary>
public interface IRenderPass
{
    string Name { get; }
    bool Enabled { get; set; }
    void Record(in RenderPassContext context);
}

/// <summary>
/// High-level CommandList recording manager providing:
/// - Reusable CommandList pooling
/// - Scoped Begin/End recording blocks
/// - Sequenced multi-pass render graph recording and submission
/// - Safe deferred submission queues
/// </summary>
public class VeldridCommandRecorder : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ConcurrentBag<CommandList> _pooledLists = new();
    private readonly List<CommandList> _allCreatedLists = new();
    private readonly List<CommandList> _pendingSubmissions = new();
    private readonly object _lock = new();

    public GraphicsDevice GraphicsDevice => _gd;

    public VeldridCommandRecorder(GraphicsDevice gd)
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
    }

    /// <summary>
    /// Acquires a CommandList from the pool or creates a new one if none are available.
    /// </summary>
    public CommandList Rent()
    {
        if (_pooledLists.TryTake(out var cl))
        {
            return cl;
        }

        lock (_lock)
        {
            var newList = _gd.ResourceFactory.CreateCommandList();
            _allCreatedLists.Add(newList);
            return newList;
        }
    }

    /// <summary>
    /// Returns a CommandList back to the pool for reuse.
    /// </summary>
    public void Return(CommandList cl)
    {
        if (cl == null) return;
        _pooledLists.Add(cl);
    }

    /// <summary>
    /// Begins a scoped recording block using a rented CommandList.
    /// Automatically returns the CommandList to the pool when disposed or submitted.
    /// </summary>
    public CommandRecordingScope BeginScoped(out CommandList commandList)
    {
        commandList = Rent();
        return new CommandRecordingScope(commandList);
    }

    /// <summary>
    /// Records a self-contained action directly to a rented command list, ends it, and submits immediately to the GPU.
    /// </summary>
    public void RecordAndSubmitImmediate(Action<CommandList> recordAction)
    {
        if (recordAction == null) throw new ArgumentNullException(nameof(recordAction));

        var cl = Rent();
        try
        {
            cl.Begin();
            recordAction(cl);
            cl.End();

            _gd.SubmitCommands(cl);
        }
        finally
        {
            Return(cl);
        }
    }

    /// <summary>
    /// Records a sequence of render passes targeting a specific framebuffer into a single command list and submits it.
    /// </summary>
    public void RecordAndSubmitPasses(
        Framebuffer targetFramebuffer,
        uint width,
        uint height,
        RgbaFloat? clearColor,
        IEnumerable<IRenderPass> passes)
    {
        var cl = Rent();
        try
        {
            cl.Begin();
            cl.SetFramebuffer(targetFramebuffer);
            cl.SetFullViewport(0);

            if (clearColor.HasValue)
            {
                cl.ClearColorTarget(0, clearColor.Value);
                cl.ClearDepthStencil(1.0f);
            }

            var context = new RenderPassContext(cl, _gd, targetFramebuffer, width, height);

            foreach (var pass in passes)
            {
                if (pass.Enabled)
                {
                    pass.Record(in context);
                }
            }

            cl.End();
            _gd.SubmitCommands(cl);
        }
        finally
        {
            Return(cl);
        }
    }

    /// <summary>
    /// Enqueues a recorded command list to be submitted later in batch.
    /// </summary>
    public void EnqueueForSubmission(CommandList cl)
    {
        lock (_lock)
        {
            _pendingSubmissions.Add(cl);
        }
    }

    /// <summary>
    /// Submits all enqueued command lists in order and recycles them back to the pool.
    /// </summary>
    public void SubmitPending()
    {
        lock (_lock)
        {
            if (_pendingSubmissions.Count == 0) return;

            for (int i = 0; i < _pendingSubmissions.Count; i++)
            {
                var cl = _pendingSubmissions[i];
                _gd.SubmitCommands(cl);
                Return(cl);
            }

            _pendingSubmissions.Clear();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _pendingSubmissions.Clear();
            while (_pooledLists.TryTake(out _)) { }

            foreach (var cl in _allCreatedLists)
            {
                cl.Dispose();
            }
            _allCreatedLists.Clear();
        }
    }
}
