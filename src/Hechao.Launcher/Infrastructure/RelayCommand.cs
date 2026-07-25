using System.Windows.Input;

namespace Hechao.Launcher.Infrastructure;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(Coerce(parameter)) ?? true;

    public void Execute(object? parameter) => _execute(Coerce(parameter));

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Coerce(object? parameter) => parameter is T value ? value : default;
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception> _onException;
    private int _isExecuting;

    public AsyncRelayCommand(
        Func<Task> execute,
        Action<Exception> onException,
        Func<bool>? canExecute = null)
    {
        _execute = execute;
        _onException = onException;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

    public bool CanExecute(object? parameter) =>
        !IsExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        await ExecuteAsync();
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null) ||
            Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return;
        }

        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        catch (Exception exception)
        {
            _onException(exception);
        }
        finally
        {
            Volatile.Write(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
