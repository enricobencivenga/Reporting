using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Reporting.Utilities
{
    public class ExcelProvider
    {
        public byte[] File { get; private set; }
        public void Generate<T>(IEnumerable<T> records, IEnumerable<String> fieldsExcluded = null, String name = "Foglio 1")
        {
            File = WorksheetUtilities.CreateGenericReport<T>(records, fieldsExcluded, name);
        }
    }
}