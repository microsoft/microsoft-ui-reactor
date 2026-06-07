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
        private Rect _lastPlaceholderRect;

        public ReactorEmbedControlViewModel()
        {
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
            _status = status;
            StatusText = EmbedStatusInfo.GetText(status);
            StatusBrush = EmbedStatusInfo.GetBrush(status);
            BuildingVisible = status == EmbedStatus.Building;

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
