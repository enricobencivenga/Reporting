﻿using Bogus;
using Reporting.Models;
using Reporting.Utilities;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace Reporting.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {

        // GET api/export/{count}
        [HttpGet("export/{count}")]
        public ActionResult GetExport(int count = 0)
        {
            var productFaker = new Faker<Product>()
                   .CustomInstantiator(f => new Product())
                   .RuleFor(p => p.Id, f => f.IndexFaker)
                   .RuleFor(p => p.Ean, f => f.Commerce.Ean13())
                   .RuleFor(p => p.Name, f => f.Commerce.ProductName())
                   .RuleFor(p => p.Description, f => f.Lorem.Sentence(f.Random.Int(5, 20)))
                   .RuleFor(p => p.Brand, f => f.Company.CompanyName())
                   .RuleFor(p => p.Category, f => f.Commerce.Categories(1).First())
                   .RuleFor(p => p.Price, f => f.Commerce.Price(1, 1000, 2, "€"))
                   .RuleFor(p => p.Quantity, f => f.Random.Int(0, 1000))
                   .RuleFor(p => p.Rating, f => f.Random.Float(0, 1))
                   .RuleFor(p => p.ReleaseDate, f => f.Date.Past(2));

            var excelProvider = new ExcelProvider();
            excelProvider.Generate(productFaker.Generate(count), null, "Sheet 1");

            return File(excelProvider.File, "application/octet-stream");
        }

    }
}
