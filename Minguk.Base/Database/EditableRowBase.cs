using DevExpress.Mvvm;

using System.ComponentModel.DataAnnotations.Schema;

namespace Minguk.Base.Database;

public enum RowStates
{
    None = 0,
    Added,
    Modified,
    Deleted
}

/// <summary>
/// 행의 편집 상태를 화면에 알려주는 계약.
/// 공용 행 표시기 템플릿(Resource/DataTemplate.xaml)이 Row.RowState 를 보고 A/U/D 를 그린다.
///
/// EF 화면은 <see cref="EditableRowBase"/> 를 상속하면 되지만,
/// XPO 화면은 모델이 이미 XPLiteObject 등을 상속하고 있어 클래스를 상속할 수 없다.
/// 그래서 계약을 인터페이스로 분리해 두 방식이 같은 표시기를 공유하도록 한다.
/// </summary>
public interface IEditableRow
{
    RowStates RowState { get; set; }
}

public abstract class EditableRowBase : ViewModelBase, IEditableRow
{
    /// <summary>행의 편집 상태. EF에 매핑되지 않는 UI 전용 값.</summary>
    [NotMapped]
    public RowStates RowState
    {
        get => GetProperty(() => RowState);
        set => SetProperty(() => RowState, value);
    }
}
