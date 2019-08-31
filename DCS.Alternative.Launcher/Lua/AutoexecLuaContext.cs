﻿using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DCS.Alternative.Launcher.Lua
{
    public class AutoexecLuaContext : LuaContextBase
    {
        private readonly string _autoexecPath;

        public AutoexecLuaContext(InstallLocation install)
        {
            _autoexecPath = install.AutoexecCfg;

            DoString("options = options or {}");
        }

        public void SetValue(string id, object value)
        {
            var optionPaths = id.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var table = string.Empty;

            for (var i = 0; i < optionPaths.Length - 1; i++)
            {
                table = string.IsNullOrEmpty(table) ? optionPaths[i] : string.Join(".", table, optionPaths[i]);
                DoString($"{table} = {table} or {{}}");
            }

            string valueStr;

            if (!(value is string) && value is IEnumerable)
            {
                var enumerable = (IEnumerable)value;
                var values =
                    (value is JArray
                        ? enumerable.OfType<JValue>().Select(j => j.Value)
                        : enumerable)
                    .Cast<object>()
                    .Select(Convert.ToDouble)
                    .ToArray();

                valueStr = $"{{ {string.Join(",", values.Select(i => i.ToString()).ToArray())} }}";
            }
            else
            {
                valueStr = value is bool ? value.ToString().ToLower() : value.ToString();
            }

            DoString($"{id} = {valueStr}");
        }

        public void Save(string optionId)
        {
            DoString($"serializeToFile(\'{_autoexecPath.Replace("\\", "\\\\")}\', \'{optionId}\', {optionId}, 'a')");
        }
    }
}