using System.Security.Claims;
using BackendApi.Data;
using BackendApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendAPI.Controllers;

[ApiController]
public abstract class BaseController<T> : ControllerBase where T : BaseEntity
{
    protected readonly ApplicationDbContext _dbContext;
    protected readonly DbSet<T> _dbSet;
    protected BaseController(ApplicationDbContext context)
    {
        _dbContext = context;
        _dbSet = _dbContext.Set<T>();
    }
    protected virtual IQueryable<T> ApplyIncludes(IQueryable<T> query)
    {
        return query;
    }

    [NonAction]
    public virtual async Task<IEnumerable<T>> GetEntitiesAsync(Func<T, bool> predicate, bool includeHistory = false)
    {
        var dbSet = includeHistory ? _dbSet.TemporalAll().AsNoTracking() : ApplyIncludes(_dbSet);
        var entities = await Task.Run(() =>
            dbSet
            .Where(predicate)
            .ToList());
        return entities.OrderByDescending(item => item.CreatedOn);

    }
    [NonAction]
    public virtual async Task<T?> GetEntityAsync(Func<T, bool> predicate)
    {
        var entity = await Task.Run(() =>
            ApplyIncludes(_dbSet)
            .FirstOrDefault(predicate));

        return entity;
    }

    [HttpGet("[action]")]
    public virtual async Task<IActionResult> GetAll()
    {
        IEnumerable<T> entities;
        entities = await GetEntitiesAsync(item => true);
        return Ok(entities);
    }

    [HttpGet("[action]/{id:int}")]
    public virtual async Task<IActionResult> GetById([FromRoute] int id)
    {
        var entity = await GetEntityAsync(item => item.Id == id);

        if (entity == null)
            return NotFound(new
            {
                message = "The item you are trying to retrieve was not found."
            });

        return Ok(entity);
    }

    [HttpPost("[action]")]
    public virtual async Task<IActionResult> Create([FromBody] T entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                message = "The information provided is not valid. Please check your input and try again.",
                errors = ModelState
            });
        }

        var existingEntity = entity.Id > 0
            ? await GetEntityAsync(item => item.Id == entity.Id)
            : null;
        var userEmail = User?.FindFirst(ClaimTypes.Email)?.Value;

        if (existingEntity == null)
        {
            entity.CreatedBy = userEmail ?? "Unknown";
            entity.CreatedOn = DateTime.UtcNow;

            await _dbSet.AddAsync(entity);
        }
        else
        {
            _dbContext.Entry(existingEntity).CurrentValues.SetValues(entity);

            existingEntity.ModifiedBy = userEmail ?? "Unknown";
            existingEntity.ModifiedOn = DateTime.UtcNow;

            _dbSet.Update(existingEntity);
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (entity.Id > 0 && !_dbSet.Any(e => e.Id == entity.Id))
                return NotFound(new
                {
                    message = "The item you are trying to update was not found. Please check your input and try again."
                });
            else
                throw;
        }

        var result = existingEntity ?? entity;
        return Ok(result);
    }

    [NonAction]
    public virtual async Task<IActionResult> RemoveHelper(int id, bool isActive)
    {
        var entity = await GetEntityAsync(item => item.Id == id && item.IsActive == !isActive);
        if (entity == null)
        {
            return NotFound(new
            {
                message = $"The item you are trying to update was not found."
            });
        }

        entity.ModifiedBy = User?.Identity?.Name ?? "Unknown";
        entity.ModifiedOn = DateTime.UtcNow;
        entity.IsActive = isActive;
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = $"The item has been successfully {(isActive ? "restored" : "deleted")}.",
            DeletedId = id
        });
    }

    [HttpPut("[action]/{id:int}")]
    public virtual async Task<IActionResult> Remove([FromRoute] int id, [FromQuery] bool restore = false) => await RemoveHelper(id, restore);

}

