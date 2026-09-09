using System.Windows;
using ICSharpCode.AvalonEdit;

namespace Minguk.Tools.Markup;

/// <summary>
/// <see cref="TextEditor.Text"/> 를 바인딩할 수 있게 해 주는 붙임 속성.
/// </summary>
/// <remarks>
/// AvalonEdit 의 <c>Text</c> 는 의존 속성이 아니다. 문서를 <see cref="TextEditor.Document"/> 가
/// 따로 들고 있어서, XAML 에서 <c>Text="{Binding ...}"</c> 이라고 써도 한 번만 들어가고
/// 그 뒤로는 양쪽이 따로 논다.
///
/// <code>
/// &lt;avalonEdit:TextEditor markup:AvalonEditText.Text="{Binding ScriptText, Mode=TwoWay}" /&gt;
/// </code>
///
/// 되돌아 쓸 때 <see cref="_updating"/> 로 막지 않으면, ViewModel 이 값을 바꿔 편집기에 넣는
/// 순간 편집기가 다시 ViewModel 로 밀어 올려 캐럿이 앞으로 튀거나 무한히 오간다.
/// </remarks>
public static class AvalonEditText
{
    private static bool _updating;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(AvalonEditText),
            // 기본값을 null 로 둔다. string.Empty 로 두면 ViewModel 의 처음 값이 빈 글일 때
            // "달라진 것이 없다" 며 콜백이 안 불려, 편집기 쪽 구독이 붙지 않는다.
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEditor editor) return;

        // 같은 정적 메서드라서 떼었다 붙이면 중복이 남지 않는다.
        editor.TextChanged -= OnEditorTextChanged;
        editor.TextChanged += OnEditorTextChanged;

        if (_updating) return;

        var value = (string)(e.NewValue ?? string.Empty);

        if (editor.Text == value) return;

        // 캐럿을 지키려고 자리를 기억해 둔다. 문서를 통째로 바꾸면 0 으로 돌아가기 때문이다.
        var caret = editor.CaretOffset;

        _updating = true;
        try
        {
            editor.Text = value;
            editor.CaretOffset = caret <= value.Length ? caret : value.Length;
        }
        finally
        {
            _updating = false;
        }
    }

    private static void OnEditorTextChanged(object? sender, System.EventArgs e)
    {
        if (_updating || sender is not TextEditor editor) return;

        _updating = true;
        try
        {
            SetText(editor, editor.Text);
        }
        finally
        {
            _updating = false;
        }
    }
}
