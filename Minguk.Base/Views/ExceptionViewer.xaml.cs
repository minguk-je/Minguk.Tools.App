using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

using DevExpress.Xpf.Core;

using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using Size = System.Windows.Size;

namespace Minguk.Base.Views;

/// <summary>
/// Interaction logic for ExceptionWindow.xaml
/// </summary>
public partial class ExceptionViewer : ThemedWindow
{
    public static void Show(Exception e, [CallerMemberName] string? callerMemeberName = "")
    {
        ExceptionViewer viewer;

        if (Application.Current != null)
        {
            viewer = new ExceptionViewer(e, callerMemeberName);

            if (DXSplashScreen.IsActive)
            {
                DXSplashScreen.Close();
            }

            try
            {
                viewer.ShowDialog();
            }
            catch
            {
                //
            }
        }
    }


    public static void Show(Exception e, object? obj)
    {
        ExceptionViewer viewer;

        if (Application.Current != null && obj is MethodBase methodBase)
        {
            //                if (Application.Current.MainWindow == null || !Application.Current.MainWindow.IsActive)
            //                {
            //                    viewer = new ExceptionViewer(e, methodBase.Name);
            //                }
            //                else
            //                {
            viewer = new ExceptionViewer(e, methodBase.Name);
            //                }


            if (DXSplashScreen.IsActive)
            {
                DXSplashScreen.Close();
            }

            viewer.ShowDialog();
        }
    }

    public static void Show(Exception e, string headerMessage, Window? owner)
    {
        if (DXSplashScreen.IsActive)
        {
            DXSplashScreen.Close();
        }


        ExceptionViewer viewer = new ExceptionViewer(e, headerMessage, owner);
        viewer.ShowDialog();
    }

    /*
            /// <summary>
            /// The exception and header message cannot be null.  If owner is specified, this window
            /// uses its Style and will appear centered on the Owner.  You can override this before
            /// calling ShowDialog().
            /// </summary>
            public ExceptionViewer(Exception e, string headerMessage)
                : this(e, headerMessage, null)
            {
            }
    */

    /// <summary>
    /// The exception and header message cannot be null.  If owner is specified, this window
    /// uses its Style and will appear centered on the Owner.  You can override this before
    /// calling ShowDialog().
    /// </summary>
    public ExceptionViewer(Exception e, string? headerMessage, Window? owner = null)
    {
        InitializeComponent();

        if (owner != null)
        {
            // This hopefully makes our window look like it belongs to the main app.
            this.Style = owner.Style;

            // This seems to make the window appear on the same monitor as the owner.
            this.Owner = owner;

            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        if (DefaultPaneBrush != null)
        {
            treeView1.Background = DefaultPaneBrush;
        }

        docViewer.Background = treeView1.Background;

        // We use three font sizes.  The smallest is based on whatever the "standard"
        // size is for the current system/app, taken from an arbitrary control.

        _small = treeView1.FontSize;
        _med = _small * 1.1;
        _large = _small * 1.2;

        Title = DefaultTitle;

        BuildTree(e, headerMessage);
    }

    /// <summary>
    /// The default title to use for the ExceptionViewer window.  Automatically initialized 
    /// to "Error - [ProductName]" where [ProductName] is taken from the application's
    /// AssemblyProduct attribute (set in the AssemblyInfo.cs file).  You can change this
    /// default, or ignore it and set Title yourself before calling ShowDialog().
    /// </summary>
    public static string? DefaultTitle
    {
        get
        {
            if (_defaultTitle == null)
            {
                if (string.IsNullOrEmpty(Product))
                {
                    _defaultTitle = $"Error v{Version}";
                }
                else
                {
                    _defaultTitle = $"Error - {Product} v{Version}";
                }
            }

            return _defaultTitle;
        }

        set
        {
            _defaultTitle = value;
        }
    }

    public static Brush? DefaultPaneBrush
    {
        get;
        set;
    }

    /// <summary>
    /// 예외를 사람이 읽을 수 있는 '원인과 조치'로 바꿔 주는 해설기. 앱 시작 시 등록한다.
    ///
    /// 프레임워크 예외는 진짜 원인을 래퍼로 덮어 버리는 일이 잦다
    /// (예: DB 접속 실패 → EF Core 가 RetryLimitExceededException 으로 감싸서
    ///  화면에는 '재시도 횟수 초과'만 보이고 어디에 접속하려다 실패했는지는 안 보인다).
    /// 해설기가 문장을 돌려주면 그 내용을 트리 맨 위에 먼저 띄운다.
    ///
    /// 알아보지 못하는 예외에는 null 을 돌려주면 되고, 그때는 기존 화면 그대로 뜬다.
    /// Minguk.Base 는 DB 드라이버를 참조하지 않으므로 판별 로직은 앱 쪽에 둔다.
    /// </summary>
    public static Func<Exception, string?>? FriendlyMessageResolver
    {
        get;
        set;
    }

    /// <summary>
    /// Gets the value of the AssemblyProduct attribute of the app.  
    /// If unable to lookup the attribute, returns an empty string.
    /// </summary>
    public static string Product
    {
        get
        {
            if (_product == null)
            {
                _product = GetProductName();
            }

            return _product;
        }
    }

    public static string? Version
    {
        get
        {
            if (_version == null)
            {
                _version = GetVersion()?.ToString();
            }

            return _version;
        }
    }

    static string? _defaultTitle;
    static string? _product;
    static string? _version;

    // Font sizes based on the "normal" size.
    readonly double _small;
    readonly double _med;
    readonly double _large;

    // This is used to dynamically calculate the mainGrid.MaxWidth when the Window is resized,
    // since I can't quite get the behavior I want without it.  See CalcMaxTreeWidth().
    double _chromeWidth;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // The grid column used for the tree started with Width="Auto" so it is now exactly
        // wide enough to fit the longest exception (up to the MaxWidth set in XAML).
        // Changing the width to a fixed pixel value prevents it from changing if the user
        // resizes the window.

        treeCol.Width = new GridLength(treeCol.ActualWidth, GridUnitType.Pixel);
        _chromeWidth = ActualWidth - RootGrid.ActualWidth;
        CalcMaxTreeWidth();
    }

