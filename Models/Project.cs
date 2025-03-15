using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models;

public class Project : BaseEntity
{
    [MaxLength(100)]
    public required string ProjectName { get; set; }

    [MaxLength(1000)]
    public required string ProjectDescription { get; set; }

    [MaxLength(500)] 
    public string? Icon { get; set; }

    [MaxLength(500)] 
    public required string Path { get; set; }
    public bool IsExternalProject { get; set; } = true;
    [MaxLength(20)] 
    public string Status {get;set;} = "InProgress";
    [MaxLength(1000)] 
    public string? Tags {get;set;} 
}
