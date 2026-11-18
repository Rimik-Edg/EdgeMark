using System;
using Microsoft.Win32;

namespace EdgeMark
{
    public static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "EdgeMark";
        private const string OldValueName = "Zakladka"; // так запись называлась до переименования

        /// <summary>
        /// Был включён автозапуск под старым названием — переносим его на этот exe под новым именем.
        /// </summary>
        public static void MigrateOldName()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (k?.GetValue(OldValueName) == null) return;
                k.DeleteValue(OldValueName, false);
                k.SetValue(ValueName, ExeCommand);
            }
            catch { }
        }

        private static string ExeCommand => "\"" + Environment.ProcessPath + "\"";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                    return k?.GetValue(ValueName) is string s &&
                           string.Equals(s, ExeCommand, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                    if (value) k.SetValue(ValueName, ExeCommand);
                    else k.DeleteValue(ValueName, false);
                }
                catch { }
            }
        }
    }
}