    // Initializes the Product property.
    static string GetProductName()
    {
        string result = "";

        try
        {
            object[] customAttributes = GetAppAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
            if (customAttributes.Length > 0)
            {
                result = ((AssemblyProductAttribute)customAttributes[0]).Product;
            }
        }
        catch
        {
            //
        }

        return result;
    }

    static Version? GetVersion()
    {
        AssemblyName? _appAssemblyName = GetAppAssembly()?.GetName();

        if (_appAssemblyName == null)
            return null;

        return _appAssemblyName.Version;
    }

    // Tries to get the assembly to extract the product name from.
    private static Assembly GetAppAssembly()
    {
        Assembly? _appAssembly = null;

        try
        {
            // This is supposedly how Windows.Forms.Application does it.
            if (Application.Current.MainWindow != null)
                _appAssembly = Application.Current.MainWindow.GetType().Assembly;
        }
        catch
        {
            //
        }

        // If the above didn't work, try less desireable ways to get an assembly.

        if (_appAssembly == null)
        {
            _appAssembly = Assembly.GetEntryAssembly();
        }

        if (_appAssembly == null)
        {
            _appAssembly = Assembly.GetExecutingAssembly();
        }

        return _appAssembly;
    }

    // Builds the tree in the left pane.
    // Each TreeViewItem.Tag will contain a list of Inlines
    // to display in the right-hand pane When it is selected.
    void BuildTree(Exception? e, string? summaryMessage)
    {
        // The first node in the tree contains the summary message and all the
        // nested exception messages.

        // 해설기는 e 를 순회하기 전에 돌린다(아래 while 에서 e 가 바뀐다).
        string? friendlyMessage = ResolveFriendlyMessage(e);

        var inlines = new List<Inline>();
        var firstItem = new TreeViewItem();
        firstItem.Header = "All Messages";
        treeView1.Items.Add(firstItem);

        var inline = new Bold(new Run(summaryMessage));
        inline.FontSize = _large;
        inlines.Add(inline);

        // Now add top-level nodes for each exception while building
        // the contents of the first node.
        while (e != null)
        {
            inlines.Add(new LineBreak());
            inlines.Add(new LineBreak());
            AddLines(inlines, e.Message);

            AddException(e);
            e = e.InnerException;
        }

        firstItem.Tag = inlines;
        //firstItem.IsSelected = true;

        // 해설이 있으면 맨 위에 꽂고 그걸 먼저 펼쳐 준다.
        // 개발자용 상세(예외 타입별 노드)는 그대로 아래에 남는다.
        if (!string.IsNullOrWhiteSpace(friendlyMessage))
        {
            var summaryInlines = new List<Inline>();
            var summaryTitle = new Bold(new Run(FriendlyNodeHeader));
            summaryTitle.FontSize = _large;
            summaryInlines.Add(summaryTitle);
            summaryInlines.Add(new LineBreak());
            summaryInlines.Add(new LineBreak());
            AddLines(summaryInlines, friendlyMessage);

            var summaryItem = new TreeViewItem();
            summaryItem.Header = FriendlyNodeHeader;
            summaryItem.Tag = summaryInlines;

            treeView1.Items.Insert(0, summaryItem);
            summaryItem.IsSelected = true;
        }
        else if (treeView1.Items.Count >= 2)
        {
            if (treeView1.Items[1] is TreeViewItem item)
                item.IsSelected = true;
        }
    }

