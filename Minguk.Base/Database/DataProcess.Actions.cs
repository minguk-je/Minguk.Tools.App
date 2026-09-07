using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Minguk.Base.Database;

public partial class DataProcess
{
    public enum Actions
    {
        None = 0, 
        
        Condition,
        UpdatedSource,

        UpdatedRow,
        UpdateButtons,
        UpdateControls,

        InitNewRow,
        SaveRow,

        Validation,
        Save,
        Cancel,

        Prepare,
        Command,

        Export, 
        Import, 
        Print
    }
}
