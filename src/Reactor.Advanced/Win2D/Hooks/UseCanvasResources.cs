using Microsoft.Graphics.Canvas;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// Hooks for Win2D device-backed resources.
/// </summary>
public static class UseCanvasResourcesHook
{
    /// <summary>
    /// Creates resources for the current Win2D shared device, recreates them
    /// after <see cref="CanvasDevice.DeviceLost"/>, and disposes them on unmount.
    /// </summary>
    /// <remarks>
    /// The resource factory can run on a Win2D worker or game thread depending on
    /// the host canvas. See <see href="docs/guide/win2d-canvas.md#threading">the
    /// Win2D canvas threading guide</see> and <see href="docs/guide/win2d-canvas.md#device-loss">device loss</see>.
    /// </remarks>
    public static Ref<TResources?> UseCanvasResources<TResources>(
        this RenderContext ctx,
        Func<CanvasDevice, ValueTask<TResources>> create,
        Action<TResources>? dispose = null)
        where TResources : class
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(create);

        var resources = ctx.UseRef<TResources?>(null);
        var createRef = ctx.UseRef(create);
        var disposeRef = ctx.UseRef(dispose);
        createRef.Current = create;
        disposeRef.Current = dispose;
        var disposeDependency = (object?)dispose ?? NoCustomDisposeSentinel.Instance;

        ctx.UseEffect(() =>
        {
            var disposed = false;
            var gate = new object();
            CanvasDevice? subscribedDevice = null;

            void DisposeResource(TResources? resource)
            {
                if (resource is null) return;
                var customDispose = disposeRef.Current;
                if (customDispose is not null)
                    customDispose(resource);
                else if (resource is IDisposable disposable)
                    disposable.Dispose();
            }

            async Task RecreateAsync(CanvasDevice device)
            {
                TResources? old;
                lock (gate)
                {
                    if (disposed) return;
                    old = resources.Current;
                    resources.Current = null;
                }

                DisposeResource(old);

                TResources? next = null;
                try
                {
                    next = await createRef.Current(device).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (disposed)
                {
                    return;
                }
                catch (ObjectDisposedException) when (disposed)
                {
                    return;
                }

                lock (gate)
                {
                    if (!disposed)
                    {
                        resources.Current = next;
                        return;
                    }
                }

                DisposeResource(next);
            }

            void Subscribe(CanvasDevice device)
            {
                if (ReferenceEquals(subscribedDevice, device)) return;
                if (subscribedDevice is not null)
                    subscribedDevice.DeviceLost -= OnDeviceLost;
                subscribedDevice = device;
                subscribedDevice.DeviceLost += OnDeviceLost;
            }

            void OnDeviceLost(CanvasDevice sender, object args)
            {
                CanvasDevice? fresh;
                lock (gate)
                {
                    if (disposed) return;
                    fresh = CanvasDevice.GetSharedDevice();
                    Subscribe(fresh);
                }

                _ = RecreateAsync(fresh);
            }

            var device = CanvasDevice.GetSharedDevice();
            lock (gate)
            {
                if (!disposed)
                    Subscribe(device);
            }

            _ = RecreateAsync(device);

            return () =>
            {
                TResources? old;
                lock (gate)
                {
                    if (disposed) return;
                    disposed = true;
                    old = resources.Current;
                    resources.Current = null;
                    if (subscribedDevice is not null)
                        subscribedDevice.DeviceLost -= OnDeviceLost;
                    subscribedDevice = null;
                }

                DisposeResource(old);
            };
        }, create, disposeDependency);

        return resources;
    }

    private sealed class NoCustomDisposeSentinel
    {
        internal static readonly NoCustomDisposeSentinel Instance = new();
    }
}