    private const string FriendlyNodeHeader = "확인하세요";

    // 해설기가 터지더라도 예외 화면 자체는 떠야 한다.
    static string? ResolveFriendlyMessage(Exception? e)
    {
        if (e == null)
            return null;

        try
        {
            return FriendlyMessageResolver?.Invoke(e);
        }
        catch
        {
            return null;
        }
    }

    void AddProperty(List<Inline> inlines, string propName, object? propVal)
    {
        inlines.Add(new LineBreak());
        inlines.Add(new LineBreak());
        var inline = new Bold(new Run(propName + ":"));
        inline.FontSize = _med;
        inlines.Add(inline);
        inlines.Add(new LineBreak());

        if (propVal is string str)
        {
            // Might have embedded newlines.

            AddLines(inlines, str);
        }
        else if (propVal != null)
        {
            inlines.Add(new Run(propVal.ToString()));
        }
    }

    // Adds the string to the list of Inlines, substituting
    // LineBreaks for an newline chars found.
    void AddLines(List<Inline> inlines, string str)
    {
        string[] lines = str.Split('\n');

        inlines.Add(new Run(lines[0].Trim('\r')));

        foreach (string line in lines.Skip(1))
        {
            inlines.Add(new LineBreak());
            inlines.Add(new Run(line.Trim('\r')));
        }
    }

    // Adds the exception as a new top-level node to the tree with child nodes
    // for all the exception's properties.
    void AddException(Exception e)
    {
        // Create a list of Inlines containing all the properties of the exception object.
        // The three most important properties (message, type, and stack trace) go first.

        var exceptionItem = new TreeViewItem();
        var inlines = new List<Inline>();
        PropertyInfo[] properties = e.GetType().GetProperties();

        exceptionItem.Header = e.GetType();
        exceptionItem.Tag = inlines;
        treeView1.Items.Add(exceptionItem);


        Inline inline = new Bold(new Run(e.GetType().Name));
        inline.FontSize = _large;
        inlines.Add(inline);

        AddProperty(inlines, "Message", e.Message);
        AddProperty(inlines, "Stack Trace", e.StackTrace);

        foreach (PropertyInfo info in properties)
        {
            // Skip InnerException because it will get a whole
            // top-level node of its own.

            if (info.Name != "InnerException")
            {
                var value = info.GetValue(e, null);

                if (value != null)
                {
                    if (value is string)
                    {
                        if (string.IsNullOrEmpty(value as string)) continue;
                    }
                    else if (value is IDictionary dic)
                    {
                        value = RenderDictionary(dic);
                        if (string.IsNullOrEmpty(value as string)) continue;
                    }
                    else if (value is IEnumerable ienum && !(value is string))
                    {
                        value = RenderEnumerable(ienum);
                        if (string.IsNullOrEmpty(value as string)) continue;
                    }

                    if (info.Name != "Message" &&
                        info.Name != "StackTrace")
                    {
                        // Add the property to list for the exceptionItem.
                        AddProperty(inlines, info.Name, value);
                    }

                    // Create a TreeViewItem for the individual property.
                    var propertyItem = new TreeViewItem();
                    var propertyInlines = new List<Inline>();

                    propertyItem.Header = info.Name;
                    propertyItem.Tag = propertyInlines;
                    exceptionItem.Items.Add(propertyItem);
                    AddProperty(propertyInlines, info.Name, value);
                }
            }
        }

        exceptionItem.IsExpanded = true;
    }

    static string RenderEnumerable(IEnumerable data)
    {
        StringBuilder result = new StringBuilder();

        foreach (object obj in data)
        {
            result.AppendFormat("{0}\n", obj);
        }

        if (result.Length > 0) result.Length = result.Length - 1;
        return result.ToString();
    }

