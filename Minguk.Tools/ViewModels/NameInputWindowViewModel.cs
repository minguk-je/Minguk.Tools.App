using System.IO;
using System.Windows.Input;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 이름 한 칸을 받는 작은 창. 새 프로젝트 이름처럼 폴더 이름이 될 글자를 받는다.
/// </summary>
/// <remarks>
/// 폴더·파일 이름이 되므로 못 쓰는 문자를 여기서 막는다 - 만들다 터지면 무엇이 문제인지 알기 어렵다.
/// 창을 닫는 것은 <see cref="ICurrentWindowService"/> 다(시작 창과 같은 이유 - 진짜 창이라 대화 상자 서비스가 안 들어온다).
/// </remarks>
public class NameInputWindowViewModel : ViewModelBase
{
    public static NameInputWindowViewModel Create() => ViewModelSource.Create(() => new NameInputWindowViewModel());

    protected NameInputWindowViewModel()
    {
        Name = string.Empty;

        DoOkCommand = new DelegateCommand(DoOk);
        DoCancelCommand = new DelegateCommand(() => CurrentWindowService?.Close());
    }

    protected ICurrentWindowService CurrentWindowService => this.GetService<ICurrentWindowService>();

    /// <summary>칸 위에 보이는 안내.</summary>
    public string? Message { get => GetProperty(() => Message); set => SetProperty(() => Message, value); }

    public string Name { get => GetProperty(() => Name); set => SetProperty(() => Name, value, () => Error = null); }

    /// <summary>못 쓰는 이름일 때 칸 아래에 뜨는 글. 고치기 시작하면 지운다.</summary>
    public string? Error { get => GetProperty(() => Error); set => SetProperty(() => Error, value); }

    /// <summary>확인을 눌렀으면 다듬은 이름, 그만뒀으면 null.</summary>
    public string? Result { get; private set; }

    /// <summary>이미 쓰는 이름인지 부르는 쪽이 알려 준다(같은 이름의 폴더 등). null 이면 괜찮다는 뜻.</summary>
    public System.Func<string, string?>? Validate { get; set; }

    public ICommand DoOkCommand { get; }

    public ICommand DoCancelCommand { get; }

    private void DoOk()
    {
        var name = Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            Error = "이름을 적으세요.";
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Error = "이름에 쓸 수 없는 문자가 있습니다.";
            return;
        }

        if (Validate?.Invoke(name) is { } problem)
        {
            Error = problem;
            return;
        }

        Result = name;

        CurrentWindowService?.Close();
    }
}
