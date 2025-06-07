using BackendApi.Models;
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
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly HttpClient _httpClient;

    public AccountController(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
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
                ProfilePicture = user.ProfilePicture,
                Roles = roles
            });
        }

        return Ok(userList);
    }
    [HttpGet("[action]")]
    public async Task<IActionResult> GetGameUsers()
    {
        var users = _userManager.Users.Where(item=>item.GameName!=null).ToList();
        var userList = new List<object>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userList.Add(new
            {
                Id = user.Id,
                Username = user.UserName,
                Email = user.Email,
                GameName = user.GameName,
                ProfilePicture = user.ProfilePicture,
                Roles = roles
            });
        }

        return Ok(userList);
    }
    [HttpPost("[action]/{userEmail}/{isAdmin:bool}")]
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

            if (tokenInfo == null  || string.IsNullOrEmpty(tokenInfo.Email))
            {
                return Unauthorized(new { Message = "Token does not belong to this app" });
            }
            var userInfoUrl = "https://www.googleapis.com/oauth2/v3/userinfo";
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenRequest);
            var userInfoResponse = await _httpClient.GetAsync(userInfoUrl);
            if (!userInfoResponse.IsSuccessStatusCode)
            {
                return Unauthorized(new { Message = "Could not retrieve user profile" });
            }
            var userInfo = await userInfoResponse.Content.ReadFromJsonAsync<GoogleUserInfo>();
            if (userInfo == null)
            {
                return Unauthorized(new { Message = "Invalid user information" });
            }
            var email = tokenInfo.Email;
            var username = email.Split('@')[0];
            username = Regex.Replace(username, @"[^a-zA-Z0-9_.]", "_");
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new User { UserName = username, Email = email,ProfilePicture = userInfo.Picture };
                var createResult = await _userManager.CreateAsync(user);

                if (!createResult.Succeeded)
                    return BadRequest(createResult.Errors);

                await _userManager.AddToRoleAsync(user, "User"); 
            }
            else
            {
                if(userInfo.Picture != null && user.ProfilePicture != userInfo.Picture)
                {
                    user.ProfilePicture = userInfo.Picture;
                    await _userManager.UpdateAsync(user);
                }
            }

            var jwtToken = GenerateJwtToken(user);
            return Ok(new { Message = "Token is valid", Token = jwtToken });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Message = "Error verifying token", Error = ex.Message });
        }
    }

    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes("YourSuperLongSecretKeyHereThatIsAtLeast16Characters");
        var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim("ProfilePicture", user.ProfilePicture ?? "")  // Add profile picture to claims
                };

        var userRoles = _userManager.GetRolesAsync(user).Result;
        if (userRoles.Any())
        {
            var rolesArray = System.Text.Json.JsonSerializer.Serialize(userRoles);
            claims.Add(new Claim("roles", rolesArray));
        }
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
    public string Azp { get; set; }
    public string Aud { get; set; }
    public string Sub { get; set; }
    public string Scope { get; set; }
    public string Exp { get; set; }
    public string ExpiresIn { get; set; }
    public string Email { get; set; }
    public string EmailVerified { get; set; }
    public string AccessType { get; set; }

}

public class GoogleUserInfo
{
    public string Sub { get; set; }
    public string Name { get; set; }
    public string GivenName { get; set; }
    public string FamilyName { get; set; }
    public string Picture { get; set; }
    public string Email { get; set; }
    public bool EmailVerified { get; set; }
    public string Locale { get; set; }
}
