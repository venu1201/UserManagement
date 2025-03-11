using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

[Route("api/[controller]")]
[ApiController]
public class AccountController : ControllerBase
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly HttpClient _httpClient;

    public AccountController(
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        HttpClient httpClient)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _httpClient = httpClient;
    }
    [HttpGet("[action]")]
    public async Task<IActionResult> GetAll()
    {
        var users = _userManager.Users.ToList();
        var userList = new List<object>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userList.Add(new
            {
                Id = user.Id,
                Username = user.UserName,
                Email = user.Email,
                Roles = roles
            });
        }

        return Ok(userList);
    }
    [HttpPost("[action]/{userEmail:string}/{isAdmin:bool}")]
    public async Task<IActionResult> AddRole([FromRoute] string userEmail , [FromRoute] bool isAdmin )
    {
        if (string.IsNullOrEmpty(userEmail))
        {
            return BadRequest(new { Message = "Email is required" });
        }

        var user = await _userManager.FindByEmailAsync(userEmail);
        if (user == null)
        {
            return NotFound(new { Message = $"User with email {userEmail} not found" });
        }

        try
        {
            var isCurrentlyAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            if (isAdmin && !isCurrentlyAdmin)
            {
                var addResult = await _userManager.AddToRoleAsync(user, "Admin");
                if (!addResult.Succeeded)
                {
                    return BadRequest(new { Message = "Failed to add user to Admin role", Errors = addResult.Errors });
                }
                return Ok(new { Message = $"User {userEmail} has been promoted to Admin" });
            }
            else if (!isAdmin && isCurrentlyAdmin)
            {
                var removeResult = await _userManager.RemoveFromRoleAsync(user, "Admin");
                if (!removeResult.Succeeded)
                {
                    return BadRequest(new { Message = "Failed to remove user from Admin role", Errors = removeResult.Errors });
                }
                return Ok(new { Message = $"User {userEmail} has been removed from Admin role" });
            }

            var currentStatus = isCurrentlyAdmin ? "already an Admin" : "not an Admin";
            return Ok(new { Message = $"User {userEmail} is {currentStatus}, no changes made" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Message = "Error updating admin status", Error = ex.Message });
        }
    }


    [HttpPost("verify-token/{tokenRequest}")]
    public async Task<IActionResult> VerifyGoogleToken([FromRoute] string tokenRequest)
    {
        if (string.IsNullOrEmpty(tokenRequest))
        {
            return BadRequest(new { Message = "Access token is required" });
        }

        var googleTokenInfoUrl = $"https://www.googleapis.com/oauth2/v3/tokeninfo?access_token={tokenRequest}";

        try
        {
            var response = await _httpClient.GetAsync(googleTokenInfoUrl);
            if (!response.IsSuccessStatusCode)
            {
                return Unauthorized(new { Message = "Invalid access token" });
            }

            var tokenInfo = await response.Content.ReadFromJsonAsync<GoogleTokenInfo>();

            if (tokenInfo == null)
            {
                return Unauthorized(new { Message = "Token does not belong to this app" });
            }

            var email = tokenInfo.Email;
            var username = email.Split('@')[0];
            username = Regex.Replace(username, @"[^a-zA-Z0-9_.]", "_");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new IdentityUser { UserName = username, Email = email };
                var createResult = await _userManager.CreateAsync(user);

                if (!createResult.Succeeded)
                    return BadRequest(createResult.Errors);

                await _userManager.AddToRoleAsync(user, "User"); // Assign default role
            }

            var jwtToken = GenerateJwtToken(user);
            return Ok(new { Message = "Token is valid", Token = jwtToken });
        }
        catch
        {
            return StatusCode(500, new { Message = "Error verifying token" });
        }
    }

    private string GenerateJwtToken(IdentityUser user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes("<YOUR_SECRET_KEY>");
        var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id),
                        new Claim(ClaimTypes.Name, user.UserName),
                        new Claim(ClaimTypes.Email, user.Email)
                    };

        var userRoles = _userManager.GetRolesAsync(user).Result;
        claims.AddRange(userRoles.Select(role => new Claim(ClaimTypes.Role, role)));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(24),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

}


public class GoogleTokenInfo
{
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public string Email { get; set; }
    public string Expiry { get; set; }
    public string Scope { get; set; }


}
