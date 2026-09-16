using System.Collections.Generic;
using System.Text.Json.Nodes;

using DevExpress.Mvvm;

using Minguk.Tools.Projects.Settings;

namespace Minguk.Tools.ViewModels.Settings;

/// <summary>
/// 목록 칸의 처음 행 대화 상자(<c>SettingsListRowsView</c>). 설정 탭 속성 창의 `처음 행` … 이 띄운다.
/// </summary>
/// <remarks>문서 탭이 아니라 대화 상자라 <c>DocumentViewModelBase</c> 가 아니다. 확인을 누르면 화면 모델이 <see cref="Rows"/> 를 가져간다.</remarks>
public sealed class SettingsListRowsViewModel : ViewModelBase
{
    public SettingsListRowsViewModel(IReadOnlyList<SettingsColumn> columns, JsonArray rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<SettingsColumn> Columns { get; }

    /// <summary>표가 고칠 때마다 새 배열로 바뀐다.</summary>
    public JsonArray Rows { get => GetValue<JsonArray>(); set => SetValue(value); }

    public string Hint => "값을 한 번도 안 바꿨을 때 들어 있을 행입니다. 맨 아래 줄에 적으면 행이 늘고, 행을 고르고 Delete 로 지웁니다.";
}
