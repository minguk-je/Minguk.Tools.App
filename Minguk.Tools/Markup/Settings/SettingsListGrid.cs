using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;

using DevExpress.Xpf.Grid;

using Minguk.Base.Controls;
using Minguk.Tools.Projects.Settings;

namespace Minguk.Tools.Markup.Settings;

/// <summary>
/// 목록 칸의 표 - 열마다 DataTable 열, 행은 JSON 배열(<c>[{ "키": "1", "HP": 30 }]</c>). 판의 미리보기와 처음 행 대화 상자가 같이 쓴다.
/// </summary>
/// <remarks>
/// 편집을 켜면 맨 아래 새 행 줄, Delete 로 행 지우기(VS 속성 표처럼). 행을 더하고 고치고 지울 때마다 <c>changed</c> - 값은 <see cref="Read"/> 로 통째로 읽는다.
/// <see cref="Push"/> 로 넣는 동안은 <c>changed</c> 를 부르지 않는다.
/// </remarks>
public sealed class SettingsListGrid
{
    private readonly IReadOnlyList<SettingsColumn> _columns;
    private readonly DataTable _table = new();
    private bool _pushing;

    public SettingsListGrid(IReadOnlyList<SettingsColumn> columns, bool editable, Action? changed)
    {
        _columns = columns;

        foreach (var column in columns)
        {
            _table.Columns.Add(column.Name, column.Kind switch
            {
                SettingsItemKind.Number => typeof(double),
                SettingsItemKind.Check => typeof(bool),
                _ => typeof(string)
            });
        }

        Grid = new BaseGridControl { AutoGenerateColumns = AutoGenerateColumnsMode.None, MinHeight = 120 };
        Minguk.Base.Dependency.GridControlDependency.SetIsColumnAutoWidth(Grid, true);

        var view = new BaseTableView
        {
            ShowGroupPanel = false,
            AllowEditing = editable,
            NewItemRowPosition = editable ? NewItemRowPosition.Bottom : NewItemRowPosition.None,
            ShowSearchPanelMode = ShowSearchPanelMode.Never
        };
        Grid.View = view;

        foreach (var column in columns)
        {
            var gridColumn = new GridColumn { FieldName = column.Name, Header = string.IsNullOrWhiteSpace(column.Label) ? column.Name : column.Label };

            gridColumn.EditSettings = column.Kind switch
            {
                SettingsItemKind.Check => new DevExpress.Xpf.Editors.Settings.CheckEditSettings(),
                SettingsItemKind.Number => new DevExpress.Xpf.Editors.Settings.SpinEditSettings(),
                SettingsItemKind.Combo => new DevExpress.Xpf.Editors.Settings.ComboBoxEditSettings { ItemsSource = column.Items ?? [], IsTextEditable = false },
                _ => null
            };

            Grid.Columns.Add(gridColumn);
        }

        Grid.ItemsSource = _table.DefaultView;

        void Changed()
        {
            if (!_pushing) changed?.Invoke();
        }

        _table.RowChanged += (_, _) => Changed();
        _table.RowDeleted += (_, _) => Changed();

        view.PreviewKeyDown += (_, e) =>
        {
            if (!editable || e.Key != Key.Delete || view.ActiveEditor is not null) return;
            if (view.FocusedRowHandle < 0 || Grid.GetRow(view.FocusedRowHandle) is not DataRowView rowView) return;

            rowView.Row.Delete();
            _table.AcceptChanges();
            Changed();
            e.Handled = true;
        };
    }

    public BaseGridControl Grid { get; }

    /// <summary>행 수(지운 행 빼고).</summary>
    public int RowCount => _table.Rows.Cast<DataRow>().Count(r => r.RowState != DataRowState.Deleted);

    /// <summary>지금 행들을 JSON 배열로. 빈 칸은 종류의 빈 값(글자 "", 숫자 0, 체크 false).</summary>
    public JsonArray Read()
    {
        var array = new JsonArray();

        foreach (DataRow row in _table.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;

            var obj = new JsonObject();

            foreach (var column in _columns)
            {
                var cell = row[column.Name];
                obj[column.Name] = column.Kind switch
                {
                    SettingsItemKind.Number => cell is double d ? (d == Math.Floor(d) ? JsonValue.Create((long)d) : JsonValue.Create(d)) : JsonValue.Create(0L),
                    SettingsItemKind.Check => JsonValue.Create(cell is true),
                    _ => JsonValue.Create(cell as string ?? string.Empty)
                };
            }

            array.Add(obj);
        }

        return array;
    }

    /// <summary>행을 갈아 넣는다. 열에 없는 키는 버리고, 종류가 안 맞는 칸은 비운다.</summary>
    public void Push(JsonNode? node)
    {
        _pushing = true;

        try
        {
            _table.Rows.Clear();

            if (node is JsonArray rows)
            {
                foreach (var row in rows.OfType<JsonObject>())
                {
                    var dataRow = _table.NewRow();

                    foreach (var column in _columns)
                    {
                        if (row[column.Name] is not JsonValue cell) continue;

                        dataRow[column.Name] = column.Kind switch
                        {
                            SettingsItemKind.Number when cell.GetValueKind() == System.Text.Json.JsonValueKind.Number => SettingsValue.ToDouble(cell),
                            SettingsItemKind.Check => cell.GetValueKind() == System.Text.Json.JsonValueKind.True,
                            _ when cell.GetValueKind() == System.Text.Json.JsonValueKind.String => cell.GetValue<string>(),
                            _ => DBNull.Value
                        };
                    }

                    _table.Rows.Add(dataRow);
                }
            }

            _table.AcceptChanges();
        }
        finally
        {
            _pushing = false;
        }
    }
}

/// <summary>
/// 목록 칸의 처음 행을 표로 고치는 자리 - 처음 행 대화 상자(<c>SettingsListRowsView</c>)가 쓴다.
/// <see cref="Columns"/> 가 오면 표를 만들고 <see cref="Rows"/> 를 넣는다. 표를 고치면 <see cref="Rows"/> 에 새 배열을 쓴다.
/// </summary>
public sealed class SettingsListRowsEditor : System.Windows.Controls.ContentControl
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(IReadOnlyList<SettingsColumn>), typeof(SettingsListRowsEditor),
        new PropertyMetadata(null, (d, _) => ((SettingsListRowsEditor)d).Rebuild()));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(JsonArray), typeof(SettingsListRowsEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((SettingsListRowsEditor)d).OnRowsChanged(e.NewValue as JsonArray)));

    private SettingsListGrid? _grid;
    private JsonArray? _written;

    public IReadOnlyList<SettingsColumn>? Columns
    {
        get => (IReadOnlyList<SettingsColumn>?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public JsonArray? Rows
    {
        get => (JsonArray?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>검사가 표를 고친다.</summary>
    public SettingsListGrid? ListGrid => _grid;

    private void Rebuild()
    {
        _grid = null;
        Content = null;

        if (Columns is not { Count: > 0 } columns) return;

        SettingsListGrid? grid = null;
        grid = new SettingsListGrid(columns, editable: true, () =>
        {
            _written = grid!.Read();
            Rows = _written;
        });

        grid.Push(Rows);
        _grid = grid;
        Content = grid.Grid;
    }

    private void OnRowsChanged(JsonArray? rows)
    {
        // 표가 쓴 값이 되돌아온 것이면 다시 넣지 않는다 - 넣으면 고치던 행의 초점이 날아간다.
        if (ReferenceEquals(rows, _written)) return;

        _grid?.Push(rows);
    }
}
