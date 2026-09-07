using System.ComponentModel;
using System.Windows.Input;

using DevExpress.Mvvm;
using DevExpress.Xpf.Core;

namespace Minguk.Tools.ViewModels;

public class InputDialogViewModel : ViewModelBase
{
    public virtual string ProtocolName { get { return GetValue<string>(); } set { SetValue(value); } }

    public UICommand UICommandApply { get { return GetValue<UICommand>(); } set { SetValue(value); } }
    public UICommand UICommandCancel { get { return GetValue<UICommand>(); } set { SetValue(value); } }

    protected ICurrentDialogService CurrentDialogService { get { return GetService<ICurrentDialogService>(); } }
    protected IMessageBoxService MessageBoxService { get { return GetService<IMessageBoxService>(); } }

    public ICommand OnClosingCommand { get; set; }


    public InputDialogViewModel()
    {
        OnClosingCommand = new DelegateCommand<CancelEventArgs>(OnClosing, false);
    }

    public void OnClosing(CancelEventArgs args)
    {
        var dialogResult = ((CurrentDialogService)CurrentDialogService).ActualWindow.DialogResult;
        if (dialogResult != null) return;

        var result = MessageBoxService.ShowMessage(
            caption: "닫기",
            messageBoxText: "취소 하시겠습니까?",
            button: MessageButton.YesNo,
            defaultResult: MessageResult.No,
            icon: MessageIcon.Question
        );

        if (result == MessageResult.Yes)
        {
            CurrentDialogService.Close(MessageResult.Cancel);
        }
        else
        {
            args.Cancel = true;
        }
    }
}
