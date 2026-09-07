using DevExpress.Mvvm;

using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;

namespace Minguk.Base.Utilities;

public static class AppSettingUtility
{
    public static string AppSettingSectionName = "appSettings";

    /// <summary>사용자 설정 파일 이름.</summary>
    public const string FileName = "AppSettings.User.config";

    /// <summary>
    /// 사용자 설정 파일(<see cref="FileName"/>)이 놓이는 폴더.
    /// 기본값은 실행 폴더이지만, 자동 업데이트로 실행 폴더가 통째로 교체되는 앱(Velopack 등)은
    /// 시작 시 이 값을 %AppData% 같은 사용자 프로필 폴더로 바꿔야 설정이 유지된다.
    /// 설정을 처음 읽기 전에 지정할 것.
    /// </summary>
    public static string DirectoryPath { get; set; } = AppDomain.CurrentDomain.BaseDirectory;

    public static string GetFilePath()
    {
        //return ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
        return OpenConfig().FilePath;
    }

    private static Configuration OpenConfig()
    {
        var map = new ExeConfigurationFileMap
        {
            ExeConfigFilename = Path.Combine(DirectoryPath, FileName)
        };
        return ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);
    }

    public static object Get(Control control, string userId, string keyName, object defaultValue)
    {
        object obj;
        string strStringValue = Get($"{control.Name}.{userId}.{keyName}", string.Empty);

        if (!string.IsNullOrEmpty(strStringValue))
        {
            try
            {
                Type type = defaultValue.GetType();
                TypeConverter converter = TypeDescriptor.GetConverter(type);
                obj = converter.ConvertFromString(strStringValue) ?? new();
            }
            catch
            {
                obj = defaultValue;
            }
        }
        else
        {
            obj = defaultValue;
        }

        return obj;
    }

    public static object Get(Control control, string keyName, object defaultValue)
    {
        object obj;
        string strStringValue = Get($"{control.Name}.{keyName}", string.Empty);

        if (!string.IsNullOrEmpty(strStringValue))
        {
            try
            {
                Type type = defaultValue.GetType();
                TypeConverter converter = TypeDescriptor.GetConverter(type);
                obj = converter.ConvertFromString(strStringValue) ?? new();
            }
            catch
            {
                obj = defaultValue;
            }
        }
        else
        {
            obj = defaultValue;
        }

        return obj;
    }

    public static object Get(ViewModelBase viewModel, string keyName, object defaultValue)
    {
        object obj;
        string strStringValue = Get($"{viewModel.GetType().BaseType?.FullName}.{keyName}", string.Empty);

        if (!string.IsNullOrEmpty(strStringValue))
        {
            try
            {
                Type type = defaultValue.GetType();
                TypeConverter converter = TypeDescriptor.GetConverter(type);
                obj = converter.ConvertFromString(strStringValue) ?? new();
            }
            catch
            {
                obj = defaultValue;
            }
        }
        else
        {
            obj = defaultValue;
        }

        return obj;
    }

    public static T Get<T>(ViewModelBase viewModel, string keyName, T defaultValue)
    {
        string fullKeyName = $"{viewModel.GetType().BaseType?.FullName}.{keyName}";
        return Get<T>(fullKeyName, defaultValue);
    }

    public static object Get(string controlName, string keyName, object defaultValue)
    {
        object obj;
        string strStringValue = Get($"{controlName}.{keyName}", string.Empty);

        if (!string.IsNullOrEmpty(strStringValue))
        {
            try
            {
                Type type = defaultValue.GetType();
                TypeConverter converter = TypeDescriptor.GetConverter(type);
                obj = converter.ConvertFromString(strStringValue) ?? new();
            }
            catch
            {
                obj = defaultValue;
            }
        }
        else
        {
            obj = defaultValue;
        }

        return obj;
    }

    public static string Get(string keyName, string defaultValue)
    {
        string strReturn = string.Empty;

        Configuration config = OpenConfig();
        if (config.AppSettings.Settings.AllKeys.Contains(keyName))
        {
            strReturn = config.AppSettings.Settings[keyName].Value;
        }

        // 저장된 값이 string.Empty 일 수 있으니 기본값을 넣어준다.
        if (string.IsNullOrEmpty(strReturn))
        {
            strReturn = defaultValue;
        }

        return strReturn;
    }

    public static T Get<T>(string keyName, T defaultValue)
    {
        T obj = defaultValue;

        Configuration config = OpenConfig();
        if (config.AppSettings.Settings.AllKeys.Contains(keyName))
        {
            string strStringValue = config.AppSettings.Settings[keyName].Value;
            if (!string.IsNullOrEmpty(strStringValue))
            {
                try
                {
                    if (defaultValue != null)
                    {
                        Type type = defaultValue.GetType();
                        TypeConverter converter = TypeDescriptor.GetConverter(type);
                        try
                        {
                            obj = (T)converter.ConvertFromString(strStringValue)!;
                        }
                        catch
                        {
                            obj = defaultValue;
                        }
                    }
                    else 
                        obj = defaultValue;
                }
                catch
                {
                    obj = defaultValue;
                }
            }
            else
            {
                obj = defaultValue;
            }
        }

        return obj;
    }

    public static string Get(string keyName)
    {
        string strReturn = string.Empty;

        Configuration config = OpenConfig();

        if (config.AppSettings.Settings.AllKeys.Contains(keyName))
        {
            strReturn = config.AppSettings.Settings[keyName].Value;
        }

        return strReturn;
    }

    public static void Set(Control control, string userId, string keyName, object value)
    {
        Type type = value.GetType();
        TypeConverter converter = TypeDescriptor.GetConverter(type);

        string? strValue = converter.ConvertToString(value);
        Set($"{control.Name}.{userId}.{keyName}", strValue);
    }

    public static void Set(Control control, string keyName, object value)
    {
        Type type = value.GetType();
        TypeConverter converter = TypeDescriptor.GetConverter(type);

        string? strValue = converter.ConvertToString(value);
        Set($"{control.Name}.{keyName}", strValue);
    }

    public static void Set(ViewModelBase viewModel, string keyName, object value)
    {
        if (value == null)
            return;

        Type type = value.GetType();
        TypeConverter converter = TypeDescriptor.GetConverter(type);

        string? strValue = converter.ConvertToString(value);
        Set($"{viewModel.GetType().BaseType?.FullName}.{keyName}", strValue);
    }

    public static void Set(string? controlName, string keyName, object value)
    {
        Type type = value.GetType();
        TypeConverter converter = TypeDescriptor.GetConverter(type);

        string? strValue = converter.ConvertToString(value);
        Set($"{controlName}.{keyName}", strValue);
    }

    public static void Set(string keyName, string? value)
    {
        Configuration config = OpenConfig();
        if (config.AppSettings.Settings.AllKeys.Contains(keyName))
        {
            // keyName : Key가 있으면 
            if (value == null)
            {
                config.AppSettings.Settings[keyName].Value = null;
            }
            else
            {
                Type type = value.GetType();
                TypeConverter converter = TypeDescriptor.GetConverter(type);

                string? strValue = converter.ConvertToString(value);
                config.AppSettings.Settings[keyName].Value = strValue;
            }
        }
        else
        {
            // keyName : Key가 없으면 
            if (value == null)
            {
                config.AppSettings.Settings.Add(keyName, null);
            }
            else
            {
                Type type = value.GetType();
                TypeConverter converter = TypeDescriptor.GetConverter(type);

                string? strValue = converter.ConvertToString(value);
                config.AppSettings.Settings.Add(keyName, strValue);
            }
        }

        config.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection(AppSettingSectionName);   // 내용 갱신
    }

    public static void Set<T>(string keyName, T? value)
    {
        Configuration config = OpenConfig();
        if (config.AppSettings.Settings.AllKeys.Contains(keyName))
        {
            // keyName : Key가 있으면 
            if (value == null)
            {
                config.AppSettings.Settings[keyName].Value = null;
            }
            else
            {
                Type type = value.GetType();
                TypeConverter converter = TypeDescriptor.GetConverter(type);

                string? strValue = converter.ConvertToString(value);
                config.AppSettings.Settings[keyName].Value = strValue;
            }
        }
        else
        {
            // keyName : Key가 없으면 
            if (value == null)
            {
                config.AppSettings.Settings.Add(keyName, null);
            }
            else
            {
                Type type = value.GetType();
                TypeConverter converter = TypeDescriptor.GetConverter(type);

                string? strValue = converter.ConvertToString(value);
                config.AppSettings.Settings.Add(keyName, strValue);
            }
        }

        config.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection(AppSettingSectionName);   // 내용 갱신
    }

    public static bool Exists(ViewModelBase viewModel, string keyName)
    {
        string key = $"{viewModel.GetType().BaseType?.FullName}.{keyName}";

        Configuration config = OpenConfig();
        if (config.AppSettings.Settings.AllKeys.Contains(key))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public static bool Exists(Control control, string userId, string keyName)
    {
        string key = Get($"{control.Name}.{userId}.{keyName}", string.Empty);

        Configuration config = OpenConfig();
        if (config.AppSettings.Settings.AllKeys.Contains(key))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public static bool Exists(Control control, string keyName)
    {
        string key = Get($"{control.Name}.{keyName}", string.Empty);

        Configuration config = OpenConfig();
        if (config.AppSettings.Settings.AllKeys.Contains(key))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public static bool Exists(string keyName)
    {
        string key = Get($"{keyName}", string.Empty);

        Configuration config = OpenConfig();
        if (config.AppSettings.Settings.AllKeys.Contains(key))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public static void Clear()
    {
        Configuration config = OpenConfig();
        string[] allKeys = config.AppSettings.Settings.AllKeys;

        foreach (string key in allKeys)
        {
            config.AppSettings.Settings.Remove(key);
        }

        config.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection(AppSettingSectionName);   // 내용 갱신
    }
}
