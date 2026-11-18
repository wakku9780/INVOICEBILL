using System.Windows.Input;

namespace SareeBilling.Desktop.Mvvm;

/// <summary>
/// Runs an async operation from a button. The button is disabled while the
/// operation runs, so a double-click can never save something twice.
/// Unexpected exceptions reach the app's global error handler.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute();
        }
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

/// <summary>
/// <see cref="AsyncRelayCommand"/> with a parameter (e.g. the row a button belongs to).
/// Disabled while running, so a double-click runs it only once.
/// </summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T, Task> _execute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<T, Task> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isRunning && parameter is T;

    public async void Execute(object? parameter)
    {
        if (_isRunning || parameter is not T value)
        {
            return;
        }

        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute(value);
        }
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
