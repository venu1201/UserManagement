using System.Security.Claims;
using BackendApi.Data;
using BackendApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendAPI.Controllers;

[Route("api/[controller]")]
public class ProjectController : BaseController<Project>
{
    public ProjectController(ApplicationDbContext context) :
    base(context)
    { }

    [Authorize]
    [HttpGet("[action]")]
    public async Task<IActionResult> GetMyProjects()
    {
        var userEmail = User?.FindFirst(ClaimTypes.Email)?.Value;

        var projects = await _dbContext.Projects.Where(item=>item.CreatedBy.ToLower() == userEmail.ToLower()).ToListAsync();

        return Ok(projects);
    }

}


