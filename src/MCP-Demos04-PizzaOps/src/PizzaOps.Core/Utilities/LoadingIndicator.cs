using System.Diagnostics;

namespace PizzaOps.Core.Utilities;

/// <summary>
/// Provides a console loading indicator that displays rotating characters while an operation is in progress.
/// </summary>
public class LoadingIndicator : IDisposable
{
    /// <summary>
    /// The character array used for the spinner style animation.
    /// </summary>
    private static readonly char[] SpinnerChars = { '|', '/', '-', '\\' };

    /// <summary>
    /// The character array used for the dots style animation.
    /// </summary>
    private static readonly char[] DotsChars = { '.', 'o', 'O', 'o' };

    /// <summary>
    /// The character array used for the arrow style animation.
    /// </summary>
    private static readonly char[] ArrowChars = { '←', '↖', '↑', '↗', '→', '↘', '↓', '↙' };

    /// <summary>
    /// The character array used for the braille style animation.
    /// </summary>
    private static readonly char[] BrailleChars = { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };

    /// <summary>
    /// The character array being used for the current animation style.
    /// </summary>
    private readonly char[] _characters;

    /// <summary>
    /// The message to display alongside the loading indicator.
    /// </summary>
    private readonly string _message;

    /// <summary>
    /// The interval in milliseconds between character changes in the animation.
    /// </summary>
    private readonly int _interval;

    /// <summary>
    /// The cancellation token source used to stop the animation task.
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource;

    /// <summary>
    /// The background task that handles the loading indicator animation.
    /// </summary>
    private readonly Task _animationTask;

    /// <summary>
    /// The current index in the character array for the animation frame.
    /// </summary>
    private int _currentIndex;

    /// <summary>
    /// Indicates whether this instance has been disposed.
    /// </summary>
    private bool _disposed;

    private bool _stopped;

    private int _lastFrameLength;

    /// <summary>
    /// The total elapsed time in milliseconds when the loading indicator was stopped.
    /// </summary>
    private double _elapsedMillisecondsSecond;

    /// <summary>
    /// Gets the available loading indicator styles.
    /// </summary>
    public enum Style
    {
        /// <summary>
        /// A rotating spinner animation using the characters '|', '/', '-', '\'.
        /// </summary>
        Spinner,

        /// <summary>
        /// A pulsing dots animation using the characters '.', 'o', 'O', 'o'.
        /// </summary>
        Dots,

        /// <summary>
        /// A rotating arrow animation cycling through the eight compass directions.
        /// </summary>
        Arrow,

        /// <summary>
        /// A Braille pattern spinner animation.
        /// </summary>
        Braille
    }

    /// <summary>
    /// Initializes a new loading indicator with the specified style and message.
    /// </summary>
    /// <param name="message">The message to display alongside the indicator.</param>
    /// <param name="style">The style of the loading indicator.</param>
    /// <param name="intervalMs">The interval between character changes in milliseconds.</param>
    public LoadingIndicator(string message = "Loading", Style style = Style.Spinner, int intervalMs = 100)
    {
        _message = message;
        _interval = intervalMs;
        _characters = style switch
        {
            Style.Dots => DotsChars,
            Style.Arrow => ArrowChars,
            Style.Braille => BrailleChars,
            _ => SpinnerChars
        };

        _cancellationTokenSource = new CancellationTokenSource();
        _animationTask = Task.Run(AnimateAsync);
    }

    /// <summary>
    /// Starts the loading indicator animation.
    /// </summary>
    /// <returns>A task representing the animation operation.</returns>
    private async Task AnimateAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                var elapsed = stopwatch.Elapsed;
                var character = _characters[_currentIndex % _characters.Length];
                var timeDisplay = $" ({elapsed.TotalSeconds:F1}s)";
                var frame = $"{character} {_message}{timeDisplay}";
                _lastFrameLength = frame.Length;
                ConsoleUtility.Write($"\r{frame}", ConsoleColor.Cyan);
                _currentIndex++;
                await Task.Delay(_interval, _cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException exception)
        {
            ConsoleUtility.WriteErrorLine($"Progress display failed: {exception.Message}");
        }
        finally
        {
            _elapsedMillisecondsSecond = stopwatch.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>
    /// Stops the loading indicator and clears the display.
    /// </summary>
    public void Stop()
    {
        if (_disposed || _stopped) return;

        _stopped = true;
        _cancellationTokenSource.Cancel();
        _animationTask.GetAwaiter().GetResult();

        if (_lastFrameLength > 0)
        {
            ConsoleUtility.Write($"\r{new string(' ', _lastFrameLength)}\r");
        }
    }

    /// <summary>
    /// Stops the loading indicator and displays a completion message.
    /// </summary>
    /// <param name="completionMessage">The message to display upon completion.</param>
    /// <param name="showTimeTaken">Whether to include the elapsed time in the completion message.</param>
    /// <param name="color">The color for the completion message.</param>
    public void Complete(string completionMessage, bool showTimeTaken, ConsoleColor color = ConsoleColor.Green)
    {
        Stop();
        if (showTimeTaken)
        {
            completionMessage += $" (Completed in {_elapsedMillisecondsSecond / 1000:F1}s)";
        }
        ConsoleUtility.WriteLine(completionMessage, color);
    }

    /// <summary>
    /// Disposes the loading indicator and stops the animation.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _cancellationTokenSource.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}