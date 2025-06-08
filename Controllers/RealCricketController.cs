using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using BackendApi.Data;
using BackendApi.Models;
using Microsoft.AspNetCore.Authorization;
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
    public async Task<IActionResult> GetDashboardData()
    {
        Tournament? tournament = await _dbContext.Tournament
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

        if (tournament == null)
        {
            return NotFound("Tournament not found");
        }

        var players = tournament.TournamentPlayers?.Split(",", StringSplitOptions.RemoveEmptyEntries)
                                                   .Select(p => p.Trim())
                                                   .ToList() ?? new List<string>();

        var (teamStandings, topBatsmen, topBowlers) = GeneratePlayerStats(tournament.Matches, players);

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
                .Take(4)
                .ToList(),
            QualificationChances = GenerateQualificationChances(teamStandings),
            Highlights = GenerateHighlights(tournament.Matches)
        };

        return Ok(dashboardData);
    }

    private (List<TeamStanding>, List<TopBatsman>, List<TopBowler>) GeneratePlayerStats(List<Match> matches, List<string> players)
    {
        var teamStandings = new List<TeamStanding>();
        var battingStats = new List<(string Name, int Runs, int Balls, int Matches, int Innings)>();
        var bowlingStats = new List<(string Name, int Wickets, int RunsConceded, int Balls, int Matches)>();

        foreach (var player in players)
        {
            var playerMatches = matches.Where(m =>
                (m.Player1Id == player || m.Player2Id == player) && m.MatchType == "League").ToList();

            var won = playerMatches.Count(m => m.WinnerId == player);
            var lost = playerMatches.Where(item => item.WinnerId != null).ToList().Count - won;
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

            foreach (var match in playerMatches)
            {
                if (match.Player1Id == player)
                {
                    totalRunsScored += match.Player1Score;
                    totalBallsFaced += match.Player1Balls;
                    totalRunsConceded += match.Player2Score;
                    totalBallsBowled += match.Player2Balls;

                    battingRuns += match.Player1Score;
                    battingBalls += match.Player1Balls;
                    battingInnings++;

                    bowlingWickets += match.Player2Wickets;
                    bowlingRunsConceded += match.Player2Score;
                    bowlingBalls += match.Player1Balls;
                }
                else
                {
                    totalRunsScored += match.Player2Score;
                    totalBallsFaced += match.Player2Balls;
                    totalRunsConceded += match.Player1Score;
                    totalBallsBowled += match.Player1Balls;

                    battingRuns += match.Player2Score;
                    battingBalls += match.Player2Balls;
                    battingInnings++;

                    bowlingWickets += match.Player1Wickets;
                    bowlingRunsConceded += match.Player1Score;
                    bowlingBalls += match.Player2Balls;
                }
            }

            var runRate = totalBallsFaced > 0 ? (double)totalRunsScored / totalBallsFaced * 6 : 0;
            var concededRate = totalBallsBowled > 0 ? (double)totalRunsConceded / totalBallsBowled * 6 : 0;
            var nrr = runRate - concededRate;

            teamStandings.Add(new TeamStanding
            {
                Team = player,
                Played = playerMatches.Where(item => item.WinnerId != null).ToList().Count,
                Won = won,
                Lost = lost,
                Points = points,
                NetRunRate = Math.Round(nrr, 2).ToString("+0.00;-0.00;0.00")
            });

            battingStats.Add((player, battingRuns, battingBalls, playerMatches.Count, battingInnings));
            bowlingStats.Add((player, bowlingWickets, bowlingRunsConceded, bowlingBalls, playerMatches.Count));
        }

        var rankedStandings = teamStandings
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => double.Parse(s.NetRunRate))
            .Select((s, index) => { s.Rank = index + 1; return s; })
            .ToList();

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
            .Take(5)
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
            .Take(5)
            .Select((b, index) => { b.Rank = index + 1; return b; })
            .ToList();

        return (rankedStandings, topBatsmen, topBowlers);
    }

    private List<QualificationChance> GenerateQualificationChances(List<TeamStanding> standings)
    {
        return standings.Select(standing => new QualificationChance
        {
            Team = standing.Team,
            Top2Chance = CalculateQualificationChance(standing.Rank, standing.Points, true),
            Top4Chance = CalculateQualificationChance(standing.Rank, standing.Points, false),
            Status = GetQualificationStatus(CalculateQualificationChance(standing.Rank, standing.Points, false))
        }).ToList();
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

    private int CalculateQualificationChance(int rank, int points, bool isTop2)
    {
        if (isTop2)
        {
            return rank switch
            {
                1 => 95,
                2 => 85,
                3 => 45,
                4 => 25,
                5 => 5,
                _ => 1
            };
        }
        else
        {
            return rank switch
            {
                1 => 99,
                2 => 98,
                3 => 90,
                4 => 75,
                5 => 35,
                _ => 15
            };
        }
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

        var matches = GenerateRoundRobinMatches(gameNames, tournament.Id);
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
        match.Description = updateMatch.Description;

        await _dbContext.SaveChangesAsync();

        // Load the tournament with its matches
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

        // Check if all league matches are complete
        var leagueMatches = tournament.Matches.Where(m => m.MatchType == "League").ToList();
        if (leagueMatches.All(m => !string.IsNullOrEmpty(m.WinnerId)))
        {
            var (teamStandings, _, _) = GeneratePlayerStats(tournament.Matches, players);
            await UpdatePlayoffMatches(tournament, teamStandings, match);
        }
        // Check if a playoff match was updated
        else if (match.MatchType != "League" && !string.IsNullOrEmpty(match.WinnerId))
        {
            var (teamStandings, _, _) = GeneratePlayerStats(tournament.Matches, players);
            await UpdatePlayoffMatches(tournament, teamStandings, match);
        }

        return Ok(match);
    }

    private async Task UpdatePlayoffMatches(Tournament tournament, List<TeamStanding> teamStandings, Match updatedMatch)
    {
        var qualifiers = Math.Min(teamStandings.Count, 4);
        if (teamStandings.Count == 2)
            qualifiers = 2;
        else if (teamStandings.Count == 3)
            qualifiers = 3;

        var rankedTeams = teamStandings.OrderBy(s => s.Rank).ToList();
        var matches = tournament.Matches.Where(m => m.MatchType != "League").ToList();

        // If all league matches are complete, initialize playoff matches
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
                    eliminator.Player2Id = "TBD"; // Loser of Q1
                }
                if (finalMatch != null)
                {
                    finalMatch.Player1Id = "TBD"; // Winner of Q1
                    finalMatch.Player2Id = "TBD"; // Winner of Eliminator
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
                    q2.Player1Id = "TBD"; // Loser of Q1
                    q2.Player2Id = "TBD"; // Winner of Eliminator
                }
                if (finalMatch != null)
                {
                    finalMatch.Player1Id = "TBD"; // Winner of Q1
                    finalMatch.Player2Id = "TBD"; // Winner of Q2
                }
            }
        }
        // Handle playoff match updates
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
                        eliminator.Player2Id = loser; // Loser of Q1
                    }
                    if (finalMatch != null)
                    {
                        finalMatch.Player1Id = winner; // Winner of Q1
                    }
                }
                else if (updatedMatch.MatchType == "Eliminator")
                {
                    var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");
                    if (finalMatch != null)
                    {
                        finalMatch.Player2Id = winner; // Winner of Eliminator
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
                        q2.Player1Id = loser; // Loser of Q1
                    }
                    if (finalMatch != null)
                    {
                        finalMatch.Player1Id = winner; // Winner of Q1
                    }
                }
                else if (updatedMatch.MatchType == "Eliminator")
                {
                    var q2 = matches.FirstOrDefault(m => m.MatchType == "Qualifier 2");
                    if (q2 != null)
                    {
                        q2.Player2Id = winner; // Winner of Eliminator
                    }
                }
                else if (updatedMatch.MatchType == "Qualifier 2")
                {
                    var finalMatch = matches.FirstOrDefault(m => m.MatchType == "Final");
                    if (finalMatch != null)
                    {
                        finalMatch.Player2Id = winner; // Winner of Q2
                    }
                }
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private List<Match> GenerateRoundRobinMatches(List<string> playerIds, int tournamentId)
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

        var rnd = new Random();
        matchups = matchups.OrderBy(_ => rnd.Next()).ToList();

        var finalMatchOrder = new List<(string, string)>();
        var usedPlayers = new HashSet<string>();
        int consecutiveLimit = playerIds.Count > 4 ? 2 : 0;

        while (matchups.Any())
        {
            bool matchFound = false;

            for (int i = 0; i < matchups.Count; i++)
            {
                var (p1, p2) = matchups[i];

                if (playerIds.Count > 4 && (usedPlayers.Contains(p1) || usedPlayers.Contains(p2)))
                {
                    continue;
                }

                var players = rnd.Next(2) == 0 ? (p1, p2) : (p2, p1);
                finalMatchOrder.Add(players);

                if (playerIds.Count > 4)
                {
                    usedPlayers.Add(players.Item1);
                    usedPlayers.Add(players.Item2);

                    if (finalMatchOrder.Count >= consecutiveLimit)
                    {
                        var oldMatch = finalMatchOrder[finalMatchOrder.Count - consecutiveLimit];
                        usedPlayers.Remove(oldMatch.Item1);
                        usedPlayers.Remove(oldMatch.Item2);
                    }
                }

                matchups.RemoveAt(i);
                matchFound = true;
                break;
            }

            if (!matchFound && matchups.Any())
            {
                var (p1, p2) = matchups[0];
                var players = rnd.Next(2) == 0 ? (p1, p2) : (p2, p1);
                finalMatchOrder.Add(players);

                if (playerIds.Count > 4)
                {
                    usedPlayers.Add(players.Item1);
                    usedPlayers.Add(players.Item2);

                    if (finalMatchOrder.Count >= consecutiveLimit)
                    {
                        var oldMatch = finalMatchOrder[finalMatchOrder.Count - consecutiveLimit];
                        usedPlayers.Remove(oldMatch.Item1);
                        usedPlayers.Remove(oldMatch.Item2);
                    }
                }

                matchups.RemoveAt(0);
            }
        }

        int matchNumber = 1;
        // Add league matches
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

        // Determine number of qualifiers (default to 4, adjust based on player count)
        int qualifiers = Math.Min(playerIds.Count, 4);
        if (playerIds.Count == 2)
            qualifiers = 2;
        else if (playerIds.Count == 3)
            qualifiers = 3;

        // Add playoff matches based on number of qualifiers
        if (qualifiers >= 2)
        {
            if (qualifiers == 2)
            {
                // Only Final for 2 qualifiers
                matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    MatchNumber = matchNumber++,
                    Player1Id = "TBD",
                    Player2Id = "TBD",
                    MatchType = "Final",
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
            else if (qualifiers == 3)
            {
                // Q1: 1st vs 2nd
                matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    MatchNumber = matchNumber++,
                    Player1Id = "TBD",
                    Player2Id = "TBD",
                    MatchType = "Qualifier 1",
                    CreatedOn = DateTime.Now,
                    IsActive = true,
                    Player1Score = 0,
                    Player2Score = 0,
                    Player1Balls = 0,
                    Player2Balls = 0,
                    Player1Wickets = 0,
                    Player2Wickets = 0,
                });
                // Eliminator: 3rd vs Loser of Q1
                matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    MatchNumber = matchNumber++,
                    Player1Id = "TBD",
                    Player2Id = "TBD",
                    MatchType = "Eliminator",
                    CreatedOn = DateTime.Now,
                    IsActive = true,
                    Player1Score = 0,
                    Player2Score = 0,
                    Player1Balls = 0,
                    Player2Balls = 0,
                    Player1Wickets = 0,
                    Player2Wickets = 0,
                });
                // Final: Winner of Q1 vs Winner of Eliminator
                matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    MatchNumber = matchNumber++,
                    Player1Id = "TBD",
                    Player2Id = "TBD",
                    MatchType = "Final",
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
            else if (qualifiers == 4)
            {
                // Q1: 1st vs 2nd
                matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    MatchNumber = matchNumber++,
                    Player1Id = "TBD",
                    Player2Id = "TBD",
                    MatchType = "Qualifier 1",
                    CreatedOn = DateTime.Now,
                    IsActive = true,
                    Player1Score = 0,
                    Player2Score = 0,
                    Player1Balls = 0,
                    Player2Balls = 0,
                    Player1Wickets = 0,
                    Player2Wickets = 0,
                });
                // Eliminator: 3rd vs 4th
                matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    MatchNumber = matchNumber++,
                    Player1Id = "TBD",
                    Player2Id = "TBD",
                    MatchType = "Eliminator",
                    CreatedOn = DateTime.Now,
                    IsActive = true,
                    Player1Score = 0,
                    Player2Score = 0,
                    Player1Balls = 0,
                    Player2Balls = 0,
                    Player1Wickets = 0,
                    Player2Wickets = 0,
                });
                // Q2: Loser of Q1 vs Winner of Eliminator
                matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    MatchNumber = matchNumber++,
                    Player1Id = "TBD",
                    Player2Id = "TBD",
                    MatchType = "Qualifier 2",
                    CreatedOn = DateTime.Now,
                    IsActive = true,
                    Player1Score = 0,
                    Player2Score = 0,
                    Player1Balls = 0,
                    Player2Balls = 0,
                    Player1Wickets = 0,
                    Player2Wickets = 0,
                });
                // Final: Winner of Q1 vs Winner of Q2
                matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    MatchNumber = matchNumber++,
                    Player1Id = "TBD",
                    Player2Id = "TBD",
                    MatchType = "Final",
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
        }

        return matches;
    }
}

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