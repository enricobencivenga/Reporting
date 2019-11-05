using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using X15 = DocumentFormat.OpenXml.Office2013.Excel;
using X14 = DocumentFormat.OpenXml.Office2010.Excel;
using A = DocumentFormat.OpenXml.Drawing;
using Ap = DocumentFormat.OpenXml.ExtendedProperties;
using Vt = DocumentFormat.OpenXml.VariantTypes;
using Thm15 = DocumentFormat.OpenXml.Office2013.Theme;
using System.Reflection;

namespace Reporting.Utilities
{
    /// <summary>
    /// Classe di supporto per la scrittura di file tramite la libreria OpenXML
    /// </summary>
    public class WorksheetUtilities
    {

        /// <summary>
        /// Compila il report per il turnover a partire da un template
        /// </summary>
        /// <param name="turnoverListDto"></param>
        /// <returns></returns>
        public static byte[] CreateGenericReport<T>(IEnumerable<T> genericList, IEnumerable<String> fieldsExcluded, String name)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
                {
                    ExtendedFilePropertiesPart extendedFilePropertiesPart = spreadsheetDocument.AddNewPart<ExtendedFilePropertiesPart>();
                    GenerateExtendedFilePropertiesPartContent(extendedFilePropertiesPart);

                    WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();

                    WorkbookStylesPart workbookStylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                    GenerateWorkbookStylesPartContent(workbookStylesPart);

                    WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

                    SetPackageProperties(spreadsheetDocument);

                    List<OpenXmlAttribute> openXmlAttribute;
                    OpenXmlWriter openXmlWriter = OpenXmlWriter.Create(worksheetPart);

                    openXmlWriter.WriteStartElement(new Worksheet());
                    openXmlWriter.WriteElement(new Columns());
                    openXmlWriter.WriteStartElement(new SheetData());

                    var propertyInfos = GetPropertyInfos<T>(fieldsExcluded).ToList();
                    List<Dictionary<ExcelAttribute, object>> dictionaries = new List<Dictionary<ExcelAttribute, object>>();

                    foreach (var genericObject in genericList)
                    {
                        dictionaries.Add(propertyInfos
                            .ToDictionary(p => p.GetCustomAttributes<ExcelAttribute>(false).First(), p => p.GetValue(genericObject, null)));
                    }

                    openXmlAttribute = new List<OpenXmlAttribute>();
                    openXmlAttribute.Add(new OpenXmlAttribute("r", null, (1).ToString()));

                    openXmlWriter.WriteStartElement(new Row(), openXmlAttribute);

                    var excelAttributes = GetExcelAttributes(propertyInfos);
                    for (int i = 0; i < excelAttributes.Count(); i++)
                    {
                        openXmlWriter.WriteElement(FillAndGetHeaderCell(excelAttributes.ElementAt(i)));
                    }

                    openXmlWriter.WriteEndElement();

                    int currentRow = 0;
                    for (currentRow = 0; currentRow < dictionaries.Count; currentRow++)
                    {
                        openXmlAttribute = new List<OpenXmlAttribute>();
                        // this is the row index
                        openXmlAttribute.Add(new OpenXmlAttribute("r", null, (currentRow + 2).ToString()));

                        openXmlWriter.WriteStartElement(new Row(), openXmlAttribute);

                        for (int j = 0; j < dictionaries.ElementAt(currentRow).Count; j++)
                        {
                            openXmlAttribute = new List<OpenXmlAttribute>();
                            openXmlAttribute.Add(new OpenXmlAttribute("t", null, "str"));
                            openXmlWriter.WriteElement(FillAndGetCell(dictionaries.ElementAt(currentRow).ElementAt(j).Value, dictionaries.ElementAt(currentRow).ElementAt(j).Key));
                        }

                        // this is for Row
                        openXmlWriter.WriteEndElement();
                    }

                    // this is for SheetData
                    openXmlWriter.WriteEndElement();
                    // this is for Worksheet
                    openXmlWriter.WriteEndElement();
                    openXmlWriter.Close();

                    openXmlWriter = OpenXmlWriter.Create(workbookPart);
                    openXmlWriter.WriteStartElement(new Workbook());
                    openXmlWriter.WriteStartElement(new Sheets());


                    openXmlWriter.WriteElement(new Sheet()
                    {
                        Name = name,
                        SheetId = (UInt32Value)1U,
                        Id = spreadsheetDocument.WorkbookPart.
                                GetIdOfPart(worksheetPart)
                    });

                    // this is for Sheets
                    openXmlWriter.WriteEndElement();
                    // this is for Workbook
                    openXmlWriter.WriteEndElement();
                    openXmlWriter.Close();

                    GetAutoSizeColumns(worksheetPart.Worksheet);

                    SetAutoFilter(worksheetPart, workbookPart.Workbook, name, GetColumnLetter(1), 1, GetColumnLetter((uint)(propertyInfos.Count())), currentRow, true);
                    spreadsheetDocument.Close();
                }
                return memoryStream.ToArray();
            }

        }

        #region private methods

        private static IEnumerable<ExcelAttribute> GetExcelAttributes(IEnumerable<PropertyInfo> propertyInfos)
        {
            return propertyInfos.Select(p => p.GetCustomAttributes<ExcelAttribute>(false).First());
        }

        private static IEnumerable<PropertyInfo> GetPropertyInfos<T>(IEnumerable<String> fieldsExcluded)
        {
            fieldsExcluded=fieldsExcluded ?? new String[0];
            var allProperties = typeof(T).GetProperties().Where(x => !fieldsExcluded.Contains(x.Name));
            allProperties = allProperties.Where(p => p.GetCustomAttributes<ExcelAttribute>(false).Count() > 0).OrderBy(x => x.DeclaringType.Name);
            return allProperties;
        }

        #endregion private methods

        #region sheet element
        private static Dictionary<int, int> GetMaxCharacterWidth(SheetData sheetData)
        {
            //iterate over all cells getting a max char value for each column
            Dictionary<int, int> maxColWidth = new Dictionary<int, int>();
            var rows = sheetData.Elements<Row>().Take(5);
            UInt32[] numberStyles = new UInt32[] { 5, 6, 7, 8 }; //styles that will add extra chars
            UInt32[] boldStyles = new UInt32[] { 1, 2, 3, 4, 6, 7, 8 }; //styles that will bold
            foreach (var r in rows)
            {
                var cells = r.Elements<Cell>().ToArray();

                //using cell index as my column
                for (int i = 0; i < cells.Length; i++)
                {
                    var cell = cells[i];
                    var cellValue = cell.CellValue == null ? string.Empty : cell.CellValue.InnerText;
                    var cellTextLength = cellValue.Length;

                    if (cell.StyleIndex != null && numberStyles.Contains(cell.StyleIndex))
                    {
                        int thousandCount = (int)Math.Truncate((double)cellTextLength / 4);

                        //add 3 for '.00' 
                        cellTextLength += (3 + thousandCount);
                    }

                    if (cell.StyleIndex != null && boldStyles.Contains(cell.StyleIndex))
                    {
                        //add an extra char for bold - not 100% acurate but good enough for what i need.
                        cellTextLength += 1;
                    }

                    if (maxColWidth.ContainsKey(i))
                    {
                        var current = maxColWidth[i];
                        if (cellTextLength > current)
                        {
                            maxColWidth[i] = cellTextLength;
                        }
                    }
                    else
                    {
                        maxColWidth.Add(i, cellTextLength);
                    }
                }
            }

            return maxColWidth;
        }
        private static void GetAutoSizeColumns(Worksheet worksheet)
        {
            var sheetData = worksheet.GetFirstChild<SheetData>();
            var maxColWidth = GetMaxCharacterWidth(sheetData);

            var columns = worksheet.Elements<Columns>().First();

            //this is the width of my font - yours may be different
            double maxWidth = 11;

            foreach (var item in maxColWidth)
            {
                //width = Truncate([{Number of Characters} * {Maximum Digit Width} + {5 pixel padding}]/{Maximum Digit Width}*256)/256
                double width = Math.Truncate((item.Value * maxWidth + 10) / (7*maxWidth/10));

                ////pixels=Truncate(((256 * {width} + Truncate(128/{Maximum Digit Width}))/256)*{Maximum Digit Width})
                //double pixels = Math.Truncate(((256 * width + Math.Truncate(128 / maxWidth)) / 256) * maxWidth);

                ////character width=Truncate(({pixels}-5)/{Maximum Digit Width} * 100+0.5)/100
                //double charWidth = Math.Truncate((pixels - 5) / maxWidth * 100 + 0.5) / 100;

                Column col = new Column() { BestFit = true, Min = (UInt32)(item.Key + 1), Max = (UInt32)(item.Key + 1), CustomWidth = true, Width = (DoubleValue)width };
                columns.Append(col);
            }
        }


        private static Cell FillAndGetHeaderCell(ExcelAttribute excelAttribute)
        {
            Cell cell = new Cell();
            cell.DataType = new EnumValue<CellValues>(CellValues.String);
            cell.StyleIndex = excelAttribute.StyleIndex > 0 ? excelAttribute.StyleIndex : 21U;
            cell.CellValue = new CellValue(excelAttribute.ColumnName.ToUpperInvariant());
            return cell;
        }

        private static Cell FillAndGetCell(object value, ExcelAttribute excelAttribute)
        {
            Cell cell = new Cell();
            CellValues dataType = new CellValues();
            String text = String.Empty;
            UInt32 style = 9U;
            if (value == null)
            {
                dataType = CellValues.String;
            }
            else if (value is DateTime?)
            {
                if (excelAttribute.OnlyDate)
                {
                    style = (UInt32Value)24U;
                }
                else if (excelAttribute.OnlyTime)
                {
                    style = (UInt32Value)33U;
                }
                else
                {
                    style = (UInt32Value)42U;
                }
                text = (value as DateTime?).Value.ToOADate().ToString(CultureInfo.InvariantCulture);
                dataType = CellValues.Number;
            }
            else if (value is decimal)
            {
                dataType = CellValues.Number;
                if (excelAttribute.IsCurrency)
                {
                    text = (Math.Round((value as decimal?).Value, 3)).ToString(CultureInfo.InvariantCulture);
                    style = (UInt32Value)69U;
                }
                else
                {
                    text = (Math.Round((value as decimal?).Value / 100, 3)).ToString(CultureInfo.InvariantCulture);
                }

            }
            else if (value is int)
            {
                text = ((value as int?).Value).ToString(CultureInfo.InvariantCulture);
                dataType = CellValues.Number;
            }
            else if (value is double)
            {
                text = ((value as double?).Value).ToString(CultureInfo.InvariantCulture);
                dataType = CellValues.String;
            }
            else if (value is bool?)
            {
                text = ((value as bool?).Value ? "SI" : "NO").ToString(CultureInfo.InvariantCulture);
                dataType = CellValues.String;
            }
            else if (value is String)
            {
                text = (value as String).Replace("\0", String.Empty).ToString(CultureInfo.InvariantCulture);
                dataType = CellValues.String;
            }

            cell.DataType = new EnumValue<CellValues>(dataType);
            cell.StyleIndex = style;
            cell.CellValue = new CellValue(text.ToUpperInvariant());
            return cell;
        }
     
        /// <summary>
        /// Ottiene l'ultimo elemento del WorkSheet di tipo Sheet*
        /// Serve per aggiungere il filtro AutoFilter nella posizione corretta, per non ottenere errori di visualizzazione e di apertura del file
        /// </summary>
        /// <param name="worksheetPart"></param>
        /// <returns></returns>
        private static OpenXmlElement GetPreceedingElement(WorksheetPart worksheetPart)
        {
            List<Type> elements = new List<Type>()
            {
                typeof(Scenarios),
                typeof(ProtectedRanges),
                typeof(SheetProtection),
                typeof(SheetCalculationProperties),
                typeof(SheetData)
            };

            OpenXmlElement preceedingElement = null;
            foreach (var item in worksheetPart.Worksheet.ChildElements.Reverse())
            {
                if (elements.Contains(item.GetType()))
                {
                    preceedingElement = item;
                    break;
                }
            }

            return preceedingElement;
        }

        /// <summary>
        /// Aggiunge un filtro al worksheet a partire da dati e celle definite
        /// </summary>
        /// <param name="worksheetPart"></param>
        /// <param name="workbook"></param>
        /// <param name="sheet"></param>
        /// <param name="firstColumnLetter"></param>
        /// <param name="topRowIndex"></param>
        /// <param name="lastColumnLetter"></param>
        /// <param name="bottomRowIndex"></param>
        private static void SetAutoFilter(WorksheetPart worksheetPart, Workbook workbook, String sheetName,
            String firstColumnLetter, int topRowIndex, String lastColumnLetter, int bottomRowIndex, bool setSortState = false)
        {
            AutoFilter autoFilter = new AutoFilter() { Reference = firstColumnLetter + topRowIndex + ":" + lastColumnLetter + topRowIndex };

            if (setSortState)
            {
                SortState sortState = new SortState() { Reference = firstColumnLetter + topRowIndex + ":" + lastColumnLetter + topRowIndex };
                SortCondition sortCondition = new SortCondition() { Descending = true, Reference = lastColumnLetter + topRowIndex };
                sortState.Append(sortCondition);
                autoFilter.Append(sortState);
            }

            worksheetPart.Worksheet.InsertAfter(autoFilter, GetPreceedingElement(worksheetPart));

            if (workbook.DefinedNames == null)
                workbook.DefinedNames = new DefinedNames();

            int autofilterCount = workbook.DefinedNames.Count();
            workbook.DefinedNames.Append(new DefinedName(string.Format("'{0}'!${1}${2}:${3}${4}",
                sheetName, firstColumnLetter, topRowIndex, lastColumnLetter, bottomRowIndex))
            {
                Name = "_xlnm._FilterDatabase",
                LocalSheetId = (uint)autofilterCount,
                Hidden = true
            });
        }

        /// <summary>
        /// Restituisce l'indice alfabetico della colonna a partire dal numero. Es (1 => A)
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private static string GetColumnLetter(uint index)
        {
            index -= 1; //adjust so it matches 0-indexed array rather than 1-indexed column

            uint quotient = index / 26;
            if (quotient > 0)
                return GetColumnLetter(quotient) + chars[index % 26].ToString();
            else
                return chars[index % 26].ToString();
        }

        private static char[] chars = new char[] { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };
        #endregion sheet element


        #region create sheet

        /// <summary>
        /// Genera le proprietà dello sheet
        /// </summary>
        /// <param name="workbookPart"></param>
        private static void GenerateExtendedFilePropertiesPartContent(ExtendedFilePropertiesPart extendedFilePropertiesPart)
        {
            Ap.Properties properties1 = new Ap.Properties();
            properties1.AddNamespaceDeclaration("vt", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
            Ap.Application application1 = new Ap.Application();
            application1.Text = "Microsoft Excel";
            Ap.DocumentSecurity documentSecurity1 = new Ap.DocumentSecurity();
            documentSecurity1.Text = "0";
            Ap.ScaleCrop scaleCrop1 = new Ap.ScaleCrop();
            scaleCrop1.Text = "false";

            Ap.HeadingPairs headingPairs1 = new Ap.HeadingPairs();

            Vt.VTVector vTVector1 = new Vt.VTVector() { BaseType = Vt.VectorBaseValues.Variant, Size = (UInt32Value)2U };

            Vt.Variant variant1 = new Vt.Variant();
            Vt.VTLPSTR vTLPSTR1 = new Vt.VTLPSTR();
            vTLPSTR1.Text = "Fogli di lavoro";

            variant1.Append(vTLPSTR1);

            Vt.Variant variant2 = new Vt.Variant();
            Vt.VTInt32 vTInt321 = new Vt.VTInt32();
            vTInt321.Text = "1";

            variant2.Append(vTInt321);

            vTVector1.Append(variant1);
            vTVector1.Append(variant2);

            headingPairs1.Append(vTVector1);

            Ap.TitlesOfParts titlesOfParts1 = new Ap.TitlesOfParts();

            Vt.VTVector vTVector2 = new Vt.VTVector() { BaseType = Vt.VectorBaseValues.Lpstr, Size = (UInt32Value)1U };
            Vt.VTLPSTR vTLPSTR2 = new Vt.VTLPSTR();
            vTLPSTR2.Text = "Foglio1";

            vTVector2.Append(vTLPSTR2);

            titlesOfParts1.Append(vTVector2);
            Ap.Company company1 = new Ap.Company();
            company1.Text = "";
            Ap.LinksUpToDate linksUpToDate1 = new Ap.LinksUpToDate();
            linksUpToDate1.Text = "false";
            Ap.SharedDocument sharedDocument1 = new Ap.SharedDocument();
            sharedDocument1.Text = "false";
            Ap.HyperlinksChanged hyperlinksChanged1 = new Ap.HyperlinksChanged();
            hyperlinksChanged1.Text = "false";
            Ap.ApplicationVersion applicationVersion1 = new Ap.ApplicationVersion();
            applicationVersion1.Text = "15.0300";

            properties1.Append(application1);
            properties1.Append(documentSecurity1);
            properties1.Append(scaleCrop1);
            properties1.Append(headingPairs1);
            properties1.Append(titlesOfParts1);
            properties1.Append(company1);
            properties1.Append(linksUpToDate1);
            properties1.Append(sharedDocument1);
            properties1.Append(hyperlinksChanged1);
            properties1.Append(applicationVersion1);

            extendedFilePropertiesPart.Properties = properties1;
        }
      
        /// <summary>
        ///  Genera il foglio di style utilizzato per il WorkSheet dei report.
        ///  Definisce font, colori, rappresentazioni di numeri e date
        ///  Per conoscere gli stili presenti, vedere il tipo @StyleEnum
        /// </summary>
        /// <param name="workbookStylesPart"></param>
        // Generates content of workbookStylesPart1.
        // Generates content of workbookStylesPart1.
        private static void GenerateWorkbookStylesPartContent(WorkbookStylesPart workbookStylesPart)
        {
            Stylesheet stylesheet1 = new Stylesheet() { MCAttributes = new MarkupCompatibilityAttributes() { Ignorable = "x14ac" } };
            stylesheet1.AddNamespaceDeclaration("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
            stylesheet1.AddNamespaceDeclaration("x14ac", "http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac");

            NumberingFormats numberingFormats1 = new NumberingFormats() { Count = (UInt32Value)2U };
            NumberingFormat numberingFormat1 = new NumberingFormat() { NumberFormatId = (UInt32Value)164U, FormatCode = "dd/mm/yyyy\\ hh:mm;@" };
            NumberingFormat numberingFormat2 = new NumberingFormat() { NumberFormatId = (UInt32Value)165U, FormatCode = "#,##0.00\\ \"€\"" };

            numberingFormats1.Append(numberingFormat1);
            numberingFormats1.Append(numberingFormat2);

            Fonts fonts1 = new Fonts() { Count = (UInt32Value)7U, KnownFonts = true };

            Font font1 = new Font();
            FontSize fontSize1 = new FontSize() { Val = 11D };
            Color color1 = new Color() { Theme = (UInt32Value)1U };
            FontName fontName1 = new FontName() { Val = "Calibri" };
            FontFamilyNumbering fontFamilyNumbering1 = new FontFamilyNumbering() { Val = 2 };
            FontScheme fontScheme1 = new FontScheme() { Val = FontSchemeValues.Minor };

            font1.Append(fontSize1);
            font1.Append(color1);
            font1.Append(fontName1);
            font1.Append(fontFamilyNumbering1);
            font1.Append(fontScheme1);

            Font font2 = new Font();
            Bold bold1 = new Bold();
            FontSize fontSize2 = new FontSize() { Val = 11D };
            Color color2 = new Color() { Theme = (UInt32Value)0U };
            FontName fontName2 = new FontName() { Val = "Calibri" };
            FontFamilyNumbering fontFamilyNumbering2 = new FontFamilyNumbering() { Val = 2 };
            FontScheme fontScheme2 = new FontScheme() { Val = FontSchemeValues.Minor };

            font2.Append(bold1);
            font2.Append(fontSize2);
            font2.Append(color2);
            font2.Append(fontName2);
            font2.Append(fontFamilyNumbering2);
            font2.Append(fontScheme2);

            Font font3 = new Font();
            Bold bold2 = new Bold();
            FontSize fontSize3 = new FontSize() { Val = 11D };
            Color color3 = new Color() { Theme = (UInt32Value)1U };
            FontName fontName3 = new FontName() { Val = "Calibri" };
            FontFamilyNumbering fontFamilyNumbering3 = new FontFamilyNumbering() { Val = 2 };
            FontScheme fontScheme3 = new FontScheme() { Val = FontSchemeValues.Minor };

            font3.Append(bold2);
            font3.Append(fontSize3);
            font3.Append(color3);
            font3.Append(fontName3);
            font3.Append(fontFamilyNumbering3);
            font3.Append(fontScheme3);

            Font font4 = new Font();
            FontSize fontSize4 = new FontSize() { Val = 11D };
            Color color4 = new Color() { Theme = (UInt32Value)0U };
            FontName fontName4 = new FontName() { Val = "Calibri" };
            FontFamilyNumbering fontFamilyNumbering4 = new FontFamilyNumbering() { Val = 2 };
            FontScheme fontScheme4 = new FontScheme() { Val = FontSchemeValues.Minor };

            font4.Append(fontSize4);
            font4.Append(color4);
            font4.Append(fontName4);
            font4.Append(fontFamilyNumbering4);
            font4.Append(fontScheme4);

            Font font5 = new Font();
            Italic italic1 = new Italic();
            FontSize fontSize5 = new FontSize() { Val = 11D };
            Color color5 = new Color() { Theme = (UInt32Value)1U };
            FontName fontName5 = new FontName() { Val = "Calibri" };
            FontFamilyNumbering fontFamilyNumbering5 = new FontFamilyNumbering() { Val = 2 };
            FontScheme fontScheme5 = new FontScheme() { Val = FontSchemeValues.Minor };

            font5.Append(italic1);
            font5.Append(fontSize5);
            font5.Append(color5);
            font5.Append(fontName5);
            font5.Append(fontFamilyNumbering5);
            font5.Append(fontScheme5);

            Font font6 = new Font();
            FontSize fontSize6 = new FontSize() { Val = 11D };
            Color color6 = new Color() { Theme = (UInt32Value)1U, Tint = 0.249977111117893D };
            FontName fontName6 = new FontName() { Val = "Calibri" };
            FontFamilyNumbering fontFamilyNumbering6 = new FontFamilyNumbering() { Val = 2 };
            FontScheme fontScheme6 = new FontScheme() { Val = FontSchemeValues.Minor };

            font6.Append(fontSize6);
            font6.Append(color6);
            font6.Append(fontName6);
            font6.Append(fontFamilyNumbering6);
            font6.Append(fontScheme6);

            Font font7 = new Font();
            Bold bold3 = new Bold();
            FontSize fontSize7 = new FontSize() { Val = 11D };
            Color color7 = new Color() { Theme = (UInt32Value)1U, Tint = 0.249977111117893D };
            FontName fontName7 = new FontName() { Val = "Calibri" };
            FontFamilyNumbering fontFamilyNumbering7 = new FontFamilyNumbering() { Val = 2 };
            FontScheme fontScheme7 = new FontScheme() { Val = FontSchemeValues.Minor };

            font7.Append(bold3);
            font7.Append(fontSize7);
            font7.Append(color7);
            font7.Append(fontName7);
            font7.Append(fontFamilyNumbering7);
            font7.Append(fontScheme7);

            fonts1.Append(font1);
            fonts1.Append(font2);
            fonts1.Append(font3);
            fonts1.Append(font4);
            fonts1.Append(font5);
            fonts1.Append(font6);
            fonts1.Append(font7);

            Fills fills1 = new Fills() { Count = (UInt32Value)16U };

            Fill fill1 = new Fill();
            PatternFill patternFill1 = new PatternFill() { PatternType = PatternValues.None };

            fill1.Append(patternFill1);

            Fill fill2 = new Fill();
            PatternFill patternFill2 = new PatternFill() { PatternType = PatternValues.Gray125 };

            fill2.Append(patternFill2);

            Fill fill3 = new Fill();

            PatternFill patternFill3 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor1 = new ForegroundColor() { Theme = (UInt32Value)3U };
            BackgroundColor backgroundColor1 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill3.Append(foregroundColor1);
            patternFill3.Append(backgroundColor1);

            fill3.Append(patternFill3);

            Fill fill4 = new Fill();

            PatternFill patternFill4 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor2 = new ForegroundColor() { Theme = (UInt32Value)4U };
            BackgroundColor backgroundColor2 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill4.Append(foregroundColor2);
            patternFill4.Append(backgroundColor2);

            fill4.Append(patternFill4);

            Fill fill5 = new Fill();

            PatternFill patternFill5 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor3 = new ForegroundColor() { Theme = (UInt32Value)4U, Tint = 0.79998168889431442D };
            BackgroundColor backgroundColor3 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill5.Append(foregroundColor3);
            patternFill5.Append(backgroundColor3);

            fill5.Append(patternFill5);

            Fill fill6 = new Fill();

            PatternFill patternFill6 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor4 = new ForegroundColor() { Theme = (UInt32Value)3U, Tint = 0.79998168889431442D };
            BackgroundColor backgroundColor4 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill6.Append(foregroundColor4);
            patternFill6.Append(backgroundColor4);

            fill6.Append(patternFill6);

            Fill fill7 = new Fill();

            PatternFill patternFill7 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor5 = new ForegroundColor() { Theme = (UInt32Value)5U, Tint = 0.59999389629810485D };
            BackgroundColor backgroundColor5 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill7.Append(foregroundColor5);
            patternFill7.Append(backgroundColor5);

            fill7.Append(patternFill7);

            Fill fill8 = new Fill();

            PatternFill patternFill8 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor6 = new ForegroundColor() { Theme = (UInt32Value)9U, Tint = 0.59999389629810485D };
            BackgroundColor backgroundColor6 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill8.Append(foregroundColor6);
            patternFill8.Append(backgroundColor6);

            fill8.Append(patternFill8);

            Fill fill9 = new Fill();

            PatternFill patternFill9 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor7 = new ForegroundColor() { Theme = (UInt32Value)7U };
            BackgroundColor backgroundColor7 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill9.Append(foregroundColor7);
            patternFill9.Append(backgroundColor7);

            fill9.Append(patternFill9);

            Fill fill10 = new Fill();

            PatternFill patternFill10 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor8 = new ForegroundColor() { Theme = (UInt32Value)6U };
            BackgroundColor backgroundColor8 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill10.Append(foregroundColor8);
            patternFill10.Append(backgroundColor8);

            fill10.Append(patternFill10);

            Fill fill11 = new Fill();

            PatternFill patternFill11 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor9 = new ForegroundColor() { Theme = (UInt32Value)2U };
            BackgroundColor backgroundColor9 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill11.Append(foregroundColor9);
            patternFill11.Append(backgroundColor9);

            fill11.Append(patternFill11);

            Fill fill12 = new Fill();

            PatternFill patternFill12 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor10 = new ForegroundColor() { Theme = (UInt32Value)1U };
            BackgroundColor backgroundColor10 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill12.Append(foregroundColor10);
            patternFill12.Append(backgroundColor10);

            fill12.Append(patternFill12);

            Fill fill13 = new Fill();

            PatternFill patternFill13 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor11 = new ForegroundColor() { Theme = (UInt32Value)8U };
            BackgroundColor backgroundColor11 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill13.Append(foregroundColor11);
            patternFill13.Append(backgroundColor11);

            fill13.Append(patternFill13);

            Fill fill14 = new Fill();

            PatternFill patternFill14 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor12 = new ForegroundColor() { Theme = (UInt32Value)0U };
            BackgroundColor backgroundColor12 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill14.Append(foregroundColor12);
            patternFill14.Append(backgroundColor12);

            fill14.Append(patternFill14);

            Fill fill15 = new Fill();

            PatternFill patternFill15 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor13 = new ForegroundColor() { Theme = (UInt32Value)5U, Tint = -0.249977111117893D };
            BackgroundColor backgroundColor13 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill15.Append(foregroundColor13);
            patternFill15.Append(backgroundColor13);

            fill15.Append(patternFill15);

            Fill fill16 = new Fill();

            PatternFill patternFill16 = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor foregroundColor14 = new ForegroundColor() { Theme = (UInt32Value)9U, Tint = -0.249977111117893D };
            BackgroundColor backgroundColor14 = new BackgroundColor() { Indexed = (UInt32Value)64U };

            patternFill16.Append(foregroundColor14);
            patternFill16.Append(backgroundColor14);

            fill16.Append(patternFill16);

            fills1.Append(fill1);
            fills1.Append(fill2);
            fills1.Append(fill3);
            fills1.Append(fill4);
            fills1.Append(fill5);
            fills1.Append(fill6);
            fills1.Append(fill7);
            fills1.Append(fill8);
            fills1.Append(fill9);
            fills1.Append(fill10);
            fills1.Append(fill11);
            fills1.Append(fill12);
            fills1.Append(fill13);
            fills1.Append(fill14);
            fills1.Append(fill15);
            fills1.Append(fill16);

            Borders borders1 = new Borders() { Count = (UInt32Value)4U };

            Border border1 = new Border();
            LeftBorder leftBorder1 = new LeftBorder();
            RightBorder rightBorder1 = new RightBorder();
            TopBorder topBorder1 = new TopBorder();
            BottomBorder bottomBorder1 = new BottomBorder();
            DiagonalBorder diagonalBorder1 = new DiagonalBorder();

            border1.Append(leftBorder1);
            border1.Append(rightBorder1);
            border1.Append(topBorder1);
            border1.Append(bottomBorder1);
            border1.Append(diagonalBorder1);

            Border border2 = new Border();

            LeftBorder leftBorder2 = new LeftBorder() { Style = BorderStyleValues.Dashed };
            Color color8 = new Color() { Auto = true };

            leftBorder2.Append(color8);

            RightBorder rightBorder2 = new RightBorder() { Style = BorderStyleValues.Dashed };
            Color color9 = new Color() { Auto = true };

            rightBorder2.Append(color9);

            TopBorder topBorder2 = new TopBorder() { Style = BorderStyleValues.Dashed };
            Color color10 = new Color() { Auto = true };

            topBorder2.Append(color10);

            BottomBorder bottomBorder2 = new BottomBorder() { Style = BorderStyleValues.Dashed };
            Color color11 = new Color() { Auto = true };

            bottomBorder2.Append(color11);
            DiagonalBorder diagonalBorder2 = new DiagonalBorder();

            border2.Append(leftBorder2);
            border2.Append(rightBorder2);
            border2.Append(topBorder2);
            border2.Append(bottomBorder2);
            border2.Append(diagonalBorder2);

            Border border3 = new Border();
            LeftBorder leftBorder3 = new LeftBorder();
            RightBorder rightBorder3 = new RightBorder();
            TopBorder topBorder3 = new TopBorder();

            BottomBorder bottomBorder3 = new BottomBorder() { Style = BorderStyleValues.Thin };
            Color color12 = new Color() { Theme = (UInt32Value)2U };

            bottomBorder3.Append(color12);
            DiagonalBorder diagonalBorder3 = new DiagonalBorder();

            border3.Append(leftBorder3);
            border3.Append(rightBorder3);
            border3.Append(topBorder3);
            border3.Append(bottomBorder3);
            border3.Append(diagonalBorder3);

            Border border4 = new Border();

            LeftBorder leftBorder4 = new LeftBorder() { Style = BorderStyleValues.Thin };
            Color color13 = new Color() { Theme = (UInt32Value)2U, Tint = -0.24994659260841701D };

            leftBorder4.Append(color13);

            RightBorder rightBorder4 = new RightBorder() { Style = BorderStyleValues.Thin };
            Color color14 = new Color() { Theme = (UInt32Value)2U, Tint = -0.24994659260841701D };

            rightBorder4.Append(color14);

            TopBorder topBorder4 = new TopBorder() { Style = BorderStyleValues.Thin };
            Color color15 = new Color() { Theme = (UInt32Value)2U, Tint = -0.24994659260841701D };

            topBorder4.Append(color15);

            BottomBorder bottomBorder4 = new BottomBorder() { Style = BorderStyleValues.Thin };
            Color color16 = new Color() { Theme = (UInt32Value)2U, Tint = -0.24994659260841701D };

            bottomBorder4.Append(color16);
            DiagonalBorder diagonalBorder4 = new DiagonalBorder();

            border4.Append(leftBorder4);
            border4.Append(rightBorder4);
            border4.Append(topBorder4);
            border4.Append(bottomBorder4);
            border4.Append(diagonalBorder4);

            borders1.Append(border1);
            borders1.Append(border2);
            borders1.Append(border3);
            borders1.Append(border4);

            CellStyleFormats cellStyleFormats1 = new CellStyleFormats() { Count = (UInt32Value)1U };
            CellFormat cellFormat1 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)0U };

            cellStyleFormats1.Append(cellFormat1);

            CellFormats cellFormats1 = new CellFormats() { Count = (UInt32Value)111U };
            CellFormat cellFormat2 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)0U, FormatId = (UInt32Value)0U };
            CellFormat cellFormat3 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)1U, FormatId = (UInt32Value)0U, ApplyBorder = true };
            CellFormat cellFormat4 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)2U, FormatId = (UInt32Value)0U, ApplyBorder = true };

            CellFormat cellFormat5 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)10U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment1 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat5.Append(alignment1);

            CellFormat cellFormat6 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)10U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment2 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat6.Append(alignment2);

            CellFormat cellFormat7 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)10U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment3 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat7.Append(alignment3);

            CellFormat cellFormat8 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)10U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment4 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat8.Append(alignment4);

            CellFormat cellFormat9 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)10U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment5 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat9.Append(alignment5);

            CellFormat cellFormat10 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)10U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment6 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat10.Append(alignment6);
            CellFormat cellFormat11 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyBorder = true };
            CellFormat cellFormat12 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyBorder = true };
            CellFormat cellFormat13 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)4U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyBorder = true };

            CellFormat cellFormat14 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)2U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment7 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat14.Append(alignment7);

            CellFormat cellFormat15 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)2U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment8 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat15.Append(alignment8);

            CellFormat cellFormat16 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)2U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment9 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat16.Append(alignment9);

            CellFormat cellFormat17 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)2U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment10 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat17.Append(alignment10);

            CellFormat cellFormat18 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)2U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment11 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat18.Append(alignment11);

            CellFormat cellFormat19 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)2U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment12 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat19.Append(alignment12);

            CellFormat cellFormat20 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)3U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment13 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat20.Append(alignment13);

            CellFormat cellFormat21 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)3U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment14 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat21.Append(alignment14);

            CellFormat cellFormat22 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)3U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment15 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat22.Append(alignment15);

            CellFormat cellFormat23 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)3U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment16 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat23.Append(alignment16);

            CellFormat cellFormat24 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)3U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment17 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat24.Append(alignment17);

            CellFormat cellFormat25 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)3U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment18 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat25.Append(alignment18);
            CellFormat cellFormat26 = new CellFormat() { NumberFormatId = (UInt32Value)14U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyBorder = true };
            CellFormat cellFormat27 = new CellFormat() { NumberFormatId = (UInt32Value)14U, FontId = (UInt32Value)2U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true };
            CellFormat cellFormat28 = new CellFormat() { NumberFormatId = (UInt32Value)14U, FontId = (UInt32Value)4U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true };

            CellFormat cellFormat29 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)14U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment19 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat29.Append(alignment19);

            CellFormat cellFormat30 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)14U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment20 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat30.Append(alignment20);

            CellFormat cellFormat31 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)14U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment21 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat31.Append(alignment21);

            CellFormat cellFormat32 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)14U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment22 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat32.Append(alignment22);

            CellFormat cellFormat33 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)14U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment23 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat33.Append(alignment23);

            CellFormat cellFormat34 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)14U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment24 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat34.Append(alignment24);
            CellFormat cellFormat35 = new CellFormat() { NumberFormatId = (UInt32Value)20U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyBorder = true };
            CellFormat cellFormat36 = new CellFormat() { NumberFormatId = (UInt32Value)20U, FontId = (UInt32Value)2U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true };
            CellFormat cellFormat37 = new CellFormat() { NumberFormatId = (UInt32Value)20U, FontId = (UInt32Value)4U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true };

            CellFormat cellFormat38 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)9U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment25 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat38.Append(alignment25);

            CellFormat cellFormat39 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)9U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment26 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat39.Append(alignment26);

            CellFormat cellFormat40 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)9U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment27 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat40.Append(alignment27);

            CellFormat cellFormat41 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)9U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment28 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat41.Append(alignment28);

            CellFormat cellFormat42 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)9U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment29 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat42.Append(alignment29);

            CellFormat cellFormat43 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)9U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment30 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat43.Append(alignment30);
            CellFormat cellFormat44 = new CellFormat() { NumberFormatId = (UInt32Value)164U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyBorder = true };
            CellFormat cellFormat45 = new CellFormat() { NumberFormatId = (UInt32Value)164U, FontId = (UInt32Value)2U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true };
            CellFormat cellFormat46 = new CellFormat() { NumberFormatId = (UInt32Value)164U, FontId = (UInt32Value)4U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true };

            CellFormat cellFormat47 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)5U, FillId = (UInt32Value)8U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment31 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat47.Append(alignment31);

            CellFormat cellFormat48 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)6U, FillId = (UInt32Value)8U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment32 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat48.Append(alignment32);

            CellFormat cellFormat49 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)5U, FillId = (UInt32Value)8U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment33 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat49.Append(alignment33);

            CellFormat cellFormat50 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)6U, FillId = (UInt32Value)8U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment34 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat50.Append(alignment34);

            CellFormat cellFormat51 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)5U, FillId = (UInt32Value)8U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment35 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat51.Append(alignment35);

            CellFormat cellFormat52 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)6U, FillId = (UInt32Value)8U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment36 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat52.Append(alignment36);

            CellFormat cellFormat53 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment37 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat53.Append(alignment37);

            CellFormat cellFormat54 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment38 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat54.Append(alignment38);

            CellFormat cellFormat55 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)4U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment39 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat55.Append(alignment39);

            CellFormat cellFormat56 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)12U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment40 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat56.Append(alignment40);

            CellFormat cellFormat57 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)12U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment41 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat57.Append(alignment41);

            CellFormat cellFormat58 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)12U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment42 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat58.Append(alignment42);

            CellFormat cellFormat59 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)12U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment43 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat59.Append(alignment43);

            CellFormat cellFormat60 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)12U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment44 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat60.Append(alignment44);

            CellFormat cellFormat61 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)12U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment45 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat61.Append(alignment45);

            CellFormat cellFormat62 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment46 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat62.Append(alignment46);

            CellFormat cellFormat63 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment47 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat63.Append(alignment47);

            CellFormat cellFormat64 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)4U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment48 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat64.Append(alignment48);

            CellFormat cellFormat65 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)15U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment49 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat65.Append(alignment49);

            CellFormat cellFormat66 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)15U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment50 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat66.Append(alignment50);

            CellFormat cellFormat67 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)15U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment51 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat67.Append(alignment51);

            CellFormat cellFormat68 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)15U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment52 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat68.Append(alignment52);

            CellFormat cellFormat69 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)15U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment53 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat69.Append(alignment53);

            CellFormat cellFormat70 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)15U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment54 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat70.Append(alignment54);
            CellFormat cellFormat71 = new CellFormat() { NumberFormatId = (UInt32Value)165U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyBorder = true, ApplyAlignment = true };
            CellFormat cellFormat72 = new CellFormat() { NumberFormatId = (UInt32Value)165U, FontId = (UInt32Value)2U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true };
            CellFormat cellFormat73 = new CellFormat() { NumberFormatId = (UInt32Value)165U, FontId = (UInt32Value)4U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true };

            CellFormat cellFormat74 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)6U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment55 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat74.Append(alignment55);

            CellFormat cellFormat75 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)6U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment56 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat75.Append(alignment56);

            CellFormat cellFormat76 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)6U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment57 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat76.Append(alignment57);

            CellFormat cellFormat77 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)6U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment58 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat77.Append(alignment58);

            CellFormat cellFormat78 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)6U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment59 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat78.Append(alignment59);

            CellFormat cellFormat79 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)6U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment60 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat79.Append(alignment60);
            CellFormat cellFormat80 = new CellFormat() { NumberFormatId = (UInt32Value)10U, FontId = (UInt32Value)0U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyBorder = true };
            CellFormat cellFormat81 = new CellFormat() { NumberFormatId = (UInt32Value)10U, FontId = (UInt32Value)2U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true };
            CellFormat cellFormat82 = new CellFormat() { NumberFormatId = (UInt32Value)10U, FontId = (UInt32Value)4U, FillId = (UInt32Value)0U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true };

            CellFormat cellFormat83 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)4U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment61 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat83.Append(alignment61);

            CellFormat cellFormat84 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)4U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment62 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat84.Append(alignment62);

            CellFormat cellFormat85 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)4U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment63 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat85.Append(alignment63);

            CellFormat cellFormat86 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)4U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment64 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat86.Append(alignment64);

            CellFormat cellFormat87 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)4U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment65 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat87.Append(alignment65);

            CellFormat cellFormat88 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)4U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment66 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat88.Append(alignment66);

            CellFormat cellFormat89 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)7U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment67 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat89.Append(alignment67);

            CellFormat cellFormat90 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)7U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment68 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat90.Append(alignment68);

            CellFormat cellFormat91 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)7U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment69 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat91.Append(alignment69);

            CellFormat cellFormat92 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)7U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment70 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat92.Append(alignment70);

            CellFormat cellFormat93 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)7U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment71 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat93.Append(alignment71);

            CellFormat cellFormat94 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)7U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment72 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat94.Append(alignment72);

            CellFormat cellFormat95 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)5U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment73 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat95.Append(alignment73);

            CellFormat cellFormat96 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)5U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment74 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat96.Append(alignment74);

            CellFormat cellFormat97 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)5U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment75 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat97.Append(alignment75);

            CellFormat cellFormat98 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)5U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment76 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat98.Append(alignment76);

            CellFormat cellFormat99 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)5U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment77 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat99.Append(alignment77);

            CellFormat cellFormat100 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)5U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment78 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat100.Append(alignment78);

            CellFormat cellFormat101 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)11U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment79 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat101.Append(alignment79);

            CellFormat cellFormat102 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)11U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment80 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat102.Append(alignment80);

            CellFormat cellFormat103 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)11U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment81 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat103.Append(alignment81);

            CellFormat cellFormat104 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)11U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment82 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat104.Append(alignment82);

            CellFormat cellFormat105 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)3U, FillId = (UInt32Value)11U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment83 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat105.Append(alignment83);

            CellFormat cellFormat106 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)1U, FillId = (UInt32Value)11U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment84 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat106.Append(alignment84);

            CellFormat cellFormat107 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)13U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment85 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat107.Append(alignment85);

            CellFormat cellFormat108 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)13U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment86 = new Alignment() { Horizontal = HorizontalAlignmentValues.Left };

            cellFormat108.Append(alignment86);

            CellFormat cellFormat109 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)13U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment87 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat109.Append(alignment87);

            CellFormat cellFormat110 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)13U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment88 = new Alignment() { Horizontal = HorizontalAlignmentValues.Center };

            cellFormat110.Append(alignment88);

            CellFormat cellFormat111 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)0U, FillId = (UInt32Value)13U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment89 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat111.Append(alignment89);

            CellFormat cellFormat112 = new CellFormat() { NumberFormatId = (UInt32Value)0U, FontId = (UInt32Value)2U, FillId = (UInt32Value)13U, BorderId = (UInt32Value)3U, FormatId = (UInt32Value)0U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true };
            Alignment alignment90 = new Alignment() { Horizontal = HorizontalAlignmentValues.Right };

            cellFormat112.Append(alignment90);

            cellFormats1.Append(cellFormat2);
            cellFormats1.Append(cellFormat3);
            cellFormats1.Append(cellFormat4);
            cellFormats1.Append(cellFormat5);
            cellFormats1.Append(cellFormat6);
            cellFormats1.Append(cellFormat7);
            cellFormats1.Append(cellFormat8);
            cellFormats1.Append(cellFormat9);
            cellFormats1.Append(cellFormat10);
            cellFormats1.Append(cellFormat11);
            cellFormats1.Append(cellFormat12);
            cellFormats1.Append(cellFormat13);
            cellFormats1.Append(cellFormat14);
            cellFormats1.Append(cellFormat15);
            cellFormats1.Append(cellFormat16);
            cellFormats1.Append(cellFormat17);
            cellFormats1.Append(cellFormat18);
            cellFormats1.Append(cellFormat19);
            cellFormats1.Append(cellFormat20);
            cellFormats1.Append(cellFormat21);
            cellFormats1.Append(cellFormat22);
            cellFormats1.Append(cellFormat23);
            cellFormats1.Append(cellFormat24);
            cellFormats1.Append(cellFormat25);
            cellFormats1.Append(cellFormat26);
            cellFormats1.Append(cellFormat27);
            cellFormats1.Append(cellFormat28);
            cellFormats1.Append(cellFormat29);
            cellFormats1.Append(cellFormat30);
            cellFormats1.Append(cellFormat31);
            cellFormats1.Append(cellFormat32);
            cellFormats1.Append(cellFormat33);
            cellFormats1.Append(cellFormat34);
            cellFormats1.Append(cellFormat35);
            cellFormats1.Append(cellFormat36);
            cellFormats1.Append(cellFormat37);
            cellFormats1.Append(cellFormat38);
            cellFormats1.Append(cellFormat39);
            cellFormats1.Append(cellFormat40);
            cellFormats1.Append(cellFormat41);
            cellFormats1.Append(cellFormat42);
            cellFormats1.Append(cellFormat43);
            cellFormats1.Append(cellFormat44);
            cellFormats1.Append(cellFormat45);
            cellFormats1.Append(cellFormat46);
            cellFormats1.Append(cellFormat47);
            cellFormats1.Append(cellFormat48);
            cellFormats1.Append(cellFormat49);
            cellFormats1.Append(cellFormat50);
            cellFormats1.Append(cellFormat51);
            cellFormats1.Append(cellFormat52);
            cellFormats1.Append(cellFormat53);
            cellFormats1.Append(cellFormat54);
            cellFormats1.Append(cellFormat55);
            cellFormats1.Append(cellFormat56);
            cellFormats1.Append(cellFormat57);
            cellFormats1.Append(cellFormat58);
            cellFormats1.Append(cellFormat59);
            cellFormats1.Append(cellFormat60);
            cellFormats1.Append(cellFormat61);
            cellFormats1.Append(cellFormat62);
            cellFormats1.Append(cellFormat63);
            cellFormats1.Append(cellFormat64);
            cellFormats1.Append(cellFormat65);
            cellFormats1.Append(cellFormat66);
            cellFormats1.Append(cellFormat67);
            cellFormats1.Append(cellFormat68);
            cellFormats1.Append(cellFormat69);
            cellFormats1.Append(cellFormat70);
            cellFormats1.Append(cellFormat71);
            cellFormats1.Append(cellFormat72);
            cellFormats1.Append(cellFormat73);
            cellFormats1.Append(cellFormat74);
            cellFormats1.Append(cellFormat75);
            cellFormats1.Append(cellFormat76);
            cellFormats1.Append(cellFormat77);
            cellFormats1.Append(cellFormat78);
            cellFormats1.Append(cellFormat79);
            cellFormats1.Append(cellFormat80);
            cellFormats1.Append(cellFormat81);
            cellFormats1.Append(cellFormat82);
            cellFormats1.Append(cellFormat83);
            cellFormats1.Append(cellFormat84);
            cellFormats1.Append(cellFormat85);
            cellFormats1.Append(cellFormat86);
            cellFormats1.Append(cellFormat87);
            cellFormats1.Append(cellFormat88);
            cellFormats1.Append(cellFormat89);
            cellFormats1.Append(cellFormat90);
            cellFormats1.Append(cellFormat91);
            cellFormats1.Append(cellFormat92);
            cellFormats1.Append(cellFormat93);
            cellFormats1.Append(cellFormat94);
            cellFormats1.Append(cellFormat95);
            cellFormats1.Append(cellFormat96);
            cellFormats1.Append(cellFormat97);
            cellFormats1.Append(cellFormat98);
            cellFormats1.Append(cellFormat99);
            cellFormats1.Append(cellFormat100);
            cellFormats1.Append(cellFormat101);
            cellFormats1.Append(cellFormat102);
            cellFormats1.Append(cellFormat103);
            cellFormats1.Append(cellFormat104);
            cellFormats1.Append(cellFormat105);
            cellFormats1.Append(cellFormat106);
            cellFormats1.Append(cellFormat107);
            cellFormats1.Append(cellFormat108);
            cellFormats1.Append(cellFormat109);
            cellFormats1.Append(cellFormat110);
            cellFormats1.Append(cellFormat111);
            cellFormats1.Append(cellFormat112);

            CellStyles cellStyles1 = new CellStyles() { Count = (UInt32Value)1U };
            CellStyle cellStyle1 = new CellStyle() { Name = "Normale", FormatId = (UInt32Value)0U, BuiltinId = (UInt32Value)0U };

            cellStyles1.Append(cellStyle1);
            DifferentialFormats differentialFormats1 = new DifferentialFormats() { Count = (UInt32Value)0U };
            TableStyles tableStyles1 = new TableStyles() { Count = (UInt32Value)0U, DefaultTableStyle = "TableStyleMedium2", DefaultPivotStyle = "PivotStyleLight16" };

            StylesheetExtensionList stylesheetExtensionList1 = new StylesheetExtensionList();

            StylesheetExtension stylesheetExtension1 = new StylesheetExtension() { Uri = "{EB79DEF2-80B8-43e5-95BD-54CBDDF9020C}" };
            stylesheetExtension1.AddNamespaceDeclaration("x14", "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main");
            X14.SlicerStyles slicerStyles1 = new X14.SlicerStyles() { DefaultSlicerStyle = "SlicerStyleLight1" };

            stylesheetExtension1.Append(slicerStyles1);

            StylesheetExtension stylesheetExtension2 = new StylesheetExtension() { Uri = "{9260A510-F301-46a8-8635-F512D64BE5F5}" };
            stylesheetExtension2.AddNamespaceDeclaration("x15", "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main");
            X15.TimelineStyles timelineStyles1 = new X15.TimelineStyles() { DefaultTimelineStyle = "TimeSlicerStyleLight1" };

            stylesheetExtension2.Append(timelineStyles1);

            stylesheetExtensionList1.Append(stylesheetExtension1);
            stylesheetExtensionList1.Append(stylesheetExtension2);

            stylesheet1.Append(numberingFormats1);
            stylesheet1.Append(fonts1);
            stylesheet1.Append(fills1);
            stylesheet1.Append(borders1);
            stylesheet1.Append(cellStyleFormats1);
            stylesheet1.Append(cellFormats1);
            stylesheet1.Append(cellStyles1);
            stylesheet1.Append(differentialFormats1);
            stylesheet1.Append(tableStyles1);
            stylesheet1.Append(stylesheetExtensionList1);

            workbookStylesPart.Stylesheet = stylesheet1;
        }

        // Generates content of themePart1.
        private static void GenerateThemePartContent(ThemePart themePart1)
        {
            A.Theme theme1 = new A.Theme() { Name = "Tema di Office" };
            theme1.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");

            A.ThemeElements themeElements1 = new A.ThemeElements();

            A.ColorScheme colorScheme1 = new A.ColorScheme() { Name = "Office" };

            A.Dark1Color dark1Color1 = new A.Dark1Color();
            A.SystemColor systemColor1 = new A.SystemColor() { Val = A.SystemColorValues.WindowText, LastColor = "000000" };

            dark1Color1.Append(systemColor1);

            A.Light1Color light1Color1 = new A.Light1Color();
            A.SystemColor systemColor2 = new A.SystemColor() { Val = A.SystemColorValues.Window, LastColor = "FFFFFF" };

            light1Color1.Append(systemColor2);

            A.Dark2Color dark2Color1 = new A.Dark2Color();
            A.RgbColorModelHex rgbColorModelHex1 = new A.RgbColorModelHex() { Val = "44546A" };

            dark2Color1.Append(rgbColorModelHex1);

            A.Light2Color light2Color1 = new A.Light2Color();
            A.RgbColorModelHex rgbColorModelHex2 = new A.RgbColorModelHex() { Val = "E7E6E6" };

            light2Color1.Append(rgbColorModelHex2);

            A.Accent1Color accent1Color1 = new A.Accent1Color();
            A.RgbColorModelHex rgbColorModelHex3 = new A.RgbColorModelHex() { Val = "5B9BD5" };

            accent1Color1.Append(rgbColorModelHex3);

            A.Accent2Color accent2Color1 = new A.Accent2Color();
            A.RgbColorModelHex rgbColorModelHex4 = new A.RgbColorModelHex() { Val = "ED7D31" };

            accent2Color1.Append(rgbColorModelHex4);

            A.Accent3Color accent3Color1 = new A.Accent3Color();
            A.RgbColorModelHex rgbColorModelHex5 = new A.RgbColorModelHex() { Val = "A5A5A5" };

            accent3Color1.Append(rgbColorModelHex5);

            A.Accent4Color accent4Color1 = new A.Accent4Color();
            A.RgbColorModelHex rgbColorModelHex6 = new A.RgbColorModelHex() { Val = "FFC000" };

            accent4Color1.Append(rgbColorModelHex6);

            A.Accent5Color accent5Color1 = new A.Accent5Color();
            A.RgbColorModelHex rgbColorModelHex7 = new A.RgbColorModelHex() { Val = "4472C4" };

            accent5Color1.Append(rgbColorModelHex7);

            A.Accent6Color accent6Color1 = new A.Accent6Color();
            A.RgbColorModelHex rgbColorModelHex8 = new A.RgbColorModelHex() { Val = "70AD47" };

            accent6Color1.Append(rgbColorModelHex8);

            A.Hyperlink hyperlink1 = new A.Hyperlink();
            A.RgbColorModelHex rgbColorModelHex9 = new A.RgbColorModelHex() { Val = "0563C1" };

            hyperlink1.Append(rgbColorModelHex9);

            A.FollowedHyperlinkColor followedHyperlinkColor1 = new A.FollowedHyperlinkColor();
            A.RgbColorModelHex rgbColorModelHex10 = new A.RgbColorModelHex() { Val = "954F72" };

            followedHyperlinkColor1.Append(rgbColorModelHex10);

            colorScheme1.Append(dark1Color1);
            colorScheme1.Append(light1Color1);
            colorScheme1.Append(dark2Color1);
            colorScheme1.Append(light2Color1);
            colorScheme1.Append(accent1Color1);
            colorScheme1.Append(accent2Color1);
            colorScheme1.Append(accent3Color1);
            colorScheme1.Append(accent4Color1);
            colorScheme1.Append(accent5Color1);
            colorScheme1.Append(accent6Color1);
            colorScheme1.Append(hyperlink1);
            colorScheme1.Append(followedHyperlinkColor1);

            A.FontScheme fontScheme8 = new A.FontScheme() { Name = "Office" };

            A.MajorFont majorFont1 = new A.MajorFont();
            A.LatinFont latinFont1 = new A.LatinFont() { Typeface = "Calibri Light", Panose = "020F0302020204030204" };
            A.EastAsianFont eastAsianFont1 = new A.EastAsianFont() { Typeface = "" };
            A.ComplexScriptFont complexScriptFont1 = new A.ComplexScriptFont() { Typeface = "" };
            A.SupplementalFont supplementalFont1 = new A.SupplementalFont() { Script = "Jpan", Typeface = "ＭＳ Ｐゴシック" };
            A.SupplementalFont supplementalFont2 = new A.SupplementalFont() { Script = "Hang", Typeface = "맑은 고딕" };
            A.SupplementalFont supplementalFont3 = new A.SupplementalFont() { Script = "Hans", Typeface = "宋体" };
            A.SupplementalFont supplementalFont4 = new A.SupplementalFont() { Script = "Hant", Typeface = "新細明體" };
            A.SupplementalFont supplementalFont5 = new A.SupplementalFont() { Script = "Arab", Typeface = "Times New Roman" };
            A.SupplementalFont supplementalFont6 = new A.SupplementalFont() { Script = "Hebr", Typeface = "Times New Roman" };
            A.SupplementalFont supplementalFont7 = new A.SupplementalFont() { Script = "Thai", Typeface = "Tahoma" };
            A.SupplementalFont supplementalFont8 = new A.SupplementalFont() { Script = "Ethi", Typeface = "Nyala" };
            A.SupplementalFont supplementalFont9 = new A.SupplementalFont() { Script = "Beng", Typeface = "Vrinda" };
            A.SupplementalFont supplementalFont10 = new A.SupplementalFont() { Script = "Gujr", Typeface = "Shruti" };
            A.SupplementalFont supplementalFont11 = new A.SupplementalFont() { Script = "Khmr", Typeface = "MoolBoran" };
            A.SupplementalFont supplementalFont12 = new A.SupplementalFont() { Script = "Knda", Typeface = "Tunga" };
            A.SupplementalFont supplementalFont13 = new A.SupplementalFont() { Script = "Guru", Typeface = "Raavi" };
            A.SupplementalFont supplementalFont14 = new A.SupplementalFont() { Script = "Cans", Typeface = "Euphemia" };
            A.SupplementalFont supplementalFont15 = new A.SupplementalFont() { Script = "Cher", Typeface = "Plantagenet Cherokee" };
            A.SupplementalFont supplementalFont16 = new A.SupplementalFont() { Script = "Yiii", Typeface = "Microsoft Yi Baiti" };
            A.SupplementalFont supplementalFont17 = new A.SupplementalFont() { Script = "Tibt", Typeface = "Microsoft Himalaya" };
            A.SupplementalFont supplementalFont18 = new A.SupplementalFont() { Script = "Thaa", Typeface = "MV Boli" };
            A.SupplementalFont supplementalFont19 = new A.SupplementalFont() { Script = "Deva", Typeface = "Mangal" };
            A.SupplementalFont supplementalFont20 = new A.SupplementalFont() { Script = "Telu", Typeface = "Gautami" };
            A.SupplementalFont supplementalFont21 = new A.SupplementalFont() { Script = "Taml", Typeface = "Latha" };
            A.SupplementalFont supplementalFont22 = new A.SupplementalFont() { Script = "Syrc", Typeface = "Estrangelo Edessa" };
            A.SupplementalFont supplementalFont23 = new A.SupplementalFont() { Script = "Orya", Typeface = "Kalinga" };
            A.SupplementalFont supplementalFont24 = new A.SupplementalFont() { Script = "Mlym", Typeface = "Kartika" };
            A.SupplementalFont supplementalFont25 = new A.SupplementalFont() { Script = "Laoo", Typeface = "DokChampa" };
            A.SupplementalFont supplementalFont26 = new A.SupplementalFont() { Script = "Sinh", Typeface = "Iskoola Pota" };
            A.SupplementalFont supplementalFont27 = new A.SupplementalFont() { Script = "Mong", Typeface = "Mongolian Baiti" };
            A.SupplementalFont supplementalFont28 = new A.SupplementalFont() { Script = "Viet", Typeface = "Times New Roman" };
            A.SupplementalFont supplementalFont29 = new A.SupplementalFont() { Script = "Uigh", Typeface = "Microsoft Uighur" };
            A.SupplementalFont supplementalFont30 = new A.SupplementalFont() { Script = "Geor", Typeface = "Sylfaen" };

            majorFont1.Append(latinFont1);
            majorFont1.Append(eastAsianFont1);
            majorFont1.Append(complexScriptFont1);
            majorFont1.Append(supplementalFont1);
            majorFont1.Append(supplementalFont2);
            majorFont1.Append(supplementalFont3);
            majorFont1.Append(supplementalFont4);
            majorFont1.Append(supplementalFont5);
            majorFont1.Append(supplementalFont6);
            majorFont1.Append(supplementalFont7);
            majorFont1.Append(supplementalFont8);
            majorFont1.Append(supplementalFont9);
            majorFont1.Append(supplementalFont10);
            majorFont1.Append(supplementalFont11);
            majorFont1.Append(supplementalFont12);
            majorFont1.Append(supplementalFont13);
            majorFont1.Append(supplementalFont14);
            majorFont1.Append(supplementalFont15);
            majorFont1.Append(supplementalFont16);
            majorFont1.Append(supplementalFont17);
            majorFont1.Append(supplementalFont18);
            majorFont1.Append(supplementalFont19);
            majorFont1.Append(supplementalFont20);
            majorFont1.Append(supplementalFont21);
            majorFont1.Append(supplementalFont22);
            majorFont1.Append(supplementalFont23);
            majorFont1.Append(supplementalFont24);
            majorFont1.Append(supplementalFont25);
            majorFont1.Append(supplementalFont26);
            majorFont1.Append(supplementalFont27);
            majorFont1.Append(supplementalFont28);
            majorFont1.Append(supplementalFont29);
            majorFont1.Append(supplementalFont30);

            A.MinorFont minorFont1 = new A.MinorFont();
            A.LatinFont latinFont2 = new A.LatinFont() { Typeface = "Calibri", Panose = "020F0502020204030204" };
            A.EastAsianFont eastAsianFont2 = new A.EastAsianFont() { Typeface = "" };
            A.ComplexScriptFont complexScriptFont2 = new A.ComplexScriptFont() { Typeface = "" };
            A.SupplementalFont supplementalFont31 = new A.SupplementalFont() { Script = "Jpan", Typeface = "ＭＳ Ｐゴシック" };
            A.SupplementalFont supplementalFont32 = new A.SupplementalFont() { Script = "Hang", Typeface = "맑은 고딕" };
            A.SupplementalFont supplementalFont33 = new A.SupplementalFont() { Script = "Hans", Typeface = "宋体" };
            A.SupplementalFont supplementalFont34 = new A.SupplementalFont() { Script = "Hant", Typeface = "新細明體" };
            A.SupplementalFont supplementalFont35 = new A.SupplementalFont() { Script = "Arab", Typeface = "Arial" };
            A.SupplementalFont supplementalFont36 = new A.SupplementalFont() { Script = "Hebr", Typeface = "Arial" };
            A.SupplementalFont supplementalFont37 = new A.SupplementalFont() { Script = "Thai", Typeface = "Tahoma" };
            A.SupplementalFont supplementalFont38 = new A.SupplementalFont() { Script = "Ethi", Typeface = "Nyala" };
            A.SupplementalFont supplementalFont39 = new A.SupplementalFont() { Script = "Beng", Typeface = "Vrinda" };
            A.SupplementalFont supplementalFont40 = new A.SupplementalFont() { Script = "Gujr", Typeface = "Shruti" };
            A.SupplementalFont supplementalFont41 = new A.SupplementalFont() { Script = "Khmr", Typeface = "DaunPenh" };
            A.SupplementalFont supplementalFont42 = new A.SupplementalFont() { Script = "Knda", Typeface = "Tunga" };
            A.SupplementalFont supplementalFont43 = new A.SupplementalFont() { Script = "Guru", Typeface = "Raavi" };
            A.SupplementalFont supplementalFont44 = new A.SupplementalFont() { Script = "Cans", Typeface = "Euphemia" };
            A.SupplementalFont supplementalFont45 = new A.SupplementalFont() { Script = "Cher", Typeface = "Plantagenet Cherokee" };
            A.SupplementalFont supplementalFont46 = new A.SupplementalFont() { Script = "Yiii", Typeface = "Microsoft Yi Baiti" };
            A.SupplementalFont supplementalFont47 = new A.SupplementalFont() { Script = "Tibt", Typeface = "Microsoft Himalaya" };
            A.SupplementalFont supplementalFont48 = new A.SupplementalFont() { Script = "Thaa", Typeface = "MV Boli" };
            A.SupplementalFont supplementalFont49 = new A.SupplementalFont() { Script = "Deva", Typeface = "Mangal" };
            A.SupplementalFont supplementalFont50 = new A.SupplementalFont() { Script = "Telu", Typeface = "Gautami" };
            A.SupplementalFont supplementalFont51 = new A.SupplementalFont() { Script = "Taml", Typeface = "Latha" };
            A.SupplementalFont supplementalFont52 = new A.SupplementalFont() { Script = "Syrc", Typeface = "Estrangelo Edessa" };
            A.SupplementalFont supplementalFont53 = new A.SupplementalFont() { Script = "Orya", Typeface = "Kalinga" };
            A.SupplementalFont supplementalFont54 = new A.SupplementalFont() { Script = "Mlym", Typeface = "Kartika" };
            A.SupplementalFont supplementalFont55 = new A.SupplementalFont() { Script = "Laoo", Typeface = "DokChampa" };
            A.SupplementalFont supplementalFont56 = new A.SupplementalFont() { Script = "Sinh", Typeface = "Iskoola Pota" };
            A.SupplementalFont supplementalFont57 = new A.SupplementalFont() { Script = "Mong", Typeface = "Mongolian Baiti" };
            A.SupplementalFont supplementalFont58 = new A.SupplementalFont() { Script = "Viet", Typeface = "Arial" };
            A.SupplementalFont supplementalFont59 = new A.SupplementalFont() { Script = "Uigh", Typeface = "Microsoft Uighur" };
            A.SupplementalFont supplementalFont60 = new A.SupplementalFont() { Script = "Geor", Typeface = "Sylfaen" };

            minorFont1.Append(latinFont2);
            minorFont1.Append(eastAsianFont2);
            minorFont1.Append(complexScriptFont2);
            minorFont1.Append(supplementalFont31);
            minorFont1.Append(supplementalFont32);
            minorFont1.Append(supplementalFont33);
            minorFont1.Append(supplementalFont34);
            minorFont1.Append(supplementalFont35);
            minorFont1.Append(supplementalFont36);
            minorFont1.Append(supplementalFont37);
            minorFont1.Append(supplementalFont38);
            minorFont1.Append(supplementalFont39);
            minorFont1.Append(supplementalFont40);
            minorFont1.Append(supplementalFont41);
            minorFont1.Append(supplementalFont42);
            minorFont1.Append(supplementalFont43);
            minorFont1.Append(supplementalFont44);
            minorFont1.Append(supplementalFont45);
            minorFont1.Append(supplementalFont46);
            minorFont1.Append(supplementalFont47);
            minorFont1.Append(supplementalFont48);
            minorFont1.Append(supplementalFont49);
            minorFont1.Append(supplementalFont50);
            minorFont1.Append(supplementalFont51);
            minorFont1.Append(supplementalFont52);
            minorFont1.Append(supplementalFont53);
            minorFont1.Append(supplementalFont54);
            minorFont1.Append(supplementalFont55);
            minorFont1.Append(supplementalFont56);
            minorFont1.Append(supplementalFont57);
            minorFont1.Append(supplementalFont58);
            minorFont1.Append(supplementalFont59);
            minorFont1.Append(supplementalFont60);

            fontScheme8.Append(majorFont1);
            fontScheme8.Append(minorFont1);

            A.FormatScheme formatScheme1 = new A.FormatScheme() { Name = "Office" };

            A.FillStyleList fillStyleList1 = new A.FillStyleList();

            A.SolidFill solidFill1 = new A.SolidFill();
            A.SchemeColor schemeColor1 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };

            solidFill1.Append(schemeColor1);

            A.GradientFill gradientFill1 = new A.GradientFill() { RotateWithShape = true };

            A.GradientStopList gradientStopList1 = new A.GradientStopList();

            A.GradientStop gradientStop1 = new A.GradientStop() { Position = 0 };

            A.SchemeColor schemeColor2 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.LuminanceModulation luminanceModulation1 = new A.LuminanceModulation() { Val = 110000 };
            A.SaturationModulation saturationModulation1 = new A.SaturationModulation() { Val = 105000 };
            A.Tint tint1 = new A.Tint() { Val = 67000 };

            schemeColor2.Append(luminanceModulation1);
            schemeColor2.Append(saturationModulation1);
            schemeColor2.Append(tint1);

            gradientStop1.Append(schemeColor2);

            A.GradientStop gradientStop2 = new A.GradientStop() { Position = 50000 };

            A.SchemeColor schemeColor3 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.LuminanceModulation luminanceModulation2 = new A.LuminanceModulation() { Val = 105000 };
            A.SaturationModulation saturationModulation2 = new A.SaturationModulation() { Val = 103000 };
            A.Tint tint2 = new A.Tint() { Val = 73000 };

            schemeColor3.Append(luminanceModulation2);
            schemeColor3.Append(saturationModulation2);
            schemeColor3.Append(tint2);

            gradientStop2.Append(schemeColor3);

            A.GradientStop gradientStop3 = new A.GradientStop() { Position = 100000 };

            A.SchemeColor schemeColor4 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.LuminanceModulation luminanceModulation3 = new A.LuminanceModulation() { Val = 105000 };
            A.SaturationModulation saturationModulation3 = new A.SaturationModulation() { Val = 109000 };
            A.Tint tint3 = new A.Tint() { Val = 81000 };

            schemeColor4.Append(luminanceModulation3);
            schemeColor4.Append(saturationModulation3);
            schemeColor4.Append(tint3);

            gradientStop3.Append(schemeColor4);

            gradientStopList1.Append(gradientStop1);
            gradientStopList1.Append(gradientStop2);
            gradientStopList1.Append(gradientStop3);
            A.LinearGradientFill linearGradientFill1 = new A.LinearGradientFill() { Angle = 5400000, Scaled = false };

            gradientFill1.Append(gradientStopList1);
            gradientFill1.Append(linearGradientFill1);

            A.GradientFill gradientFill2 = new A.GradientFill() { RotateWithShape = true };

            A.GradientStopList gradientStopList2 = new A.GradientStopList();

            A.GradientStop gradientStop4 = new A.GradientStop() { Position = 0 };

            A.SchemeColor schemeColor5 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.SaturationModulation saturationModulation4 = new A.SaturationModulation() { Val = 103000 };
            A.LuminanceModulation luminanceModulation4 = new A.LuminanceModulation() { Val = 102000 };
            A.Tint tint4 = new A.Tint() { Val = 94000 };

            schemeColor5.Append(saturationModulation4);
            schemeColor5.Append(luminanceModulation4);
            schemeColor5.Append(tint4);

            gradientStop4.Append(schemeColor5);

            A.GradientStop gradientStop5 = new A.GradientStop() { Position = 50000 };

            A.SchemeColor schemeColor6 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.SaturationModulation saturationModulation5 = new A.SaturationModulation() { Val = 110000 };
            A.LuminanceModulation luminanceModulation5 = new A.LuminanceModulation() { Val = 100000 };
            A.Shade shade1 = new A.Shade() { Val = 100000 };

            schemeColor6.Append(saturationModulation5);
            schemeColor6.Append(luminanceModulation5);
            schemeColor6.Append(shade1);

            gradientStop5.Append(schemeColor6);

            A.GradientStop gradientStop6 = new A.GradientStop() { Position = 100000 };

            A.SchemeColor schemeColor7 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.LuminanceModulation luminanceModulation6 = new A.LuminanceModulation() { Val = 99000 };
            A.SaturationModulation saturationModulation6 = new A.SaturationModulation() { Val = 120000 };
            A.Shade shade2 = new A.Shade() { Val = 78000 };

            schemeColor7.Append(luminanceModulation6);
            schemeColor7.Append(saturationModulation6);
            schemeColor7.Append(shade2);

            gradientStop6.Append(schemeColor7);

            gradientStopList2.Append(gradientStop4);
            gradientStopList2.Append(gradientStop5);
            gradientStopList2.Append(gradientStop6);
            A.LinearGradientFill linearGradientFill2 = new A.LinearGradientFill() { Angle = 5400000, Scaled = false };

            gradientFill2.Append(gradientStopList2);
            gradientFill2.Append(linearGradientFill2);

            fillStyleList1.Append(solidFill1);
            fillStyleList1.Append(gradientFill1);
            fillStyleList1.Append(gradientFill2);

            A.LineStyleList lineStyleList1 = new A.LineStyleList();

            A.Outline outline1 = new A.Outline() { Width = 6350, CapType = A.LineCapValues.Flat, CompoundLineType = A.CompoundLineValues.Single, Alignment = A.PenAlignmentValues.Center };

            A.SolidFill solidFill2 = new A.SolidFill();
            A.SchemeColor schemeColor8 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };

            solidFill2.Append(schemeColor8);
            A.PresetDash presetDash1 = new A.PresetDash() { Val = A.PresetLineDashValues.Solid };
            A.Miter miter1 = new A.Miter() { Limit = 800000 };

            outline1.Append(solidFill2);
            outline1.Append(presetDash1);
            outline1.Append(miter1);

            A.Outline outline2 = new A.Outline() { Width = 12700, CapType = A.LineCapValues.Flat, CompoundLineType = A.CompoundLineValues.Single, Alignment = A.PenAlignmentValues.Center };

            A.SolidFill solidFill3 = new A.SolidFill();
            A.SchemeColor schemeColor9 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };

            solidFill3.Append(schemeColor9);
            A.PresetDash presetDash2 = new A.PresetDash() { Val = A.PresetLineDashValues.Solid };
            A.Miter miter2 = new A.Miter() { Limit = 800000 };

            outline2.Append(solidFill3);
            outline2.Append(presetDash2);
            outline2.Append(miter2);

            A.Outline outline3 = new A.Outline() { Width = 19050, CapType = A.LineCapValues.Flat, CompoundLineType = A.CompoundLineValues.Single, Alignment = A.PenAlignmentValues.Center };

            A.SolidFill solidFill4 = new A.SolidFill();
            A.SchemeColor schemeColor10 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };

            solidFill4.Append(schemeColor10);
            A.PresetDash presetDash3 = new A.PresetDash() { Val = A.PresetLineDashValues.Solid };
            A.Miter miter3 = new A.Miter() { Limit = 800000 };

            outline3.Append(solidFill4);
            outline3.Append(presetDash3);
            outline3.Append(miter3);

            lineStyleList1.Append(outline1);
            lineStyleList1.Append(outline2);
            lineStyleList1.Append(outline3);

            A.EffectStyleList effectStyleList1 = new A.EffectStyleList();

            A.EffectStyle effectStyle1 = new A.EffectStyle();
            A.EffectList effectList1 = new A.EffectList();

            effectStyle1.Append(effectList1);

            A.EffectStyle effectStyle2 = new A.EffectStyle();
            A.EffectList effectList2 = new A.EffectList();

            effectStyle2.Append(effectList2);

            A.EffectStyle effectStyle3 = new A.EffectStyle();

            A.EffectList effectList3 = new A.EffectList();

            A.OuterShadow outerShadow1 = new A.OuterShadow() { BlurRadius = 57150L, Distance = 19050L, Direction = 5400000, Alignment = A.RectangleAlignmentValues.Center, RotateWithShape = false };

            A.RgbColorModelHex rgbColorModelHex11 = new A.RgbColorModelHex() { Val = "000000" };
            A.Alpha alpha1 = new A.Alpha() { Val = 63000 };

            rgbColorModelHex11.Append(alpha1);

            outerShadow1.Append(rgbColorModelHex11);

            effectList3.Append(outerShadow1);

            effectStyle3.Append(effectList3);

            effectStyleList1.Append(effectStyle1);
            effectStyleList1.Append(effectStyle2);
            effectStyleList1.Append(effectStyle3);

            A.BackgroundFillStyleList backgroundFillStyleList1 = new A.BackgroundFillStyleList();

            A.SolidFill solidFill5 = new A.SolidFill();
            A.SchemeColor schemeColor11 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };

            solidFill5.Append(schemeColor11);

            A.SolidFill solidFill6 = new A.SolidFill();

            A.SchemeColor schemeColor12 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.Tint tint5 = new A.Tint() { Val = 95000 };
            A.SaturationModulation saturationModulation7 = new A.SaturationModulation() { Val = 170000 };

            schemeColor12.Append(tint5);
            schemeColor12.Append(saturationModulation7);

            solidFill6.Append(schemeColor12);

            A.GradientFill gradientFill3 = new A.GradientFill() { RotateWithShape = true };

            A.GradientStopList gradientStopList3 = new A.GradientStopList();

            A.GradientStop gradientStop7 = new A.GradientStop() { Position = 0 };

            A.SchemeColor schemeColor13 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.Tint tint6 = new A.Tint() { Val = 93000 };
            A.SaturationModulation saturationModulation8 = new A.SaturationModulation() { Val = 150000 };
            A.Shade shade3 = new A.Shade() { Val = 98000 };
            A.LuminanceModulation luminanceModulation7 = new A.LuminanceModulation() { Val = 102000 };

            schemeColor13.Append(tint6);
            schemeColor13.Append(saturationModulation8);
            schemeColor13.Append(shade3);
            schemeColor13.Append(luminanceModulation7);

            gradientStop7.Append(schemeColor13);

            A.GradientStop gradientStop8 = new A.GradientStop() { Position = 50000 };

            A.SchemeColor schemeColor14 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.Tint tint7 = new A.Tint() { Val = 98000 };
            A.SaturationModulation saturationModulation9 = new A.SaturationModulation() { Val = 130000 };
            A.Shade shade4 = new A.Shade() { Val = 90000 };
            A.LuminanceModulation luminanceModulation8 = new A.LuminanceModulation() { Val = 103000 };

            schemeColor14.Append(tint7);
            schemeColor14.Append(saturationModulation9);
            schemeColor14.Append(shade4);
            schemeColor14.Append(luminanceModulation8);

            gradientStop8.Append(schemeColor14);

            A.GradientStop gradientStop9 = new A.GradientStop() { Position = 100000 };

            A.SchemeColor schemeColor15 = new A.SchemeColor() { Val = A.SchemeColorValues.PhColor };
            A.Shade shade5 = new A.Shade() { Val = 63000 };
            A.SaturationModulation saturationModulation10 = new A.SaturationModulation() { Val = 120000 };

            schemeColor15.Append(shade5);
            schemeColor15.Append(saturationModulation10);

            gradientStop9.Append(schemeColor15);

            gradientStopList3.Append(gradientStop7);
            gradientStopList3.Append(gradientStop8);
            gradientStopList3.Append(gradientStop9);
            A.LinearGradientFill linearGradientFill3 = new A.LinearGradientFill() { Angle = 5400000, Scaled = false };

            gradientFill3.Append(gradientStopList3);
            gradientFill3.Append(linearGradientFill3);

            backgroundFillStyleList1.Append(solidFill5);
            backgroundFillStyleList1.Append(solidFill6);
            backgroundFillStyleList1.Append(gradientFill3);

            formatScheme1.Append(fillStyleList1);
            formatScheme1.Append(lineStyleList1);
            formatScheme1.Append(effectStyleList1);
            formatScheme1.Append(backgroundFillStyleList1);

            themeElements1.Append(colorScheme1);
            themeElements1.Append(fontScheme8);
            themeElements1.Append(formatScheme1);
            A.ObjectDefaults objectDefaults1 = new A.ObjectDefaults();
            A.ExtraColorSchemeList extraColorSchemeList1 = new A.ExtraColorSchemeList();

            A.OfficeStyleSheetExtensionList officeStyleSheetExtensionList1 = new A.OfficeStyleSheetExtensionList();

            A.OfficeStyleSheetExtension officeStyleSheetExtension1 = new A.OfficeStyleSheetExtension() { Uri = "{05A4C25C-085E-4340-85A3-A5531E510DB2}" };

            Thm15.ThemeFamily themeFamily1 = new Thm15.ThemeFamily() { Name = "Office Theme", Id = "{62F939B6-93AF-4DB8-9C6B-D6C7DFDC589F}", Vid = "{4A3C46E8-61CC-4603-A589-7422A47A8E4A}" };
            themeFamily1.AddNamespaceDeclaration("thm15", "http://schemas.microsoft.com/office/thememl/2012/main");

            officeStyleSheetExtension1.Append(themeFamily1);

            officeStyleSheetExtensionList1.Append(officeStyleSheetExtension1);

            theme1.Append(themeElements1);
            theme1.Append(objectDefaults1);
            theme1.Append(extraColorSchemeList1);
            theme1.Append(officeStyleSheetExtensionList1);

            themePart1.Theme = theme1;
        }

        private static void SetPackageProperties(OpenXmlPackage document)
        {
            document.PackageProperties.Creator = "Teamwork Report Service";
            document.PackageProperties.Created = DateTime.Now;
        }
        #endregion create sheet

        #region style
        public class CellStyled
        {
            public object value { get; set; }
            public StyleEnum style { get; set; }

            public CellStyled(object _value, StyleEnum _style)
            {
                value = _value;
                style = _style;
            }

            public CellStyled(object _value)
            {
                value = _value;
                style = StyleEnum.Normale;
            }
        }
        /// <summary>
        /// Enum che tiene traccia degli stili definiti nei metodi della regione Create Sheet
        /// </summary>
        public enum StyleEnum
        {
            Normale = 0,
            Titolo = 4,
            RiepilogoNormale = 2,
            RiepilogoPercentuale = 14,
            RiepilogoMeseAnno = 12,
            HeaderTabellaNormale = 7,
            HeaderTabellaData = 8,
            DatiTabellaNormale = 1,
            DatiTabellaPercentuale = 3,
            DatiTabellaNumero = 9,
            DatiTabellaDataBreve = 10,
            DatiTabellaOra = 11,
            FooterTabellaNormale = 5,
            FooterTabellaPercentuale = 6
        }
        #endregion style
    }
}
