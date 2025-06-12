namespace BackendApi.Models;
public class DashboardData
{
    public TournamentSummary Tournament { get; set; }
    public List<TeamStanding> TeamStandings { get; set; } = new();
    public List<TopBatsman> TopBatsmen { get; set; } = new();
    public List<TopBowler> TopBowlers { get; set; } = new();
    public List<Match> UpcomingMatches { get; set; } = new();
    public List<Match> CompletedMatches { get; set; } = new();
    public List<Match> Schedule { get; set; } = new();
    public List<QualificationChance> QualificationChances { get; set; } = new();
    public TournamentHighlights Highlights { get; set; }
}

public class TournamentSummary
{
    public string Name { get; set; }
    public string Status { get; set; }
    public int TotalMatches { get; set; }
    public int CompletedMatches { get; set; }
    public int RemainingMatches { get; set; }
}

public class TeamStanding
{
    public int Rank { get; set; }
    public string Team { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Points { get; set; }
    public string NetRunRate { get; set; } = string.Empty;
    public bool IsQualified { get; set; }
}

public class TopBatsman
{
    public int Rank { get; set; }
    public string Name { get; set; }
    public int Runs { get; set; }
    public int Matches { get; set; }
    public double Average { get; set; }
    public double StrikeRate { get; set; }
}

public class TopBowler
{
    public int Rank { get; set; }
    public string Name { get; set; }
    public int Wickets { get; set; }
    public int Matches { get; set; }
    public double Economy { get; set; }
    public double Average { get; set; }
}

public class QualificationChance
{
    public string Team { get; set; }
    public int Top2Chance { get; set; }
    public int Top4Chance { get; set; }
    public string Status { get; set; }
}

public class TournamentHighlights
{
    public int HighestIndividualScore { get; set; }
    public int MostWickets { get; set; }
    public int HighestTeamScore { get; set; }
    public double BestStrikeRate { get; set; }
}