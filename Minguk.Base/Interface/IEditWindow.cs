using DevExpress.DataProcessing.InMemoryDataProcessor;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Minguk.Base.Database;

namespace Minguk.Base.Interface;

public interface IEditWindow
{
    DataProcess DataRowProcess { get; set; }
    object DataContext { get; set; }
    object Parameter { get; set; }
    string Title { get; set; }

    void Close();

    void ShowWindow();
    bool? ShowDialog();
}
