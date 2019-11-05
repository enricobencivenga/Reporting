using System;
using System.Configuration;

namespace Reporting.Utilities
{
    public class ExcelAttribute : Attribute
    {
        //[DefaultSettingValue("")]
        public String ColumnName { get; set; }

        //[DefaultSettingValue("")]
        public Boolean OnlyDate { get; set; }

        //[DefaultSettingValue("")]
        public Boolean OnlyTime { get; set; }

        //[DefaultSettingValue("")]
        public Boolean IsCurrency { get; set; }

        //[DefaultSettingValue("")]
        public Double Width { get; set; }

        //[DefaultSettingValue("")]
        public uint StyleIndex { get; set; }

    }
}
