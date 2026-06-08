#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Microsoft.UI.Reactor.VsExtension.UI
{
    public sealed class HwndHostPlaceholder : HwndHost
    {
        private IntPtr _hwnd;

        public event EventHandler<Rect>? PlaceholderResized;

        public IntPtr PlaceholderHwnd => _hwnd;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var className = PlaceholderClass.EnsureRegistered();
            _hwnd = NativeMethods.CreateWindowExW(
                dwExStyle: 0,
                lpClassName: className,
                lpWindowName: null,
                dwStyle: NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN,
                x: 0,
                y: 0,
                nWidth: 0,
                nHeight: 0,
                hWndParent: hwndParent.Handle,
                hMenu: IntPtr.Zero,
                hInstance: NativeMethods.GetModuleHandleW(null),
                lpParam: IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create Reactor embed placeholder window.");
            }

            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            RaisePlaceholderResized(rcBoundingBox);
        }

        internal void RaiseResizedForTest(Rect rect)
        {
            RaisePlaceholderResized(rect);
        }

        protected override bool TabIntoCore(TraversalRequest request)
        {
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.SetFocus(_hwnd);
            }

            return true;
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                if (_hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetFocus(_hwnd);
                }

                handled = true;
                return (IntPtr)NativeMethods.MA_ACTIVATE;
            }

            // WS_CHILD normally routes wheel, UI-state, and DPI messages to the embedded child.
            // Block D can forward these through the EmbedClient once it tracks the child HWND.
            handled = false;
            return IntPtr.Zero;
        }

        protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers)
        {
            if (_hwnd == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.TranslateMessage(ref msg);
            return true;
        }

        protected override bool TranslateCharCore(ref MSG msg, ModifierKeys modifiers)
        {
            if (_hwnd == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.DispatchMessageW(ref msg);
            return true;
        }

        private void RaisePlaceholderResized(Rect rect)
        {
            PlaceholderResized?.Invoke(this, rect);
        }
    }
}
