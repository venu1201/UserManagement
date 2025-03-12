using BackendApi.Data;
using BackendApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace BackendAPI.Controllers;

[Route("api/[controller]")]
public class ProjectController : BaseController<Project>
{
    public ProjectController(ApplicationDbContext context) : 
    base(context) { }

}


