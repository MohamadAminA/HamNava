using System.Diagnostics;
using System.Text;
using HamNava.Models;
using Microsoft.AspNetCore.Mvc;

namespace HamNava.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult RobotsTxt()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var robots = $"User-agent: *\nAllow: /\nSitemap: {baseUrl}/sitemap.xml\n";
            return Content(robots, "text/plain", Encoding.UTF8);
        }

        public IActionResult SitemapXml()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var lastModified = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var sitemap = $"""
<?xml version="1.0" encoding="utf-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>{baseUrl}/</loc>
    <lastmod>{lastModified}</lastmod>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
</urlset>
""";

            return Content(sitemap, "application/xml", Encoding.UTF8);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
