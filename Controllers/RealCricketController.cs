using System.Security.Claims;
using BackendApi.Data;
using BackendApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendAPI.Controllers;

[Route("api/[controller]")]
public class RealCricketController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public RealCricketController(ApplicationDbContext context)
    {
        _dbContext = context;
    }

    [HttpGet("GetAll")]
    public async Task<IActionResult> GetTournaments()
    {
        var tournaments = await _dbContext.Tournament
            .Include(item => item.Matches)
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.Id)
            .ToListAsync();
        return Ok(tournaments);
    }

    [HttpGet("GetById/{id}")]
    public async Task<IActionResult> GetTournamentById(int id)
    {
        var tournament = await _dbContext.Tournament
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tournament == null)
        {
            return NotFound($"Tournament with ID {id} not found or has been deleted.");
        }

        return Ok(tournament);
    }

    [HttpGet("GetDashboardData")]
    public async Task<IActionResult> GetDashboardData(int? tournamentId)
    {
        Tournament? tournament = null;
        if (tournamentId != null)
        {
            tournament = await _dbContext.Tournament
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
        }
        else
        {
            tournament = await _dbContext.Tournament
            .Where(t => t.Status == "InProgress" && t.IsActive)
            .OrderByDescending(t => t.Id)
            .Include(t => t.Matches).FirstOrDefaultAsync()
            ;

            if (tournament == null)
            {
                tournament = await _dbContext.Tournament
                    .Where(t => t.IsActive)
                    .OrderByDescending(t => t.Id)
                    .Include(t => t.Matches)
                    .FirstOrDefaultAsync();
            }
        }

        if (tournament == null)
        {
            return NotFound("Tournament not found");
        }

        var players = tournament.TournamentPlayers?.Split(",", StringSplitOptions.RemoveEmptyEntries)
                                                   .Select(p => p.Trim())
                                                   .ToList() ?? new List<string>();

        var (teamStandings, topBatsmen, topBowlers) = GeneratePlayerStats(tournament.Matches, players, tournament.NumberOfQualifications);

        var dashboardData = new DashboardData
        {
            Tournament = new TournamentSummary
            {
                Name = tournament.TournamentName,
                Status = tournament.Status,
                TotalMatches = tournament.Matches.Count,
                CompletedMatches = tournament.Matches.Count(m => !string.IsNullOrEmpty(m.WinnerId)),
                RemainingMatches = tournament.Matches.Count(m => string.IsNullOrEmpty(m.WinnerId))
            },
            TeamStandings = teamStandings,
            TopBatsmen = topBatsmen,
            TopBowlers = topBowlers,
            Schedule = tournament.Matches,
            UpcomingMatches = tournament.Matches.Where(m => string.IsNullOrEmpty(m.WinnerId)).ToList(),
            CompletedMatches = tournament.Matches
                .Where(m => !string.IsNullOrEmpty(m.WinnerId))
                .OrderByDescending(m => m.MatchNumber)
                .ToList(),
            QualificationChances = GenerateQualificationChances(
                                        teamStandings,
                                        tournament.Matches.Where(m => m.MatchType == "League" && string.IsNullOrEmpty(m.WinnerId)).ToList(),
                                        tournament.Matches.Where(m=>m.MatchType == "League").ToList()
                                    ),
            Highlights = GenerateHighlights(tournament.Matches)
        };

        return Ok(dashboardData);
    }

    private (List<TeamStanding>, List<TopBatsman>, List<TopBowler>) GeneratePlayerStats(List<Match> matches, List<string> players, int numberOfQualifications)
    {
        var teamStandings = new List<TeamStanding>();
        var battingStats = new List<(string Name, int Runs, int Balls, int Matches, int Innings)>();
        var bowlingStats = new List<(string Name, int Wickets, int RunsConceded, int Balls, int Matches)>();

        foreach (var player in players)
        {
            var playerLeagueMatches = matches.Where(m =>
                (m.Player1Id == player || m.Player2Id == player) && m.MatchType == "League").ToList();

            var playerAllMatches = matches.Where(m =>
                m.Player1Id == player || m.Player2Id == player).ToList();

            var won = playerLeagueMatches.Count(m => m.WinnerId == player);
            var lost = playerLeagueMatches.Where(item => item.WinnerId != null).ToList().Count - won;
            var points = won * 2;

            var totalRunsScored = 0;
            var totalBallsFaced = 0;
            var totalRunsConceded = 0;
            var totalBallsBowled = 0;

            var battingRuns = 0;
            var battingBalls = 0;
            var battingInnings = 0;
            var bowlingWickets = 0;
            var bowlingRunsConceded = 0;
            var bowlingBalls = 0;

            foreach (var match in playerAllMatches)
            {
                if (match.Player1Id == player)
                {
                    battingRuns += match.Player1Score;
                    battingBalls += match.Player1Balls;
                    battingInnings++;

                    bowlingWickets += match.Player2Wickets;
                    bowlingRunsConceded += match.Player2Score;
                    bowlingBalls += match.Player2Balls;
                }
                else
                {
                    battingRuns += match.Player2Score;
                    battingBalls += match.Player2Balls;
                    battingInnings++;

                    bowlingWickets += match.Player1Wickets;
                    bowlingRunsConceded += match.Player1Score;
                    bowlingBalls += match.Player1Balls;
                }
            }

            foreach (var match in playerLeagueMatches)
            {
                if (match.Player1Id == player)
                {
                    totalRunsScored += match.Player1Score;
                    totalBallsFaced += match.Player1Balls;
                    totalRunsConceded += match.Player2Score;
                    totalBallsBowled += match.Player2Balls;
                }
                else
                {
                    totalRunsScored += match.Player2Score;
                    totalBallsFaced += match.Player2Balls;
                    totalRunsConceded += match.Player1Score;
                    totalBallsBowled += match.Player1Balls;
                }
            }

            var runRate = totalBallsFaced > 0 ? (double)totalRunsScored / totalBallsFaced * 6 : 0;
            var concededRate = totalBallsBowled > 0 ? (double)totalRunsConceded / totalBallsBowled * 6 : 0;
            var nrr = runRate - concededRate;

            teamStandings.Add(new TeamStanding
            {
                Team = player,
                Played = playerLeagueMatches.Where(item => item.WinnerId != null).ToList().Count,
                Won = won,
                Lost = lost,
                Points = points,
                NetRunRate = Math.Round(nrr, 2).ToString("+0.00;-0.00;0.00")
            });

            battingStats.Add((player, battingRuns, battingBalls, playerAllMatches.Count, battingInnings));
            bowlingStats.Add((player, bowlingWickets, bowlingRunsConceded, bowlingBalls, playerAllMatches.Count));
        }

        var rankedStandings = teamStandings
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => double.Parse(s.NetRunRate))
            .Select((s, index) => { s.Rank = index + 1; return s; })
            .ToList();

        var remainingMatches = matches.Where(m => m.MatchType == "League" && string.IsNullOrEmpty(m.WinnerId)).ToList();

        foreach (var standing in rankedStandings)
        {
            standing.IsQualified = IsTeamQualified(standing, rankedStandings, remainingMatches, numberOfQualifications, matches);
        }

        var topBatsmen = battingStats
            .Where(b => b.Innings > 0)
            .Select(b => new TopBatsman
            {
                Name = b.Name,
                Runs = b.Runs,
                Matches = b.Matches,
                Average = Math.Round((double)b.Runs / b.Innings, 1),
                StrikeRate = b.Balls > 0 ? Math.Round((double)b.Runs / b.Balls * 100, 1) : 0
            })
            .OrderByDescending(b => b.Runs)
            .Select((b, index) => { b.Rank = index + 1; return b; })
            .ToList();

        var topBowlers = bowlingStats
            .Where(b => b.Matches > 0)
            .Select(b => new TopBowler
            {
                Name = b.Name,
                Wickets = b.Wickets,
                Matches = b.Matches,
                Economy = b.Balls > 0 ? Math.Round((double)b.RunsConceded / b.Balls * 6, 1) : 0,
                Average = b.Wickets > 0 ? Math.Round((double)b.RunsConceded / b.Wickets, 1) : 0
            })
            .OrderByDescending(b => b.Wickets)
            .Select((b, index) => { b.Rank = index + 1; return b; })
            .ToList();

        return (rankedStandings, topBatsmen, topBowlers);
    }
    private bool IsTeamQualified(TeamStanding team, List<TeamStanding> allStandings, List<Match> remainingMatches, int qualificationSpots, List<Match> allMatches)
    {
        if (team.Rank > qualificationSpots)
            return false;

        var teamsBelow = allStandings.Where(t => t.Rank > qualificationSpots).ToList();

        foreach (var belowTeam in teamsBelow)
        {
            var teamRemainingMatches = remainingMatches.Where(m =>
                m.Player1Id == belowTeam.Team || m.Player2Id == belowTeam.Team).Count();

            var maxPossiblePoints = belowTeam.Points + (teamRemainingMatches * 2);

            if (maxPossiblePoints > team.Points)
            {
                return false;
            }

            if (maxPossiblePoints == team.Points)
            {
                var teamCurrentStats = GetTeamNRRStats(team.Team, allMatches);
                var belowTeamCurrentStats = GetTeamNRRStats(belowTeam.Team, allMatches);

                var bestPossibleNRR = CalculateBestPossibleNRR(belowTeam.Team, belowTeamCurrentStats, remainingMatches);
                var worstPossibleNRR = CalculateWorstPossibleNRR(team.Team, teamCurrentStats, remainingMatches);

                if (bestPossibleNRR > worstPossibleNRR)
                {
                    return false;
                }
            }
        }

        return true;
    }
    private (int RunsFor, int RunsAgainst, int BallsFaced, int BallsBowled) GetTeamNRRStats(string teamName, List<Match> allMatches)
    {
        var teamMatches = allMatches.Where(m =>
            (m.Player1Id == teamName || m.Player2Id == teamName) &&
            m.MatchType == "League" &&
            !string.IsNullOrEmpty(m.WinnerId)).ToList();

        int runsFor = 0, runsAgainst = 0, ballsFaced = 0, ballsBowled = 0;

        foreach (var match in teamMatches)
        {
            if (match.Player1Id == teamName)
            {
                runsFor += match.Player1Score;
                runsAgainst += match.Player2Score;
                ballsFaced += match.Player1Balls;
                ballsBowled += match.Player2Balls;
            }
            else
            {
                runsFor += match.Player2Score;
                runsAgainst += match.Player1Score;
                ballsFaced += match.Player2Balls;
                ballsBowled += match.Player1Balls;
            }
        }

        return (runsFor, runsAgainst, ballsFaced, ballsBowled);
    }

    private double CalculateBestPossibleNRR(string teamName, (int RunsFor, int RunsAgainst, int BallsFaced, int BallsBowled) currentStats, List<Match> remainingMatches)
    {
        var teamRemainingMatches = remainingMatches.Where(m => m.Player1Id == teamName || m.Player2Id == teamName).ToList();

        int additionalRunsFor = teamRemainingMatches.Count * 70;
        int additionalRunsAgainst = teamRemainingMatches.Count * 20;
        int additionalBallsFaced = teamRemainingMatches.Count * 30;
        int additionalBallsBowled = teamRemainingMatches.Count * 30;

        int totalRunsFor = currentStats.RunsFor + additionalRunsFor;
        int totalRunsAgainst = currentStats.RunsAgainst + additionalRunsAgainst;
        int totalBallsFaced = currentStats.BallsFaced + additionalBallsFaced;
        int totalBallsBowled = currentStats.BallsBowled + additionalBallsBowled;

        return CalculateNetRunRate(totalRunsFor, totalRunsAgainst, totalBallsFaced, totalBallsBowled);
    }

    private double CalculateWorstPossibleNRR(string teamName, (int RunsFor, int RunsAgainst, int BallsFaced, int BallsBowled) currentStats, List<Match> remainingMatches)
    {
        var teamRemainingMatches = remainingMatches.Where(m => m.Player1Id == teamName || m.Player2Id == teamName).ToList();

        int additionalRunsFor = teamRemainingMatches.Count * 20;
        int additionalRunsAgainst = teamRemainingMatches.Count * 70;
        int additionalBallsFaced = teamRemainingMatches.Count * 30;
        int additionalBallsBowled = teamRemainingMatches.Count * 30;

        int totalRunsFor = currentStats.RunsFor + additionalRunsFor;
        int totalRunsAgainst = currentStats.RunsAgainst + additionalRunsAgainst;
        int totalBallsFaced = currentStats.BallsFaced + additionalBallsFaced;
        int totalBallsBowled = currentStats.BallsBowled + additionalBallsBowled;

        return CalculateNetRunRate(totalRunsFor, totalRunsAgainst, totalBallsFaced, totalBallsBowled);
    }

    private List<QualificationChance> GenerateQualificationChances(List<TeamStanding> standings, List<Match> remainingMatches, List<Match> allMatches)
    {
        return standings.Select(standing =>
        {
            var chances = CalculateQualificationChance(standing, standings, remainingMatches, allMatches);
            return new QualificationChance
            {
                Team = standing.Team,
                Top2Chance = chances.top2,
                Top4Chance = chances.top4,
                Status = GetQualificationStatus(chances.top4)
            };
        }).ToList();
    }

    private (int top2, int top4) CalculateQualificationChance(TeamStanding targetTeam, List<TeamStanding> allStandings, List<Match> remainingMatches, List<Match> allMatches)
    {
        const int simulations = 10000;
        int top2Count = 0;
        int top4Count = 0;
        var random = new Random();

        for (int sim = 0; sim < simulations; sim++)
        {
            var simulatedStandings = allStandings.Select(s =>
            {
                var teamMatches = allMatches.Where(m => !string.IsNullOrEmpty(m.WinnerId) &&
                                                       (m.Player1Id == s.Team || m.Player2Id == s.Team)).ToList();

                int totalRunsFor = 0, totalRunsAgainst = 0, totalBallsFaced = 0, totalBallsBowled = 0;

                foreach (var match in teamMatches)
                {
                    if (match.Player1Id == s.Team)
                    {
                        totalRunsFor += match.Player1Score;
                        totalRunsAgainst += match.Player2Score;
                        totalBallsFaced += match.Player1Balls;
                        totalBallsBowled += match.Player2Balls;
                    }
                    else
                    {
                        totalRunsFor += match.Player2Score;
                        totalRunsAgainst += match.Player1Score;
                        totalBallsFaced += match.Player2Balls;
                        totalBallsBowled += match.Player1Balls;
                    }
                }

                return new
                {
                    Team = s.Team,
                    Points = s.Points,
                    RunsFor = totalRunsFor,
                    RunsAgainst = totalRunsAgainst,
                    BallsFaced = totalBallsFaced,
                    BallsBowled = totalBallsBowled
                };
            }).ToList();

            foreach (var match in remainingMatches)
            {
                var team1Index = simulatedStandings.FindIndex(s => s.Team == match.Player1Id);
                var team2Index = simulatedStandings.FindIndex(s => s.Team == match.Player2Id);

                if (team1Index >= 0 && team2Index >= 0)
                {
                    var team1Score = SimulateInningsScore(random);
                    var team2Score = SimulateInningsScore(random);
                    var team1Balls = 30;
                    var team2Balls = team1Score >= team2Score ? 30 : random.Next(6, 31);

                    if (team2Score > team1Score)
                    {
                        team2Balls = random.Next(18, 30);
                    }

                    var team1 = simulatedStandings[team1Index];
                    var team2 = simulatedStandings[team2Index];

                    int team1Points = 0, team2Points = 0;
                    if (team1Score > team2Score)
                    {
                        team1Points = 2;
                    }
                    else if (team2Score > team1Score)
                    {
                        team2Points = 2;
                    }
                    else
                    {
                        team1Points = team2Points = 1;
                    }

                    var updatedTeam1 = new
                    {
                        team1.Team,
                        Points = team1.Points + team1Points,
                        RunsFor = team1.RunsFor + team1Score,
                        RunsAgainst = team1.RunsAgainst + team2Score,
                        BallsFaced = team1.BallsFaced + team1Balls,
                        BallsBowled = team1.BallsBowled + team2Balls
                    };

                    var updatedTeam2 = new
                    {
                        team2.Team,
                        Points = team2.Points + team2Points,
                        RunsFor = team2.RunsFor + team2Score,
                        RunsAgainst = team2.RunsAgainst + team1Score,
                        BallsFaced = team2.BallsFaced + team2Balls,
                        BallsBowled = team2.BallsBowled + team1Balls
                    };

                    simulatedStandings[team1Index] = updatedTeam1;
                    simulatedStandings[team2Index] = updatedTeam2;
                }
            }

            var finalStandings = simulatedStandings
                .Select(s => new
                {
                    s.Team,
                    s.Points,
                    NetRunRate = CalculateNetRunRate(s.RunsFor, s.RunsAgainst, s.BallsFaced, s.BallsBowled)
                })
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.NetRunRate)
                .ToList();

            var targetPosition = finalStandings.FindIndex(s => s.Team == targetTeam.Team);
            if (targetPosition < 2) top2Count++;
            if (targetPosition < 4) top4Count++;
        }

        return (
            top2: (int)Math.Round((double)top2Count / simulations * 100),
            top4: (int)Math.Round((double)top4Count / simulations * 100)
        );
    }

    private int SimulateInningsScore(Random random)
    {
        var baseScore = random.Next(35, 56);
        var outlierChance = random.NextDouble();

        if (outlierChance < 0.1)
        {
            return random.Next(20, 35);
        }
        else if (outlierChance > 0.9)
        {
            return random.Next(55, 71);
        }

        return baseScore;
    }

    private double CalculateNetRunRate(int runsFor, int runsAgainst, int ballsFaced, int ballsBowled)
    {
        if (ballsFaced == 0 || ballsBowled == 0) return 0.0;

        double oversFaced = ballsFaced / 6.0;
        double oversBowled = ballsBowled / 6.0;

        double runRateFor = runsFor / oversFaced;
        double runRateAgainst = runsAgainst / oversBowled;

        return Math.Round(runRateFor - runRateAgainst, 3);
    }

    private string GetQualificationStatus(int top4Chance)
    {
        return top4Chance switch
        {
            >= 90 => "Almost Certain",
            >= 70 => "Very Likely",
            >= 50 => "Good Chance",
            >= 30 => "Possible",
            >= 15 => "Unlikely",
            _ => "Very Unlikely"
        };
    }

    private TournamentHighlights GenerateHighlights(List<Match> matches)
    {
        var completedMatches = matches.Where(m => !string.IsNullOrEmpty(m.WinnerId)).ToList();

        if (!completedMatches.Any())
        {
            return new TournamentHighlights();
        }

        var highestScore = Math.Max(
            completedMatches.Any() ? completedMatches.Max(m => m.Player1Score) : 0,
            completedMatches.Any() ? completedMatches.Max(m => m.Player2Score) : 0
        );

        var mostWickets = Math.Max(
            completedMatches.Any() ? completedMatches.Max(m => m.Player1Wickets) : 0,
            completedMatches.Any() ? completedMatches.Max(m => m.Player2Wickets) : 0
        );

        var bestStrikeRate = 0.0;
        foreach (var match in completedMatches)
        {
            if (match.Player1Balls > 0)
            {
                var sr1 = (double)match.Player1Score / match.Player1Balls * 100;
                if (sr1 > bestStrikeRate) bestStrikeRate = sr1;
            }
            if (match.Player2Balls > 0)
            {
                var sr2 = (double)match.Player2Score / match.Player2Balls * 100;
                if (sr2 > bestStrikeRate) bestStrikeRate = sr2;
            }
        }

        return new TournamentHighlights
        {
            HighestIndividualScore = highestScore,
            MostWickets = mostWickets,
            HighestTeamScore = highestScore,
            BestStrikeRate = Math.Round(bestStrikeRate, 1)
        };
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> CreateTournament([FromBody] Tournament tournament)
    {
        var userEmail = User?.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrWhiteSpace(tournament.TournamentPlayers))
            return BadRequest("TournamentPlayers must be provided as comma-separated GameNames.");

        var gameNames = tournament.TournamentPlayers.Split(",", StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(g => g.Trim().ToLower())
                                                    .Distinct()
                                                    .ToList();

        if (gameNames.Count < 2)
            return BadRequest("At least 2 distinct players are required.");

        tournament.TournamentPlayers = string.Join(",", gameNames);
        tournament.CreatedBy = userEmail;
        tournament.CreatedOn = DateTime.UtcNow;
        tournament.Matches = new List<Match>();

        _dbContext.Tournament.Add(tournament);
        await _dbContext.SaveChangesAsync();

        var matches = GenerateRoundRobinMatches(gameNames, tournament.Id, tournament.NumberOfQualifications);
        _dbContext.Match.AddRange(matches);
        await _dbContext.SaveChangesAsync();

        var createdTournament = await _dbContext.Tournament
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournament.Id);

        return Ok(createdTournament);
    }

    [HttpPut("UpdateMatch/{matchId}")]
    public async Task<IActionResult> UpdateMatch(int matchId, [FromBody] Match updateMatch)
    {
        if (updateMatch == null)
        {
            return BadRequest("Match data is required.");
        }

        var match = await _dbContext.Match.FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null)
        {
            return NotFound($"Match with ID {matchId} not found.");
        }

        match.Player1Score = updateMatch.Player1Score;
        match.Player2Score = updateMatch.Player2Score;
        match.Player1Balls = updateMatch.Player1Balls;
        match.Player2Balls = updateMatch.Player2Balls;
        match.Player1Wickets = updateMatch.Player1Wickets;
        match.Player2Wickets = updateMatch.Player2Wickets;
        match.WinnerId = updateMatch.WinnerId;
        match.TossWinnerId = updateMatch.TossWinnerId;
        match.TossWinnerChoice = updateMatch.TossWinnerChoice;
        if (updateMatch.Description.Length == 0 || updateMatch.Description == null)
        {
            if (!string.IsNullOrEmpty(match.WinnerId))
            {
                string winnerName = match.WinnerId == match.Player1Id ? match.Player1Id : match.Player2Id;

                var winnerWonToss = match.TossWinnerId == match.WinnerId;
                var tossWinnerChoseToBat = match.TossWinnerChoice?.ToLower() == "bat";

                bool wonByRuns;
                if (winnerWonToss)
                {
                    wonByRuns = tossWinnerChoseToBat;
                }
                else
                {
                    wonByRuns = !tossWinnerChoseToBat;
                }

                if (wonByRuns)
                {
                    var margin = Math.Abs(match.Player1Score - match.Player2Score);
                    match.Description = $"{winnerName} won by {margin} runs";
                }
                else
                {
                    var wicketsLost = match.WinnerId == match.Player1Id ? match.Player1Wickets : match.Player2Wickets;
                    var wicketsRemaining = 10 - wicketsLost;
                    match.Description = $"{winnerName} won by {wicketsRemaining} wickets";
                }
            }
            else
            {
                match.Description = "Match in progress";
            }
        }
        else
            match.Description = updateMatch.Description;
        await _dbContext.SaveChangesAsync();

        var tournament = await _dbContext.Tournament
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == match.TournamentId);

        if (tournament == null)
        {
            return NotFound($"Tournament with ID {match.TournamentId} not found.");
        }

        var players = tournament.TournamentPlayers?.Split(",", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList() ?? new List<string>();

        var leagueMatches = tournament.Matches.Where(m => m.MatchType == "League").ToList();
        if (leagueMatches.All(m => !string.IsNullOrEmpty(m.WinnerId)))
        {
            var (teamStandings, _, _) = GeneratePlayerStats(tournament.Matches, players, tournament.NumberOfQualifications);
            await UpdatePlayoffMatches(tournament, teamStandings, match);
        }
        else if (match.MatchType != "League" && !string.IsNullOrEmpty(match.WinnerId))
        {
            var (teamStandings, _, _) = GeneratePlayerStats(tournament.Matches, players, tournament.NumberOfQualifications);
            await UpdatePlayoffMatches(tournament, teamStandings, match);
        }

        return Ok(match);
    }

    private async Task UpdatePlayoffMatches(Tournament tournament, List<TeamStanding> teamStandings, Match updatedMatch)
    {
        if (tournament == null || teamStandings == null || updatedMatch == null)
        {
            return;
        }

        var qualifiers = tournament.NumberOfQualifications;

        var rankedTeams = teamStandings.OrderBy(s => s.Rank).ToList();
        var matches = tournament.Matches.Where(m => m.MatchType != "League").ToList();

        var leagueMatches = tournament.Matches.Where(m => m.MatchType == "League").ToList();
        if (leagueMatches.All(m => !string.IsNullOrEmpty(m.WinnerId)) && updatedMatch.MatchType == "League")
        {
            if (qualifiers == 2)
            {
                var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");
                if (finalMatch != null)
                {
                    finalMatch.Player1Id = rankedTeams[0].Team;
                    finalMatch.Player2Id = rankedTeams[1].Team;
                }
            }
            else if (qualifiers == 3)
            {
                var q1 = matches.FirstOrDefault(m => m.MatchType == "Qualifier 1");
                var eliminator = matches.FirstOrDefault(m => m.MatchType == "Eliminator");
                var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");

                if (q1 != null)
                {
                    q1.Player1Id = rankedTeams[0].Team;
                    q1.Player2Id = rankedTeams[1].Team;
                }
                if (eliminator != null)
                {
                    eliminator.Player1Id = rankedTeams[2].Team;
                    eliminator.Player2Id = "TBD";
                }
                if (finalMatch != null)
                {
                    finalMatch.Player1Id = "TBD";
                    finalMatch.Player2Id = "TBD";
                }
            }
            else if (qualifiers == 4)
            {
                var q1 = matches.FirstOrDefault(m => m.MatchType == "Qualifier 1");
                var eliminator = matches.FirstOrDefault(m => m.MatchType == "Eliminator");
                var q2 = matches.FirstOrDefault(m => m.MatchType == "Qualifier 2");
                var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");

                if (q1 != null)
                {
                    q1.Player1Id = rankedTeams[0].Team;
                    q1.Player2Id = rankedTeams[1].Team;
                }
                if (eliminator != null)
                {
                    eliminator.Player1Id = rankedTeams[2].Team;
                    eliminator.Player2Id = rankedTeams[3].Team;
                }
                if (q2 != null)
                {
                    q2.Player1Id = "TBD";
                    q2.Player2Id = "TBD";
                }
                if (finalMatch != null)
                {
                    finalMatch.Player1Id = "TBD";
                    finalMatch.Player2Id = "TBD";
                }
            }
        }
        else if (updatedMatch.MatchType != "League" && !string.IsNullOrEmpty(updatedMatch.WinnerId))
        {
            var winner = updatedMatch.WinnerId;
            var loser = updatedMatch.Player1Id == winner ? updatedMatch.Player2Id : updatedMatch.Player1Id;

            if (qualifiers == 3)
            {
                if (updatedMatch.MatchType == "Qualifier 1")
                {
                    var eliminator = matches.FirstOrDefault(m => m.MatchType == "Eliminator");
                    var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");

                    if (eliminator != null)
                    {
                        eliminator.Player2Id = loser;
                    }
                    if (finalMatch != null)
                    {
                        finalMatch.Player1Id = winner;
                    }
                }
                else if (updatedMatch.MatchType == "Eliminator")
                {
                    var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");
                    if (finalMatch != null)
                    {
                        finalMatch.Player2Id = winner;
                    }
                }
            }
            else if (qualifiers == 4)
            {
                if (updatedMatch.MatchType == "Qualifier 1")
                {
                    var q2 = matches.FirstOrDefault(m => m.MatchType == "Qualifier 2");
                    var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");

                    if (q2 != null)
                    {
                        q2.Player1Id = loser;
                    }
                    if (finalMatch != null)
                    {
                        finalMatch.Player1Id = winner;
                    }
                }
                else if (updatedMatch.MatchType == "Eliminator")
                {
                    var q2 = matches.FirstOrDefault(m => m.MatchType == "Qualifier 2");
                    if (q2 != null)
                    {
                        q2.Player2Id = winner;
                    }
                }
                else if (updatedMatch.MatchType == "Qualifier 2")
                {
                    var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");
                    if (finalMatch != null)
                    {
                        finalMatch.Player2Id = winner;
                    }
                }
            }

            if (updatedMatch.MatchType == "Final" && !string.IsNullOrEmpty(updatedMatch.WinnerId))
            {
                if (tournament.Status != "Completed")
                {
                    tournament.Status = "Completed";
                }
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private List<Match> GenerateRoundRobinMatches(List<string> playerIds, int tournamentId, int NumberOfQualifications)
    {
        var matches = new List<Match>();
        var matchups = new List<(string, string)>();

        for (int i = 0; i < playerIds.Count; i++)
        {
            for (int j = i + 1; j < playerIds.Count; j++)
            {
                matchups.Add((playerIds[i], playerIds[j]));
            }
        }

        List<(string, string)> finalMatchOrder;

        if (playerIds.Count <= 4)
        {
            var rnd = new Random();
            finalMatchOrder = matchups.OrderBy(_ => rnd.Next()).ToList();
        }
        else
        {
            finalMatchOrder = ScheduleMatchesWithBacktracking(matchups, playerIds.Count);

            if (finalMatchOrder == null || finalMatchOrder.Count == 0)
            {
                var rnd = new Random();
                finalMatchOrder = matchups.OrderBy(_ => rnd.Next()).ToList();
            }
        }

        int matchNumber = 1;

        foreach (var (p1, p2) in finalMatchOrder)
        {
            matches.Add(new Match
            {
                TournamentId = tournamentId,
                MatchNumber = matchNumber++,
                Player1Id = p1,
                Player2Id = p2,
                MatchType = "League",
                CreatedOn = DateTime.Now,
                IsActive = true,
                Player1Score = 0,
                Player2Score = 0,
                Player1Balls = 0,
                Player2Balls = 0,
                Player1Wickets = 0,
                Player2Wickets = 0,
            });
        }

        AddPlayoffMatches(matches, tournamentId, ref matchNumber, NumberOfQualifications);

        return matches;
    }

    private List<(string, string)> ScheduleMatchesWithBacktracking(List<(string, string)> matchups, int playerCount)
    {
        var schedule = new List<(string, string)>();
        var usedMatchups = new bool[matchups.Count];
        var rnd = new Random();

        var shuffledIndices = Enumerable.Range(0, matchups.Count).OrderBy(_ => rnd.Next()).ToList();

        if (BacktrackSchedule(matchups, shuffledIndices, schedule, usedMatchups, 0))
        {
            return schedule;
        }

        return new List<(string, string)>();
    }

    private bool BacktrackSchedule(List<(string, string)> matchups, List<int> shuffledIndices,
        List<(string, string)> schedule, bool[] usedMatchups, int currentIndex)
    {
        if (schedule.Count == matchups.Count)
        {
            return true;
        }

        for (int i = 0; i < shuffledIndices.Count; i++)
        {
            int matchupIndex = shuffledIndices[i];

            if (usedMatchups[matchupIndex])
                continue;

            var matchup = matchups[matchupIndex];

            if (IsValidPlacement(schedule, matchup))
            {
                schedule.Add(matchup);
                usedMatchups[matchupIndex] = true;

                if (BacktrackSchedule(matchups, shuffledIndices, schedule, usedMatchups, currentIndex + 1))
                {
                    return true;
                }

                schedule.RemoveAt(schedule.Count - 1);
                usedMatchups[matchupIndex] = false;
            }
        }

        return false;
    }

    private bool IsValidPlacement(List<(string, string)> schedule, (string, string) newMatchup)
    {
        if (schedule.Count == 0)
            return true;

        var lastMatch = schedule[schedule.Count - 1];

        return !(newMatchup.Item1 == lastMatch.Item1 || newMatchup.Item1 == lastMatch.Item2 ||
                 newMatchup.Item2 == lastMatch.Item1 || newMatchup.Item2 == lastMatch.Item2);
    }

    private void AddPlayoffMatches(List<Match> matches, int tournamentId, ref int matchNumber, int qualifiers)
    {
        if (qualifiers >= 2)
        {
            if (qualifiers == 2)
            {
                matches.Add(CreatePlayoffMatch(tournamentId, matchNumber++, "Final"));
            }
            else if (qualifiers == 3)
            {
                matches.Add(CreatePlayoffMatch(tournamentId, matchNumber++, "Qualifier 1"));
                matches.Add(CreatePlayoffMatch(tournamentId, matchNumber++, "Eliminator"));
                matches.Add(CreatePlayoffMatch(tournamentId, matchNumber++, "Final"));
            }
            else if (qualifiers == 4)
            {
                matches.Add(CreatePlayoffMatch(tournamentId, matchNumber++, "Qualifier 1"));
                matches.Add(CreatePlayoffMatch(tournamentId, matchNumber++, "Eliminator"));
                matches.Add(CreatePlayoffMatch(tournamentId, matchNumber++, "Qualifier 2"));
                matches.Add(CreatePlayoffMatch(tournamentId, matchNumber++, "Final"));
            }
        }
    }

    private Match CreatePlayoffMatch(int tournamentId, int matchNumber, string matchType)
    {
        return new Match
        {
            TournamentId = tournamentId,
            MatchNumber = matchNumber,
            Player1Id = "TBD",
            Player2Id = "TBD",
            MatchType = matchType,
            CreatedOn = DateTime.Now,
            IsActive = true,
            Player1Score = 0,
            Player2Score = 0,
            Player1Balls = 0,
            Player2Balls = 0,
            Player1Wickets = 0,
            Player2Wickets = 0,
        };
    }
}