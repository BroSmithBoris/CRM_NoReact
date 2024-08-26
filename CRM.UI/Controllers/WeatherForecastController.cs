using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CRM.UI.Controllers;

public class WeatherForecastController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public WeatherForecastController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }
    
    public IActionResult Index()
    {
        return View();
    }
}