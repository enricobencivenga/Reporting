using System;
using System.Collections.Generic;
using Reporting.Utilities;

namespace Reporting.Models
{
    public class Product
    {
        [Excel(ColumnName = "Id")]
        public int Id { get; set; }
        [Excel(ColumnName = "Ean")]
        public string Ean { get; set; }
        [Excel(ColumnName = "Name")]
        public string Name { get; set; }
        [Excel(ColumnName = "Description")]
        public string Description { get; set; }
        [Excel(ColumnName = "Brand")]
        public string Brand { get; set; }
        [Excel(ColumnName = "Category")]
        public string Category { get; set; }
        [Excel(ColumnName = "Price", IsCurrency = true)]
        public string Price { get; set; }
        [Excel(ColumnName = "Quantity")]
        public int Quantity { get; set; }
        [Excel(ColumnName = "Rating")]
        public float Rating { get; set; }
        [Excel(ColumnName = "ReleaseDate")]
        public DateTime ReleaseDate { get; set; }


    }

}