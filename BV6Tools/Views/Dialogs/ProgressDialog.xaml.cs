using System.Runtime.ExceptionServices;
using Wpf.Ui.Controls;

namespace BV6Tools.Views.Dialogs;

/// <summary>
///     Interaction logic for ProgressDialog.xaml
/// </summary>
[INotifyPropertyChanged]
public partial class ProgressDialog : ContentDialog
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<ProgressDialogArgs, Task> _work;
    private ExceptionDispatchInfo? _exception;

    public ProgressDialog(string title, Func<ProgressDialogArgs, Task> work)
    {
        InitializeComponent();
        DataContext = this;
        Title = title;
        _work = work;
        Progress = new ProgressDialogArgs(_cts.Token);
    }

    public ProgressDialogArgs Progress { get; }

    protected override async void OnLoaded()
    {
        base.OnLoaded();
        try
        {
            await _work(Progress);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _exception = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            Hide();
        }
    }

    protected override void OnButtonClick(ContentDialogButton button = ContentDialogButton.Close)
    {
        _cts.Cancel();
    }

    protected override void OnClosed(ContentDialogResult result)
    {
        base.OnClosed(result);
        _exception?.Throw();
    }
}

public partial class ProgressDialogArgs(CancellationToken token) : ObservableObject
{
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;
    [ObservableProperty]
    public partial double Value { get; set; }
    public bool IsCancelled => Token.IsCancellationRequested;
    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }
    [ObservableProperty]
    public partial double MaxValue { get; set; }
    public CancellationToken Token { get; } = token;
}

public readonly record struct ProgressInfo(
    string Text = "",
    double Value = 0,
    double MaxValue = 100,
    bool IsIndeterminate = false
    );