using DevExpress.Mvvm;

namespace Minguk.Base.Utilities;

public enum MessengerMessageType
{
    Action,         /* 어떤동작 */
    Command,        /* 어떤명령 */

    Message,        /* 텍스트 메시지 발송 */
    SubMessage,
    Log,
    Error,
    Notification,   /* 어떤 알림 */
    Request,        /* 데이터 요청 */
    SelectItem,     /* 아이템 선택 */
    ShowWindow,     /* 윈도우 실행 */
    Test
};

public class MessengerUtility
{
    // Request
    public MessengerMessageType MessageType { get; set; } = MessengerMessageType.Action;

    public string? TargetTypeName { get; set; }
    public Type? TargetType { get; set; }
    public Type? RequestType { get; set; }
    public Type? NotificationType { get; set; }
    public Type? ObjectType { get; set; }
    public object? TargetObject { get; set; }
    public string? Message { get; set; }
    public string? Parameter { get; set; }
    public object? Value { get; set; }

    public string? Category { get; set; }
    public Color? Foreground { get; set; }
    public Color? Background { get; set; }

    // Resonse
    public object? Result { get; set; }
    public bool Success { get; set; } = false; // Success가 true이면 원하는 요청을 처리 했다는 얘기 : 다른 class에서 처리 중지용


    public bool IsTarget(object obj)
    {
        return IsTargetObject(obj) && IsTargetType(obj.GetType());
    }

    private bool IsTargetObject(object obj)
    {
        if (TargetObject == null)
            return true;

        return TargetObject == obj;
    }

    private bool IsTargetType(Type type)
    {
        if (TargetType == null)
            return true;

        return TargetType == type || TargetType == type.BaseType;
    }


    /*
     * 공용
     */
    public static MessengerUtility Send(MessengerUtility messengerUtility)
    {
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    /*
     * 사용에 혼동되지 않게 별로도 생성
     */
    public static MessengerUtility SendAction(string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Action,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendAction(object targetObject, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Action,
            TargetObject = targetObject,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendAction(Type targetType, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Action,
            TargetType = targetType,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendAction(string targetTypeName, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Action,
            TargetTypeName = targetTypeName,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendCommand(string targetTypeName, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Command,
            TargetTypeName = targetTypeName,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendCommand(object targetObject, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Command,
            TargetObject = targetObject,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendCommand(Type targetType, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Command,
            TargetType = targetType,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendMessage(object targetObject, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Message,
            TargetObject = targetObject,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendMessage(Type targetType, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Message,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendMainMessage(string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Message,
            //TargetType = System.Windows.Application.Current.MainWindow!.DataContext.GetType(),
            Message = message,
            Parameter = parameter,
            Value = value
        };

        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendMainMessage(object targetObject, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Message,
            TargetObject = targetObject,
            Message = message,
            Parameter = parameter,
            Value = value
        };

        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendSubMessage(string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.SubMessage,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendSubMessage(object targetObject, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.SubMessage,
            TargetObject = targetObject,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendElapsedMessage(object targetObject, string message)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.SubMessage,
            TargetObject = targetObject,
            Message = message,
            Parameter = "Elapsed"
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendFpsMessage(object targetObject, string message)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.SubMessage,
            TargetObject = targetObject,
            Message = message,
            Parameter = "Fps",
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendLog(string targetTypeName, string category, string message, Color foreground = default, Color background = default)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Log,
            TargetTypeName = targetTypeName,
            Message = message,
            Category = category,
            Foreground = foreground,
            Background = background,
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendLog(Type targetType, string category, string message, string parameter, Color foreground = default, Color background = default)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Log,
            TargetType = targetType,
            Message = message,
            Parameter = parameter,
            Category = category,
            Foreground = foreground,
            Background = background,
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendLog(object targetObject, string category, string message, string parameter, Color foreground = default, Color background = default)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Log,
            TargetObject = targetObject,
            Message = message,
            Parameter = parameter,
            Category = category,
            Foreground = foreground,
            Background = background,
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendError(string targetTypeName, string category, string message, string parameter, Color foreground = default, Color background = default)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Error,
            TargetTypeName = targetTypeName,
            Message = message,
            Parameter = parameter,
            Category = category,
            Foreground = foreground,
            Background = background,
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendError(Type targetType, string category, string message, Color foreground = default, Color background = default)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Error,
            TargetType = targetType,
            Message = message,
            Category = category,
            Foreground = foreground,
            Background = background,
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendError(object targetObject, string category, string message, Color foreground = default, Color background = default)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Message,
            TargetObject = targetObject,
            Message = message,
            Category = category,
            Foreground = foreground,
            Background = background,
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendErrorLog(Type targetType, string category, string message)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Error,
            TargetType = targetType,
            Message = message,
            Category = category,
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendNotification(string message, string parameter, object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Notification,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendNotification(Type targetType, string message, string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Notification,
            TargetType = targetType,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendRequest(Type targetType, Type requestType, string message = "", string parameter = "", object? value = null)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.Request,
            TargetType = targetType,
            RequestType = requestType,
            Message = message,
            Parameter = parameter,
            Value = value
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendSelectItem(Type targetType, Type objectType, object value)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.SelectItem,
            TargetType = targetType,
            ObjectType = objectType,
            Value = value,
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendShowWindow(string message, string parameter = "")
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.ShowWindow,
            Message = message,
            Parameter = parameter
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendShowWindow(Type targetType, string message, string parameter = "")
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.ShowWindow,
            TargetType = targetType,
            Message = message,
            Parameter = parameter
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }

    public static MessengerUtility SendShowWindow(object targetObject, string message, string parameter)
    {
        MessengerUtility messengerUtility = new MessengerUtility
        {
            MessageType = MessengerMessageType.ShowWindow,
            TargetObject = targetObject,
            Message = message,
            Parameter = parameter
        };
        Messenger.Default.Send(messengerUtility);
        return messengerUtility;
    }
}
