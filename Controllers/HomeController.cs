using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Controllers;

[Route("")] 
[ApiController]
public class HomeController : ControllerBase
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    public IActionResult Index()
    {
        var userName = User.Identity?.Name ?? "Unknown User";
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "No Email";
        var message = $"Hello {userName}, Welcome to BackendApi (Email: {email})";

        _logger.LogInformation(message);
        return Ok(new { Message = message });
    }
}