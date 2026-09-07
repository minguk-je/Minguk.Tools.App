using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;

using Minguk.Base.Enums;
using Minguk.Base.Extension;
using Minguk.Base.Interface;
using Minguk.Base.SplashScreen;
using Minguk.Base.Views;

using Newtonsoft.Json;

using System.Data;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Minguk.Base.Database;

public partial class DataProcess : ViewModelBase, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public string Name { get => GetProperty(() => Name); set => SetProperty(() => Name, value); }
    public DataProcess.States State { get => GetProperty(() => State); set => SetProperty(() => State, value); }
    public DataProcess.Actions Action { get => GetProperty(() => Action); set => SetProperty(() => Action, value); }
    public bool IsDataLoaded { get => GetProperty(() => IsDataLoaded); set => SetProperty(() => IsDataLoaded, value); }

    private List<DataProcess> DetailDataRowProcess { get => GetProperty(() => DetailDataRowProcess); set => SetProperty(() => DetailDataRowProcess, value); }

    private DataTable DataTable { get => GetProperty(() => DataTable); set => SetProperty(() => DataTable, value); }
    public List<IEditWindow> EditWindows { get; set; } = new List<IEditWindow>();



    public int Progress { get; set; } = 0;
    public int Maximum { get; set; } = 100;
    public int Percent { get; set; } = -1; // 화면을 바로 갱신 하게
    public string Status { get; set; } = string.Empty;
    public bool IsShowPercent { get; set; } = true;
    public SplashMessageTypes SplashMessageTypes { get; set; }
    public DXSplashScreenViewModel? ProgressSplashScreenViewModel { get; set; }
    public SplashScreenManager? ActiveSplashScreenManager { get; set; }




    private List<BaseEdit> EditControlList { get => GetProperty(() => EditControlList); set => SetProperty(() => EditControlList, value); }
    private List<bool> CheckErrorList { get => GetProperty(() => CheckErrorList); set => SetProperty(() => CheckErrorList, value); }
    private List<ValidationResult> ValidationResults { get => GetProperty(() => ValidationResults); set => SetProperty(() => ValidationResults, value); }

    public bool IsValid => CheckErrorList.Count == 0;
    public delegate void DataProcessEventHandler(DataProcess process, DataProcessEventArgs e);

    public event DataProcessEventHandler? OnCondition;
    public event DataProcessEventHandler? OnQuery;
    // public event DataProcessEventHandler? OnQueryAsync;
    // public event DataProcessEventHandler? OnEndQueryAsync;
    public event DataProcessEventHandler? OnUpdatedSource;
    public event DataProcessEventHandler? OnUpdatedRow;
    public event DataProcessEventHandler? OnDataSourceCount;
    public event DataProcessEventHandler? OnUpdateButtons;
    public event DataProcessEventHandler? OnUpdateControls;
    public event DataProcessEventHandler? OnChangeRow;
    public event DataProcessEventHandler? OnCurrentItemChanged;
    public event DataProcessEventHandler? OnShowEditWindow;
    public event DataProcessEventHandler? OnInsert;
    public event DataProcessEventHandler? OnUpdate;
    public event DataProcessEventHandler? OnDelete;
    public event DataProcessEventHandler? OnValidation;
    public event DataProcessEventHandler? OnSave;
    public event DataProcessEventHandler? OnCancel;
    public event DataProcessEventHandler? OnCopy;
    public event DataProcessEventHandler? OnCommand;
    public event DataProcessEventHandler? OnPrepare;
    public event DataProcessEventHandler? OnPrint;
    public event DataProcessEventHandler? OnExport;
    public event DataProcessEventHandler? OnImport;
    public event DataProcessEventHandler? OnDownload;
    public event DataProcessEventHandler? OnUpload;



    public bool CanInsert { get => GetProperty(() => CanInsert); set => SetProperty(() => CanInsert, value); }
    public bool CanUpdate { get => GetProperty(() => CanUpdate); set => SetProperty(() => CanUpdate, value); }
    public bool CanDelete { get => GetProperty(() => CanDelete); set => SetProperty(() => CanDelete, value); }
    public bool CanSave { get => GetProperty(() => CanSave); set => SetProperty(() => CanSave, value); }
    public bool CanCancel { get => GetProperty(() => CanCancel); set => SetProperty(() => CanCancel, value); }
    public bool CanExport { get => GetProperty(() => CanExport); set => SetProperty(() => CanExport, value); }
    public bool CanImport { get => GetProperty(() => CanImport); set => SetProperty(() => CanImport, value); }
    public bool CanPrint { get => GetProperty(() => CanPrint); set => SetProperty(() => CanPrint, value); }


    public bool IsRefreshButtonEnabled { get => GetProperty(() => IsRefreshButtonEnabled); set => SetProperty(() => IsRefreshButtonEnabled, value); }
    public bool IsInsertButtonEnabled { get => GetProperty(() => IsInsertButtonEnabled); set => SetProperty(() => IsInsertButtonEnabled, value); }
    public bool IsUpdateButtonEnabled { get => GetProperty(() => IsUpdateButtonEnabled); set => SetProperty(() => IsUpdateButtonEnabled, value); }
    public bool IsDeleteButtonEnabled { get => GetProperty(() => IsDeleteButtonEnabled); set => SetProperty(() => IsDeleteButtonEnabled, value); }
    public bool IsSaveButtonEnabled { get => GetProperty(() => IsSaveButtonEnabled); set => SetProperty(() => IsSaveButtonEnabled, value); }
    public bool IsCancelButtonEnabled { get => GetProperty(() => IsCancelButtonEnabled); set => SetProperty(() => IsCancelButtonEnabled, value); }
    public bool IsCopyButtonEnabled { get => GetProperty(() => IsCopyButtonEnabled); set => SetProperty(() => IsCopyButtonEnabled, value); }
    public bool IsPrintButtonEnabled { get => GetProperty(() => IsPrintButtonEnabled); set => SetProperty(() => IsPrintButtonEnabled, value); }
    public bool IsExportButtonEnabled { get => GetProperty(() => IsExportButtonEnabled); set => SetProperty(() => IsExportButtonEnabled, value); }
    public bool IsImportButtonEnabled { get => GetProperty(() => IsImportButtonEnabled); set => SetProperty(() => IsImportButtonEnabled, value); }
    public bool IsDownloadButtonEnabled { get => GetProperty(() => IsDownloadButtonEnabled); set => SetProperty(() => IsDownloadButtonEnabled, value); }
    public bool IsUploadButtonEnabled { get => GetProperty(() => IsUploadButtonEnabled); set => SetProperty(() => IsUploadButtonEnabled, value); }
    public bool IsEditorButtonEnabled { get => GetProperty(() => IsEditorButtonEnabled); set => SetProperty(() => IsEditorButtonEnabled, value); }

    public bool IsConditionEditorEnabled { get => GetProperty(() => IsConditionEditorEnabled); set => SetProperty(() => IsConditionEditorEnabled, value); }
    public bool IsEditEditorEnabled { get => GetProperty(() => IsEditEditorEnabled); set => SetProperty(() => IsEditEditorEnabled, value); }
    public bool IsPresentationEditorEnabled { get => GetProperty(() => IsPresentationEditorEnabled); set => SetProperty(() => IsPresentationEditorEnabled, value); }


    public DataProcess(string name, bool[]? permissionArray = null)
    {
        try
        {
            Logger.Trace(Name);

            //Name = GetType().Name;
            Name = name;
            State = DataProcess.States.None;
            Action = DataProcess.Actions.None;
            IsDataLoaded = false;

            DetailDataRowProcess = new List<DataProcess>();

            DataTable = new DataTable(Name);

            EditControlList = new List<BaseEdit>();
            CheckErrorList = new List<bool>();
            ValidationResults = new List<ValidationResult>();

            if (permissionArray == null)
            {
                CanInsert = CanUpdate = CanDelete = CanPrint = true;
            }
            else
            {
                CanInsert = permissionArray[0];
                CanUpdate = permissionArray[1];
                CanDelete = permissionArray[2];
                CanPrint = permissionArray[3];
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void Dispose()
    {
        try
        {
            Logger.Trace(Name);

            Dispose(true);
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        try
        {
            Logger.Trace(Name);

            if (disposing)
            {
                ClearEventHandler();
                ClearEditWindows();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void SetState(DataProcess.States state)
    {
        try
        {
            Logger.Trace(Name);

            State = state;

            switch (state)
            {
                case DataProcess.States.None:
                    SetAction(Actions.None);

                    DoUpdateButtons();
                    DoUpdateControls();

                    ClearValidation();
                    CascadeDetailDataRowProces(DataProcess.States.None);
                    break;

                case DataProcess.States.Browse:
                    SetAction(Actions.None);

                    DoUpdateButtons();
                    DoUpdateControls();

                    ClearValidation();
                    CascadeDetailDataRowProces(DataProcess.States.Browse);
                    break;

                case DataProcess.States.Insert:
                case DataProcess.States.Update:
                    SetAction(Actions.None);

                    DoUpdateButtons();
                    DoUpdateControls();

                    ClearErrorCheckList();
                    CascadeDetailDataRowProces(DataProcess.States.None);
                    break;

                case DataProcess.States.Delete:
                    SetAction(Actions.None);

                    DoUpdateButtons();
                    DoUpdateControls();

                    ClearErrorCheckList();
                    CascadeDetailDataRowProces(DataProcess.States.None);
                    break;

                case DataProcess.States.Query:
                    SetAction(Actions.None);

                    DoUpdateButtons();
                    DoUpdateControls();

                    ClearValidation();
                    CascadeDetailDataRowProces(DataProcess.States.None);
                    break;

                case DataProcess.States.Copy:
                    SetAction(Actions.None);

                    DoUpdateButtons();
                    DoUpdateControls();

                    ClearErrorCheckList();
                    CascadeDetailDataRowProces(DataProcess.States.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private int ControlUpdateCount { get; set; } = 0;
    public bool IsControlUpdate => ControlUpdateCount > 0;

    public void BeginControlUpdate()
    {
        try
        {
            Logger.Trace(Name);

            ControlUpdateCount++;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void EndControlUpdate()
    {
        try
        {
            Logger.Trace(Name);

            ControlUpdateCount--;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private int DataUpdateCount { get; set; } = 0;
    public bool IsDataUpdate => DataUpdateCount > 0;

    public void BeginDataUpdate()
    {
        try
        {
            Logger.Trace(Name);

            DataUpdateCount++;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void EndDataUpdate()
    {
        try
        {
            Logger.Trace(Name);

            DataUpdateCount--;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void SetButtons(bool canInsert, bool canUpdate, bool canDelete, bool canSave, bool canCancel, bool canDownload, bool canUpload, bool canPrint)
    {
        try
        {
            Logger.Trace(Name);

            CanInsert = canInsert;
            CanUpdate = canUpdate;
            CanDelete = canDelete;
            CanSave = canSave;
            CanCancel = canCancel;
            CanExport = canDownload;
            CanImport = canUpload;
            CanPrint = canPrint;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void CascadeDetailDataRowProces(DataProcess.States state)
    {
        try
        {
            Logger.Trace(Name);

            foreach (DataProcess dataRowProcess in DetailDataRowProcess)
                dataRowProcess.SetState(state);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void SetAction(Actions action)
    {
        try
        {
            Logger.Trace(Name);

            Action = action;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void AddDetailProcess(DataProcess dataRowProcess)
    {
        try
        {
            Logger.Trace(Name);

            DetailDataRowProcess.Add(dataRowProcess);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void ClearValidation()
    {
        try
        {
            Logger.Trace(Name);

            ValidationResults.Clear();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void ClearErrorCheckList()
    {
        try
        {
            Logger.Trace(Name);

            CheckErrorList.Clear();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void ClearEventHandler()
    {
        try
        {
            Logger.Trace(Name);

            OnCondition = null;
            OnQuery = null;
            // OnQueryAsync = null;
            // OnEndQueryAsync = null;
            OnUpdateButtons = null;
            OnUpdateControls = null;
            OnUpdatedSource = null;
            OnUpdatedRow = null;
            OnDataSourceCount = null;
            OnChangeRow = null;
            OnCurrentItemChanged = null;
            OnShowEditWindow = null;
            OnInsert = null;
            OnUpdate = null;
            OnDelete = null;
            OnValidation = null;
            OnSave = null;
            OnCancel = null;
            OnCopy = null;
            OnCommand = null;
            OnPrepare = null;
            OnPrint = null;
            OnExport = null;
            OnImport = null;
            OnDownload = null;
            OnUpload = null;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void ClearEditWindows()
    {
        try
        {
            Logger.Trace(Name);

            foreach (IEditWindow editWindow in EditWindows.ToList())
            {
                editWindow.Close();
            }

            EditWindows.Clear();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private bool DoConditions()
    {
        bool result = true;
        try
        {
            Logger.Trace(Name);

            SetAction(Actions.Condition);
            DataProcessEventArgs args = new DataProcessEventArgs(State, Action);
            OnCondition?.Invoke(this, args);
            SetAction(Actions.None);

            if (!args.Success)
            {
                //RefreshButtons.Enable(true);
            }

            return args.Success && IsValid;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
            result = false;
        }
        return result;
    }

    public bool DoQuery()
    {
        bool result = true;
        try
        {
            Logger.Trace(Name);

            SetState(States.Query);

            if (DoConditions())
            {
                DataProcessEventArgs args = new DataProcessEventArgs(State);
                OnQuery?.Invoke(this, args);

                if (args.Success)
                {
                    IsDataLoaded = true;
                    DoUpdatedSource();
                    DoUpdateButtons();
                    DoUpdateControls();
                    DoChangeRow();
                }
                else
                {
                    result = false;
                }

                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            result = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return result;
    }

    public DataProcessEventArgs DoUpdatedSource()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            OnUpdatedSource?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoUpdatedRow()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            OnUpdatedRow?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public int DataSourceCount()
    {
        int result = 0;
        try
        {
            Logger.Trace(Name);

            DataProcessEventArgs args = new DataProcessEventArgs(State);
            OnDataSourceCount?.Invoke(this, args);
            result = args.Value != null ? Convert.ToInt32(args.Value) : 0;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return result;
    }

    public DataProcessEventArgs DoUpdateButtons()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            switch (State)
            {
                case States.None:
                case States.Query:
                    IsRefreshButtonEnabled = false;
                    IsInsertButtonEnabled = false;
                    IsUpdateButtonEnabled = false;
                    IsDeleteButtonEnabled = false;
                    IsSaveButtonEnabled = false;
                    IsCancelButtonEnabled = false;
                    IsCopyButtonEnabled = false;
                    IsPrintButtonEnabled = false;
                    IsExportButtonEnabled = false;
                    IsImportButtonEnabled = false;
                    IsDownloadButtonEnabled = false;
                    IsUploadButtonEnabled = false;
                    IsEditorButtonEnabled = false;
                    break;

                case States.Browse:
                    int dataSourceCount = DataSourceCount();
                    IsRefreshButtonEnabled = IsDataLoaded;
                    IsInsertButtonEnabled = CanInsert && IsDataLoaded;
                    IsUpdateButtonEnabled = CanUpdate && dataSourceCount > 0 && IsDataLoaded;
                    IsDeleteButtonEnabled = CanDelete && dataSourceCount > 0 && IsDataLoaded;
                    IsSaveButtonEnabled = IsDataLoaded;
                    IsCancelButtonEnabled = false;
                    IsCopyButtonEnabled = CanInsert && dataSourceCount > 0 && IsDataLoaded;
                    IsPrintButtonEnabled = CanPrint && dataSourceCount > 0 && IsDataLoaded;
                    IsExportButtonEnabled = dataSourceCount > 0 && IsDataLoaded;
                    IsImportButtonEnabled = true && IsDataLoaded;
                    IsDownloadButtonEnabled = dataSourceCount > 0 && IsDataLoaded;
                    IsUploadButtonEnabled = true && IsDataLoaded;
                    IsEditorButtonEnabled = dataSourceCount > 0 && IsDataLoaded;
                    break;

                case States.Insert:
                case States.Update:
                    IsRefreshButtonEnabled = false;
                    IsInsertButtonEnabled = true;
                    IsUpdateButtonEnabled = false;
                    IsDeleteButtonEnabled = false;
                    IsSaveButtonEnabled = true;
                    IsCancelButtonEnabled = true;
                    IsCopyButtonEnabled = false;
                    IsPrintButtonEnabled = false;
                    IsExportButtonEnabled = false;
                    IsImportButtonEnabled = false;
                    IsDownloadButtonEnabled = false;
                    IsUploadButtonEnabled = false;
                    IsEditorButtonEnabled = false;
                    break;

                case States.Delete:
                    IsRefreshButtonEnabled = false;
                    IsInsertButtonEnabled = false;
                    IsUpdateButtonEnabled = false;
                    IsDeleteButtonEnabled = false;
                    IsSaveButtonEnabled = false;
                    IsCancelButtonEnabled = false;
                    IsCopyButtonEnabled = false;
                    IsPrintButtonEnabled = false;
                    IsExportButtonEnabled = false;
                    IsImportButtonEnabled = false;
                    IsDownloadButtonEnabled = false;
                    IsUploadButtonEnabled = false;
                    IsEditorButtonEnabled = false;
                    break;

                case States.Copy:
                case States.Print:
                case States.Export:
                case States.Import:
                case States.Download:
                case States.Upload:
                    IsRefreshButtonEnabled = false;
                    IsInsertButtonEnabled = false;
                    IsUpdateButtonEnabled = false;
                    IsDeleteButtonEnabled = false;
                    IsSaveButtonEnabled = false;
                    IsCancelButtonEnabled = false;
                    IsCopyButtonEnabled = false;
                    IsPrintButtonEnabled = false;
                    IsExportButtonEnabled = false;
                    IsImportButtonEnabled = false;
                    IsDownloadButtonEnabled = false;
                    IsUploadButtonEnabled = false;
                    IsEditorButtonEnabled = false;
                    break;
            }

            OnUpdateButtons?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoUpdateControls()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            switch (State)
            {
                case States.None:
                    IsConditionEditorEnabled = false;
                    IsEditEditorEnabled = false;
                    IsPresentationEditorEnabled = false;
                    break;

                case States.Query:
                    IsConditionEditorEnabled = false;
                    IsEditEditorEnabled = true;
                    IsPresentationEditorEnabled = true;
                    break;

                case States.Browse:
                    IsConditionEditorEnabled = true;
                    IsEditEditorEnabled = true;
                    IsPresentationEditorEnabled = true;
                    break;

                case States.Insert:
                case States.Copy:
                    IsConditionEditorEnabled = false;
                    IsEditEditorEnabled = false;
                    IsPresentationEditorEnabled = false;
                    break;

                case States.Update:
                    IsConditionEditorEnabled = false;
                    IsEditEditorEnabled = true;
                    IsPresentationEditorEnabled = false;
                    break;

                case States.Delete:
                    IsConditionEditorEnabled = false;
                    IsEditEditorEnabled = false;
                    IsPresentationEditorEnabled = false;
                    break;

                case States.Print:
                case States.Export:
                case States.Import:
                case States.Download:
                case States.Upload:
                    IsConditionEditorEnabled = false;
                    IsEditEditorEnabled = false;
                    IsPresentationEditorEnabled = false;
                    break;
            }

            OnUpdateControls?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoChangeRow()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            OnChangeRow?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoChangeRow(object value)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, string.Empty, value);
        try
        {
            Logger.Trace(Name);

            OnChangeRow?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoChangeRow(params object[] values)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, string.Empty, values);
        try
        {
            Logger.Trace(Name);

            OnChangeRow?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public void DoSelectItem(GridControl gridControl, object? value)
    {
        try
        {
            int rowHandle = gridControl.FindRow(value);
            if (rowHandle != DataControlBase.InvalidRowHandle)
            {
                gridControl.SelectedItems.Clear();
                gridControl.SelectItem(rowHandle);

                if (gridControl.View is GridDataViewBase gridDataViewBase)
                {
                    gridDataViewBase.FocusedRowHandle = rowHandle;
                    gridDataViewBase.ScrollIntoView(rowHandle);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void DoSelectFirstItem(GridControl? gridControl)
    {
        if (gridControl == null)
            return;

        try
        {
            Logger.Trace(Name);

            if (gridControl?.View != null)
            {
                if (gridControl.VisibleRowCount > 0)
                {
                    gridControl.SelectedItems.Clear();
                    gridControl.SelectItem(0);

                    if (gridControl.View is GridDataViewBase gridDataViewBase)
                    {
                        gridDataViewBase.TopRowIndex = 0;
                        gridDataViewBase.FocusedRowHandle = 0;
                        gridDataViewBase.ScrollIntoView(0);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void DoSelectLastItem(GridControl? gridControl)
    {
        if (gridControl == null)
            return;

        try
        {
            Logger.Trace(Name);

            if (gridControl.VisibleRowCount > 0)
            {
                gridControl.SelectedItems.Clear();
                gridControl.SelectItem(gridControl.VisibleRowCount - 1);

                if (gridControl.View is GridDataViewBase gridDataViewBase)
                {
                    gridDataViewBase.FocusedRowHandle = gridControl.VisibleRowCount - 1;
                    gridDataViewBase.ScrollIntoView(gridControl.VisibleRowCount - 1);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public DataProcessEventArgs DoCurrentItemChanged(TableView tableView)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            if (tableView.FocusedRowHandle == DataControlBase.AutoFilterRowHandle ||
                tableView.FocusedRowHandle == DataControlBase.NewItemRowHandle)
            {
                IsUpdateButtonEnabled = false;
                IsDeleteButtonEnabled = false;
                IsEditorButtonEnabled = false;
            }
            else
            {
                int count = DataSourceCount();

                IsUpdateButtonEnabled = CanUpdate && count > 0;
                IsDeleteButtonEnabled = CanDelete && count > 0;
                IsEditorButtonEnabled = count > 0;
            }

            OnCurrentItemChanged?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }



    public string GetStateTitle()
    {
        string result = string.Empty;
        try
        {
            Logger.Trace(Name);

            switch (State)
            {
                case States.Insert:
                    result = "Insert";
                    break;

                case States.Update:
                    result = "Update";
                    break;

                case States.Delete:
                    result = "Delete";
                    break;

                case States.Browse:
                    result = "Explorer";
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return result;
    }

    public DataProcessEventArgs DoInsert()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            SetState(States.Insert);
            OnInsert?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoCopy()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            OnCopy?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoUpdate()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            SetState(States.Update);
            OnUpdate?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoDelete()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            SetState(States.Delete);
            OnDelete?.Invoke(this, args);
            if (args.Success)
            {
                SetAction(Actions.UpdatedRow);

                args = new DataProcessEventArgs(State, Action);
                OnUpdatedRow?.Invoke(this, args);
            }

            SetState(States.Browse);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoValidation()
    {
        SetAction(Actions.Validation);
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action);
        try
        {
            Logger.Trace(Name);

            OnValidation?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoSave()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action);
        try
        {
            Logger.Trace(Name);

            // 저장 전 검증(OnValidation). 실패 시 저장 중단.
            if (!DoValidation().Success)
            {
                args.Success = false;
                return args;
            }

            if (IsValid)
            {
                SetAction(Actions.Save);
                OnSave?.Invoke(this, args);

                if (args.Success)
                {
                    SetAction(Actions.UpdatedRow);
                    OnUpdatedRow?.Invoke(this, args);
                    SetState(States.Browse);
                }
            }
            else
            {
                args.Success = false;
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoCancel()
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State);
        try
        {
            Logger.Trace(Name);

            OnCancel?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoShowEditWindow(string command = "")
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command);
        try
        {
            Logger.Trace(Name);

            OnShowEditWindow?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoCommand(string command, object value)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command, value);
        try
        {
            Logger.Trace(Name);

            OnCommand?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoCommand(string command, params object[] values)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command, values);
        try
        {
            Logger.Trace(Name);

            OnCommand?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoCommand(string command, string message, object value)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command, message, value);
        try
        {
            Logger.Trace(Name);

            OnCommand?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoCommand(string command, string message, params object[] values)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command, message, values);
        try
        {
            Logger.Trace(Name);

            OnCommand?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoCommand(string command, string message, string parameter, object value)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command, message, parameter, value);
        try
        {
            Logger.Trace(Name);

            OnCommand?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoCommand(string command, string message, string parameter, params object[] values)
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command, message, parameter, values);
        try
        {
            Logger.Trace(Name);

            OnCommand?.Invoke(this, args);
            if (args.Success)
            {
                SetState(States.Browse);
            }
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoPrepare(string command = "")
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command);
        try
        {
            Logger.Trace(Name);

            OnPrepare?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoPrint(string command = "")
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command);
        try
        {
            Logger.Trace(Name);

            SetState(States.Print);
            OnPrint?.Invoke(this, args);
            SetState(States.Browse);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoExport(string command = "")
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command);
        try
        {
            Logger.Trace(Name);

            SetState(States.Export);
            OnExport?.Invoke(this, args);
            SetState(States.Browse);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoImport(string command = "")
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command);
        try
        {
            Logger.Trace(Name);

            SetState(States.Import);
            OnImport?.Invoke(this, args);
            SetState(States.Browse);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoDownload(string command = "")
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command);
        try
        {
            Logger.Trace(Name);

            SetState(States.Download);
            OnDownload?.Invoke(this, args);
            SetState(States.Browse);
        }
        catch (Exception ex)
        {
            args.Success = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public DataProcessEventArgs DoUpload(string command = "")
    {
        DataProcessEventArgs args = new DataProcessEventArgs(State, Action, command);
        try
        {
            Logger.Trace(Name);

            SetState(States.Upload);
            OnUpload?.Invoke(this, args);
            SetState(States.Browse);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return args;
    }

    public string GetSplashScreenMessage(SplashMessageTypes splashMessageTypes)
    {
        string result = string.Empty;
        try
        {
            Logger.Trace(Name);

            if (SplashMessageTypes.Loading == splashMessageTypes)
            {
                result = "불러오는 중 입니다";
            }
            else if (SplashMessageTypes.Processing == splashMessageTypes)
            {
                result = "처리 중 입니다";
            }
            else if (SplashMessageTypes.Stopping == splashMessageTypes)
            {
                result = "중지하는 중 입니다";
            }
            else if (SplashMessageTypes.Saveing == splashMessageTypes)
            {
                result = "저장 중 입니다";
            }
            else if (SplashMessageTypes.Analysis == splashMessageTypes)
            {
                result = "분석 중 입니다";
            }
            else if (SplashMessageTypes.Connecting == splashMessageTypes)
            {
                result = "연결 중 입니다";
            }
            else if (SplashMessageTypes.Waiting == splashMessageTypes)
            {
                result = "잠시만 기다려 주세요";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return result;
    }

    public SplashScreenManager? ShowWaitSplashScreen(SplashMessageTypes splashMessageTypes, string customMessage = "")
    {
        try
        {
            Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);

            if (ActiveSplashScreenManager == null)
            {
                ActiveSplashScreenManager = SplashScreenManager.CreateWaitIndicator(new DXSplashScreenViewModel());
                ActiveSplashScreenManager.ViewModel.Status = Status;
                ActiveSplashScreenManager.Show();
            }
            else
            {
                ActiveSplashScreenManager.ViewModel.Status = Status;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }

        return ActiveSplashScreenManager;
    }

    public void SetMaximum(int value)
    {
        Maximum = value;
        Percent = -1; // 화면을 바로 갱신 시킬 수 있게
        Progress = 0;
    }

    public int CalcPercent(int percent = 100)
    {
        return PercentOfMaximum(Progress, Maximum, percent);
    }

    public void SetPercent()
    {
        Percent = CalcPercent();
    }

    public int GetPercent()
    {
        return Percent;
    }

    public int PercentOfMaximum(int current, int maximum, int percent = 100)
    {
        return (int)PercentOfMaximum((double)current, (double)maximum, (double)percent);
    }

    public double PercentOfMaximum(double current, double maximum, double percent = 100d)
    {
        return current / maximum * percent;
    }

    public int CurrentOfMaximum(int maximum, int current, int percent = 100)
    {
        return (int)CurrentOfMaximum((double)maximum, (double)current, (double)percent);
    }

    public double CurrentOfMaximum(double maximum, double current, double percent = 100d)
    {
        return maximum * current / percent;
    }

    public void InitProgressSplashScreen(SplashMessageTypes splashMessageTypes, int maximum, string customMessage = "")
    {
        SplashMessageTypes = splashMessageTypes;
        SetMaximum(maximum);
        Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
    }

    public SplashScreenManager ShowProgressSplashScreen(int increaseProgress)
    {
        Progress += increaseProgress;
        return ShowProgressSplashScreen();
    }

    public SplashScreenManager ShowProgressSplashScreen(SplashMessageTypes splashMessageTypes, int increaseProgress, string customMessage = "")
    {
        SplashMessageTypes = splashMessageTypes;
        Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
        Progress += increaseProgress;
        return ShowProgressSplashScreen();
    }

    public SplashScreenManager ShowProgressSplashScreen()
    {
        // ViewModel 생성 
        if (ProgressSplashScreenViewModel == null)
        {
            ProgressSplashScreenViewModel = new DXSplashScreenViewModel();
            ProgressSplashScreenViewModel.IsIndeterminate = false;
        }



        // 변경 값 지정
        ProgressSplashScreenViewModel.Progress = PercentOfMaximum(Progress, Maximum);
        if (SplashMessageTypes.Custom == SplashMessageTypes)
        {
            if (IsShowPercent)
                ProgressSplashScreenViewModel.Status = Status + $" ({Progress:#,0}/{Maximum:#,0}, {PercentOfMaximum(Progress, Maximum)}%)";
            else
                ProgressSplashScreenViewModel.Status = Status + $" ({Progress:#,0}/{Maximum:#,0})";
        }
        else
        {
            if (IsShowPercent)
                ProgressSplashScreenViewModel.Status = $"{Status} ({Progress:#,0}/{Maximum:#,0}, {PercentOfMaximum(Progress, Maximum)}%)";
            else
                ProgressSplashScreenViewModel.Status = $"{Status} ({Progress:#,0}/{Maximum:#,0})";
        }



        // SplashScreenManager 생성
        if (ActiveSplashScreenManager == null)
        {
            ActiveSplashScreenManager = SplashScreenManager.Create(() => new ProgressSplashScreenWindow(), ProgressSplashScreenViewModel);
            ActiveSplashScreenManager.Show();
        }

        return ActiveSplashScreenManager;
    }

    public void CloseSplashScreen()
    {
        ActiveSplashScreenManager?.Close();
        ActiveSplashScreenManager = null;
    }

    public MessageBoxResult MessageBox(string messageBoxText, string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.Information)
    {
        CloseSplashScreen();

        return DXMessageBox.Show(messageBoxText, caption, button, icon);
    }
}
