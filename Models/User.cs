using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace BackendApi.Models;

public class User : IdentityUser
{
    [MaxLength(500)]
    public string? ProfilePicture {get;set;}
}
