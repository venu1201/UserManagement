using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models;

public class Tournament : BaseEntity
{
    [MaxLength(100)]
    public required string TournamentName { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(20)] 
    public string Status {get;set;} = "InProgress";

    [MaxLength(1000)]
    public string? TournamentPlayers {get;set;}

    public int NumberOfQualifications {get;set;}

    public List<Match> Matches { get; set; } = new List<Match>();

}
