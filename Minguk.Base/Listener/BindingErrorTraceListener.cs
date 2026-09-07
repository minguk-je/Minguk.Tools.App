using System.Diagnostics;
using System.Text;

namespace Minguk.Base.Listener;

public class BindingErrorTraceListener : DefaultTraceListener
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    //private static readonly NLog.Logger _logger = NLog.LogManager.CreateNullLogger();

    private static BindingErrorTraceListener? _Listener;
    private readonly StringBuilder _message = new StringBuilder();

    private BindingErrorTraceListener()
    {
    }

    public static void SetTrace()
    {
        SetTrace(SourceLevels.Error, TraceOptions.None);
    }

    public static void SetTrace(SourceLevels level, TraceOptions options)
    {
        if (_Listener == null)
        {
            _Listener = new BindingErrorTraceListener();
            PresentationTraceSources.DataBindingSource.Listeners.Add(_Listener);
        }

        _Listener.TraceOutputOptions = options;
        PresentationTraceSources.DataBindingSource.Switch.Level = level;
    }

    /*
    public static void CloseTrace()
    {
        if (_Listener == null)
        {
            return;
        }

        _Listener.Flush();
        _Listener.Close();
        PresentationTraceSources.DataBindingSource.Listeners.Remove(_Listener);
        _Listener = null;
    }
    */

    public override void Write(string? message)
    {
        _message.Append(message);
    }

    public override void WriteLine(string? message)
    {
        _message.Append(message);
        try
        {
            _logger.Error(_message.ToString());
        }
        finally
        {
            _message.Clear();
        }
    }
}
