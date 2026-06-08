#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.UI.Reactor.VsExtension.UI
{
    public sealed class ReactorEmbedControlViewModel : PropertyChangedBase
    {
        private bool _manuallyPinned;
        private string? _selectedComponent;
        private EmbedStatus _status = EmbedStatus.Idle;
        private string _statusText = EmbedStatusInfo.GetText(EmbedStatus.Idle);
        private Brush _statusBrush = EmbedStatusInfo.GetBrush(EmbedStatus.Idle);
        private bool _errorOverlayVisible;
        private string _errorTitle = string.Empty;
        private string _errorDetail = string.Empty;
        private bool _buildingVisible;
        private bool _placeholderVisible = true;
        private bool _embeddedHostVisible;
        private string _placeholderTitle = "Reactor preview";
        private string _placeholderDetail = "Waiting for the preview to start.";
        private Rect _lastPlaceholderRect;
        private readonly JoinableTaskFactory? _jtf;

        public ReactorEmbedControlViewModel(JoinableTaskFactory? jtf = null)
        {
            _jtf = jtf;
            ForceReloadCommand = new RelayCommand(
                _ => ForceReloadRequested?.Invoke(this, EventArgs.Empty),
                _ => _status != EmbedStatus.Idle && _status != EmbedStatus.Launching);
        }

        public ObservableCollection<string> Components { get; } = new ObservableCollection<string>();

        public string? SelectedComponent
        {
            get => _selectedComponent;
            set => SetSelectedComponent(value, manualPin: true);
        }

        public bool IsManuallyPinned => _manuallyPinned;

        internal EmbedStatus CurrentStatus => _status;

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            private set => SetProperty(ref _statusBrush, value);
        }

        public bool ErrorOverlayVisible
        {
            get => _errorOverlayVisible;
            private set => SetProperty(ref _errorOverlayVisible, value);
        }

        public string ErrorTitle
        {
            get => _errorTitle;
            private set => SetProperty(ref _errorTitle, value);
        }

        public string ErrorDetail
        {
            get => _errorDetail;
            private set => SetProperty(ref _errorDetail, value);
        }

        public bool BuildingVisible
        {
            get => _buildingVisible;
            private set => SetProperty(ref _buildingVisible, value);
        }

        /// <summary>
        /// True whenever the preview area has nothing live to show — i.e. before the
        /// Reactor child app's HWND has been reparented into the placeholder, after
        /// the child exits, during a rude-edit respawn, etc. Bound by the XAML to a
        /// full-area "Reactor preview" overlay (title + status text + spinner) so the
        /// content zone is never "void" (uninitialized Win32 pixels from whatever was
        /// last drawn in that screen region).
        /// </summary>
        public bool PlaceholderVisible
        {
            get => _placeholderVisible;
            private set => SetProperty(ref _placeholderVisible, value);
        }

        /// <summary>
        /// Inverse of <see cref="PlaceholderVisible"/>. Bound to the
        /// <c>HwndHostPlaceholder.Visibility</c> via <c>BooleanToHiddenVisibilityConverter</c>
        /// so the host's HWND is hidden (SW_HIDE) when there's no embedded child —
        /// otherwise WPF airspace would render the empty placeholder HWND on top of
        /// the WPF overlay and the user would still see "void" pixels through it.
        /// The HWND itself stays alive across visibility toggles so the embed-client
        /// reparent flow (SetParent into the placeholder HWND) keeps working.
        ///
        /// IMPORTANT: this MUST resolve to <see cref="Visibility.Hidden"/>, not
        /// <see cref="Visibility.Collapsed"/>, when false. Collapsed removes the
        /// HwndHost from layout entirely, so <c>OnWindowPositionChanged</c> never
        /// fires and <see cref="LastPlaceholderRect"/> stays at 0,0,0,0. The
        /// subsequent <c>AckEmbedAsync</c> then tells the child to resize to 0×0,
        /// producing a black, invisible client area once the placeholder is shown.
        /// </summary>
        public bool EmbeddedHostVisible
        {
            get => _embeddedHostVisible;
            private set => SetProperty(ref _embeddedHostVisible, value);
        }

        /// <summary>
        /// Heading rendered inside the placeholder overlay. Derived from
        /// <see cref="EmbedStatus"/> in <see cref="TransitionTo"/>.
        /// </summary>
        public string PlaceholderTitle
        {
            get => _placeholderTitle;
            private set => SetProperty(ref _placeholderTitle, value);
        }

        /// <summary>
        /// Sub-heading rendered inside the placeholder overlay. Derived from
        /// <see cref="EmbedStatus"/> in <see cref="TransitionTo"/>.
        /// </summary>
        public string PlaceholderDetail
        {
            get => _placeholderDetail;
            private set => SetProperty(ref _placeholderDetail, value);
        }

        public ICommand ForceReloadCommand { get; }

        public Rect LastPlaceholderRect
        {
            get => _lastPlaceholderRect;
            private set => SetProperty(ref _lastPlaceholderRect, value);
        }

        public event EventHandler? Loaded;
        public event EventHandler? Unloaded;
        public event EventHandler<Rect>? PlaceholderRectChanged;
        public event EventHandler? ForceReloadRequested;

        public void TransitionTo(EmbedStatus status)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
            {
                RunOnMainThread(() => ApplyTransitionTo(status));
                return;
            }

            ApplyTransitionTo(status);
        }

        private void ApplyTransitionTo(EmbedStatus status)
        {
            _status = status;
            StatusText = EmbedStatusInfo.GetText(status);
            StatusBrush = EmbedStatusInfo.GetBrush(status);
            BuildingVisible = status == EmbedStatus.Building;

            // Placeholder overlay is visible whenever the embedded child can't be
            // expected to be drawing content right now. Building is special — the
            // child HWND is still parented and the old frame is fine to keep showing
            // under the thin "Building…" strip while the new build lands.
            EmbeddedHostVisible = status == EmbedStatus.Embedded || status == EmbedStatus.Building;
            PlaceholderVisible = !EmbeddedHostVisible;
            UpdatePlaceholderMessage(status);

            if (status == EmbedStatus.BuildFailed)
            {
                ShowErrorIfEmpty("Build failed", "Fix the build errors and reload the preview.");
            }
            else if (status == EmbedStatus.Crashed)
            {
                ShowErrorIfEmpty("Preview crashed", "Reload the preview to start a new embedded process.");
            }
            else if (status != EmbedStatus.BuildFailed && status != EmbedStatus.Crashed)
            {
                ClearError();
            }

            if (ForceReloadCommand is RelayCommand relayCommand)
            {
                relayCommand.RaiseCanExecuteChanged();
            }
        }

        public void SetComponents(IEnumerable<string> components, string? selected = null)
        {
            if (components == null)
            {
                throw new ArgumentNullException(nameof(components));
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
            {
                // ObservableCollection is bound to a WPF ComboBox via
                // CollectionView; mutating it from a background thread throws
                // "This type of CollectionView does not support changes to its
                // SourceCollection from a thread different from the Dispatcher
                // thread." Dispatch so call sites (refresh after handshake,
                // active-document changed) can keep their straight-line shape.
                var snapshot = components as IList<string> ?? components.ToList();
                RunOnMainThread(() => ApplyComponents(snapshot, selected));
                return;
            }

            ApplyComponents(components, selected);
        }

        private void ApplyComponents(IEnumerable<string> components, string? selected)
        {
            var componentList = components.Where(component => !string.IsNullOrWhiteSpace(component)).Distinct(StringComparer.Ordinal).ToList();
            var previousSelection = SelectedComponent;
            Components.Clear();
            foreach (var component in componentList)
            {
                Components.Add(component);
            }

            string? nextSelection = null;
            if (selected != null && componentList.Contains(selected, StringComparer.Ordinal))
            {
                nextSelection = selected;
            }
            else if (previousSelection != null && componentList.Contains(previousSelection, StringComparer.Ordinal))
            {
                nextSelection = previousSelection;
            }
            else if (!_manuallyPinned)
            {
                nextSelection = componentList.FirstOrDefault();
            }

            SetSelectedComponent(nextSelection, manualPin: false);
        }

        public void OnPlaceholderResized(Rect rect)
        {
            LastPlaceholderRect = rect;
            PlaceholderRectChanged?.Invoke(this, rect);
        }

        public void OnActiveDocumentChanged(string? path, IEnumerable<string>? componentsInDoc)
        {
            if (_manuallyPinned || componentsInDoc == null)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
            {
                var snapshot = componentsInDoc as IList<string> ?? componentsInDoc.ToList();
                RunOnMainThread(() => ApplyActiveDocumentChanged(path, snapshot));
                return;
            }

            ApplyActiveDocumentChanged(path, componentsInDoc);
        }

        private void ApplyActiveDocumentChanged(string? path, IEnumerable<string> componentsInDoc)
        {
            var firstComponent = componentsInDoc.FirstOrDefault(component => !string.IsNullOrWhiteSpace(component));
            if (firstComponent == null || string.Equals(firstComponent, SelectedComponent, StringComparison.Ordinal))
            {
                return;
            }

            if (!Components.Contains(firstComponent))
            {
                Components.Add(firstComponent);
            }

            SetSelectedComponent(firstComponent, manualPin: false);
        }

        public void OnLoaded()
        {
            Loaded?.Invoke(this, EventArgs.Empty);
        }

        public void OnUnloaded()
        {
            Unloaded?.Invoke(this, EventArgs.Empty);
        }

        public void ShowError(string title, string detail)
        {
            ErrorTitle = title ?? string.Empty;
            ErrorDetail = detail ?? string.Empty;
            ErrorOverlayVisible = true;
        }

        public void ClearError()
        {
            ErrorOverlayVisible = false;
            ErrorTitle = string.Empty;
            ErrorDetail = string.Empty;
        }

        public void ClearPin()
        {
            if (_manuallyPinned)
            {
                _manuallyPinned = false;
                OnPropertyChanged(nameof(IsManuallyPinned));
            }
        }

        private void SetSelectedComponent(string? value, bool manualPin)
        {
            if (SetProperty(ref _selectedComponent, value) && manualPin)
            {
                _manuallyPinned = true;
                OnPropertyChanged(nameof(IsManuallyPinned));
            }
            else if (manualPin && !_manuallyPinned)
            {
                _manuallyPinned = true;
                OnPropertyChanged(nameof(IsManuallyPinned));
            }
        }

        private void RunOnMainThread(Action action)
        {
            if (_jtf == null)
            {
                action();
                return;
            }

            _jtf.Run(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();
                action();
            });
        }

        private void ShowErrorIfEmpty(string title, string detail)
        {
            if (string.IsNullOrEmpty(ErrorTitle) && string.IsNullOrEmpty(ErrorDetail))
            {
                ShowError(title, detail);
            }
            else
            {
                ErrorOverlayVisible = true;
            }
        }

        private void UpdatePlaceholderMessage(EmbedStatus status)
        {
            switch (status)
            {
                case EmbedStatus.Idle:
                    PlaceholderTitle = "Reactor preview";
                    PlaceholderDetail = "Pick a component to start the preview.";
                    break;
                case EmbedStatus.Launching:
                    PlaceholderTitle = "Starting Reactor preview";
                    PlaceholderDetail = "Building your project and launching the preview host…";
                    break;
                case EmbedStatus.WaitingForHandshake:
                    PlaceholderTitle = "Connecting to preview";
                    PlaceholderDetail = "The preview process is running — waiting for the first frame…";
                    break;
                case EmbedStatus.Respawning:
                    PlaceholderTitle = "Restarting Reactor preview";
                    PlaceholderDetail = "A rude edit (type/record shape change) was detected. Respawning…";
                    break;
                case EmbedStatus.ProjectSwitching:
                    PlaceholderTitle = "Switching project";
                    PlaceholderDetail = "Stopping the current preview and starting one for the active project…";
                    break;
                case EmbedStatus.BuildFailed:
                    PlaceholderTitle = "Build failed";
                    PlaceholderDetail = "See the error details below. Fix the build, then reload the preview.";
                    break;
                case EmbedStatus.Crashed:
                    PlaceholderTitle = "Preview crashed";
                    PlaceholderDetail = "The preview process exited. Reload to start a new embedded process.";
                    break;
                // Embedded / Building leave the live HWND visible so the placeholder
                // text doesn't matter — keep the prior value to avoid a transient flash
                // if a future transition makes the overlay visible again.
            }
        }
    }

    public abstract class PropertyChangedBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                _execute(parameter);
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