    static string RenderDictionary(IDictionary data)
    {
        StringBuilder result = new StringBuilder();

        foreach (object key in data.Keys)
        {
            if (key != null && data[key] != null)
            {
                result.AppendLine(key.ToString() + " = " + data[key]!.ToString());
            }
        }

        if (result.Length > 0) result.Length = result.Length - 1;
        return result.ToString();
    }

    private void treeView1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ShowCurrentItem();
    }

    void ShowCurrentItem()
    {
        if (treeView1.SelectedItem != null)
        {
            if (treeView1.SelectedItem is TreeViewItem treeViewItem &&
                treeViewItem.Tag is List<Inline> inlines)
            {
                var doc = new FlowDocument();

                doc.FontSize = _small;
                doc.FontFamily = treeView1.FontFamily;
                doc.TextAlignment = TextAlignment.Left;
                doc.Background = docViewer.Background;

                if (chkWrap.IsChecked == false)
                {
                    doc.PageWidth = CalcNoWrapWidth(inlines) + 50;
                }

                var para = new Paragraph();
                para.Inlines.AddRange(inlines);
                doc.Blocks.Add(para);
                docViewer.Document = doc;
            }
        }
    }

    // Determines the page width for the Inlilness that causes no wrapping.
    double CalcNoWrapWidth(IEnumerable<Inline> inlines)
    {
        double pageWidth = 0;
        var tb = new TextBlock();
        var size = new Size(double.PositiveInfinity, double.PositiveInfinity);

        foreach (Inline inline in inlines)
        {
            tb.Inlines.Clear();
            tb.Inlines.Add(inline);
            tb.Measure(size);

            if (tb.DesiredSize.Width > pageWidth) pageWidth = tb.DesiredSize.Width;
        }

        return pageWidth;
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void btnCopy_Click(object sender, RoutedEventArgs e)
    {
        // Build a FlowDocument with Inlines from all top-level tree items.

        var inlines = new List<Inline>();
        var doc = new FlowDocument();
        var para = new Paragraph();

        doc.FontSize = _small;
        doc.FontFamily = treeView1.FontFamily;
        doc.TextAlignment = TextAlignment.Left;

        foreach (TreeViewItem treeItem in treeView1.Items)
        {
            if (inlines.Any())
            {
                // Put a line of underscores between each exception.

                inlines.Add(new LineBreak());
                inlines.Add(new Run("____________________________________________________"));
                inlines.Add(new LineBreak());
            }

            if (treeItem.Tag is List<Inline> inlineList)
                inlines.AddRange(inlineList);
        }

        para.Inlines.AddRange(inlines);
        doc.Blocks.Add(para);

        // Now place the doc contents on the clipboard in both
        // rich text and plain text format.

        TextRange range = new TextRange(doc.ContentStart, doc.ContentEnd);
        DataObject data = new DataObject();

        try
        {
            using (var stream = new MemoryStream())
            {
                range.Save(stream, DataFormats.Rtf);
                data.SetData(DataFormats.Rtf, Encoding.UTF8.GetString(((MemoryStream)stream).ToArray()));
            }
        }
        catch (FormatException /* ex */)
        {
            // RTF 변환 실패: 최소한 plain text와 XAML을 복사
            data.SetData(DataFormats.Rtf, null);
            data.SetData(DataFormats.Xaml, System.Windows.Markup.XamlWriter.Save(doc));
        }
        data.SetData(DataFormats.StringFormat, range.Text);
        Clipboard.SetDataObject(data);

        // The Inlines that were being displayed are now in the temporary document we just built,
        // causing them to disappear from the viewer.  This puts them back.
        ShowCurrentItem();
    }


    private void chkWrap_Checked(object sender, RoutedEventArgs e)
    {
        ShowCurrentItem();
    }

    private void chkWrap_Unchecked(object sender, RoutedEventArgs e)
    {
        ShowCurrentItem();
    }

    private void ExceptionViewerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            CalcMaxTreeWidth();
        }
    }

    private void CalcMaxTreeWidth()
    {
        // This prevents the GridSplitter from being dragged beyond the right edge of the window.
        // Another way would be to use star sizing for all Grid columns including the left 
        // Grid column (i.e. treeCol), but that causes the width of that column to change when the
        // window's width changes, which I don't like.

        RootGrid.MaxWidth = ActualWidth - _chromeWidth;
        treeCol.MaxWidth = RootGrid.MaxWidth - textCol.MinWidth;
    }
}
