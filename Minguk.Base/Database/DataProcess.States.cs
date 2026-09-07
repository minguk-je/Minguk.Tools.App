using DevExpress.Mvvm;

namespace Minguk.Base.Database;

public partial class DataProcess : ViewModelBase, IDisposable
{
    public enum States
    {
        None = 0,
        Query,
        Browse, 
        Insert, 
        Update, 
        Delete, 
        Copy,
        
        Preview,
        Print,

        Export,
        Import,

        Download,
        Upload,
    }
}
