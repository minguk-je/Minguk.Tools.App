using DevExpress.Xpf.Grid;

namespace Minguk.Base.Controls;

public class BaseGridControl : GridControl
{
    public BaseGridControl()
    {
        //this.MaxHeight = 10000;
        this.AllowLiveDataShaping = true;
        this.AutoGenerateColumns = AutoGenerateColumnsMode.None;
        this.AutoExpandAllGroups = true;
        this.ClipToBounds = true;
        this.ClipboardCopyMode = ClipboardCopyMode.ExcludeHeader;
        this.DesignTimeDataSourceRowCount = 20;
        this.DesignTimeShowSampleData = true;
        this.EnableSmartColumnsGeneration = false;
        this.SelectionMode = MultiSelectMode.Cell;
        this.ShowLoadingPanel = false;

        this.SnapsToDevicePixels = true;
        this.UseLayoutRounding = true;

        // this.SetValue(DXSerializer.LayoutVersionProperty, "1");
        // this.SetValue(GridControlDependency.IsAutoWidthProperty, true);
        // this.SetValue(GridControlDependency.IsRowNumberProperty, true);

        /*
        string assemblyName = Assembly.GetExecutingAssembly().ManifestModule.Name.Replace(".dll", "");
        ResourceDictionary resourceDictionary = Application.LoadComponent(new Uri($"/{assemblyName};component/Resource/Style.xaml", UriKind.RelativeOrAbsolute)) as ResourceDictionary;
        if (resourceDictionary != null)
        {
            if (resourceDictionary.Contains("BaseFontFamily"))
                this.FontFamily = (FontFamily)resourceDictionary["BaseFontFamily"];

            if (resourceDictionary.Contains("BaseFontSize"))
                this.FontSize = (double)resourceDictionary["BaseFontSize"];
        }
        */
    }
}
