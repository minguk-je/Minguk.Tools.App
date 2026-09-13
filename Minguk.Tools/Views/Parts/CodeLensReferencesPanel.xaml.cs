using System;
using System.Collections.Generic;

using DevExpress.Xpf.Grid;

using Minguk.Tools.Markup;

namespace Minguk.Tools.Views.Parts;

/// <summary>참조 창. 편집기가 팝업에 담아 띄운다 - 줄을 넣고, 더블 클릭을 <see cref="Navigate"/> 로 받는다.</summary>
public partial class CodeLensReferencesPanel : System.Windows.Controls.UserControl
{
    public CodeLensReferencesPanel()
    {
        InitializeComponent();

        View.RowDoubleClick += (_, e) =>
        {
            if (Grid.GetRow(e.HitInfo.RowHandle) is CodeLensReferenceRow row)
            {
                Navigate?.Invoke(this, row);
                e.Handled = true;
            }
        };

        CollapseAllButton.Click += (_, _) => Grid.CollapseAllGroups();
    }

    /// <summary>줄을 더블 클릭했다.</summary>
    public event EventHandler<CodeLensReferenceRow>? Navigate;

    public IReadOnlyList<CodeLensReferenceRow> Rows
    {
        set => Grid.ItemsSource = value;
    }
}
