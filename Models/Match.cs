using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models;

public class Match : BaseEntity
{
    public int TournamentId { get; set; }
    public int MatchNumber { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500)] 
    public required string Player1Id { get; set; }
    [MaxLength(500)] 
    public required string Player2Id { get; set; }

    [MaxLength(10)] 
    public required string MatchType {get;set;} = "League";

    [MaxLength(500)] 
    public string? WinnerId { get; set; }

    [MaxLength(500)] 
    public string? TossWinnerId {get;set;}

    [MaxLength(10)] 
    public string? TossWinnerChoice {get;set;}

    public int Player1Score {get;set;}
    public int Player2Score {get;set;}
    public int Player1Balls {get;set;}
    public int Player2Balls {get;set;}
    public int Player1Wickets {get;set;}
    public int Player2Wickets {get;set;}
    
}
