namespace Minguk.Base.Database
{
    public class DataProcessEventArgs : EventArgs
    {
        public DataProcess.Actions Action;
        public DataProcess.States State;
        public bool Success;

        public string Command;
        public string Message;
        public string Parameter;
        public object? Value;
        public object[]? Values;

        public DataProcessEventArgs(DataProcess.States state)
        {
            this.Action = DataProcess.Actions.None;
            this.State = state;
            this.Success = true;

            this.Command = string.Empty;
            this.Message = string.Empty;
            this.Parameter = string.Empty;
            this.Value = null;
            this.Values = null;
        }

        public DataProcessEventArgs(string message)
        {
            this.Action = DataProcess.Actions.None;
            this.State = DataProcess.States.None;
            this.Success = true;

            this.Command = string.Empty;
            this.Message = message;
            this.Parameter = string.Empty;
            this.Value = null;
            this.Values = null;
        }

        public DataProcessEventArgs(DataProcess.States state, DataProcess.Actions action)
        {
            this.Action = action;
            this.State = state;
            this.Success = true;

            this.Command = string.Empty;
            this.Message = string.Empty;
            this.Parameter = string.Empty;
            this.Value = null;
            this.Values = null;
        }

        public DataProcessEventArgs(DataProcess.States state, DataProcess.Actions action, string command, object? value = null)
        {
            this.Action = action;
            this.State = state;
            this.Success = true;

            this.Command = command;
            this.Message = string.Empty;
            this.Parameter = string.Empty;
            this.Value = value;
            this.Values = null;
        }

        public DataProcessEventArgs(DataProcess.States state, DataProcess.Actions action, string command, params object[] values)
        {
            this.Action = action;
            this.State = state;
            this.Success = true;

            this.Command = command;
            this.Message = string.Empty;
            this.Parameter = string.Empty;
            this.Value = null;
            this.Values = values;
        }

        public DataProcessEventArgs(DataProcess.States state, DataProcess.Actions action, string command, string message, object? value = null)
        {
            this.Action = action;
            this.State = state;
            this.Success = true;

            this.Command = command;
            this.Message = message;
            this.Parameter = string.Empty;
            this.Value = value;
            this.Values = null;
        }

        public DataProcessEventArgs(DataProcess.States state, DataProcess.Actions action, string command, string message, params object[] values)
        {
            this.Action = action;
            this.State = state;
            this.Success = true;

            this.Command = command;
            this.Message = message;
            this.Parameter = string.Empty;
            this.Value = null;
            this.Values = values;
        }

        public DataProcessEventArgs(DataProcess.States state, DataProcess.Actions action, string command, string message, string parameter, object? value = null)
        {
            this.Action = action;
            this.State = state;
            this.Success = true;

            this.Command = command;
            this.Message = message;
            this.Parameter = parameter;
            this.Value = value;
            this.Values = null;
        }

        public DataProcessEventArgs(DataProcess.States state, DataProcess.Actions action, string command, string message, string parameter, params object[] values)
        {
            this.Action = action;
            this.State = state;
            this.Success = true;

            this.Command = command;
            this.Message = message;
            this.Parameter = parameter;
            this.Value = null;
            this.Values = values;
        }
    }
}
