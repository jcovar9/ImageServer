using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using JonahsImageServer.Models;

namespace JonahsImageServer.Controllers;

public class AccountController(ApplicationDbContext context) : Controller
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    public IActionResult Login()
    {
        HttpContext.Session.Clear();
        return View("Login");
    }

    [HttpPost]
    public async Task<IActionResult> Login(string Username, string Password)
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ViewBag.Error = "Username and password are required.";
            return View("Login");
        }
        DBUser? user = await _context.Users.FindAsync(Username);
        if(user is null)
        {
            ViewBag.Error = "Username not associated with existing User.";
            return View("Login");
        }
        string hashedPassword = ComputeSha256Hash(Password);
        if (user.PasswordHash != hashedPassword)
        {
            ViewBag.Error = "Password incorrect.";
            return View("Login");
        }
        Console.WriteLine(user.Username + " logged in.");

        HttpContext.Session.SetString("Username", Username);

        return RedirectToAction("EnterDirectory", "Home", new { folderIDPath = user.RootID });
    }

    [HttpGet]
    public IActionResult Register()
    {
        HttpContext.Session.Clear();
        return View("Register");
    }

    [HttpPost]
    public async Task<IActionResult> Register(string Username, string Password)
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ViewBag.Error = "Username and password are required.";
            return View("Register");
        }

        if (Username.Any(HomeController.InvalidChars.Contains))
        {
            ViewBag.Error = "Username cannot have " + string.Join(", ", HomeController.InvalidChars.ToCharArray());
            return View("Register");
        }

        // Check if the username is already taken
        DBUser? user = await _context.Users.FindAsync(Username);
        if(user is not null)
        {
            ViewBag.Error = "Username is already taken.";
            return View("Register");
        }

        // Hash the password
        string passwordHash = ComputeSha256Hash(Password);

        // Create root folder
        DBFolder rootFolder = new()
        {
            ID = Guid.NewGuid().ToString(),
            Name = "Home",
            Owner = Username,
            Size = 0.0,
        };

        // Create shared folder
        DBFolder sharedFolder = new()
        {
            ID = Guid.NewGuid().ToString(),
            Name = "Shared With Me",
            Owner = Username,
            Size = 0.0,
        };
        _context.Folders.AddRange(rootFolder, sharedFolder);

        // Create new user
        user = new DBUser
        {
            Username = Username,
            PasswordHash = passwordHash,
            RootID = rootFolder.ID,
            SharedWithMeID = sharedFolder.ID,
        };
        _context.Users.Add(user);

        await _context.SaveChangesAsync();
        
        Console.WriteLine(user.Username + " registered.");

        // Redirect to Login page
        return RedirectToAction("Login", "Account");
    }

    public static string ComputeSha256Hash(string rawData)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        StringBuilder builder = new();
        foreach (byte b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }
        return builder.ToString();
    }
}
