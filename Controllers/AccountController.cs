using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using JonahsImageServer.Models;
using Microsoft.EntityFrameworkCore;

namespace JonahsImageServer.Controllers;

public class AccountController(ApplicationDbContext context) : Controller
{
    private readonly ApplicationDbContext _context = context;

    [HttpPost]
    public async Task<IActionResult> DeleteProfile()
    {
        try
        {
            string? userID = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userID))
            {
                return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
            }

            DBUser? dBUser = await _context.Users.FindAsync(userID);
            if (dBUser is null)
            {
                return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
            }

            DBFolder? sharedFolder = await _context.Folders.FindAsync(dBUser.SharedWithMeID);
            if (sharedFolder is not null)
            {
                List<DBFolder> foldersSharedWithMe = await _context.Folders
                    .Where(f => sharedFolder.Subfolders.Contains(f.ID))
                    .ToListAsync();

                foldersSharedWithMe.ForEach(folder => folder.SharedWith.Remove(dBUser.ID));
                _context.Folders.Remove(sharedFolder);
            }

            await HomeController.RecursiveDeleteFolder(_context, dBUser.RootID);
            _context.Users.Remove(dBUser);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        string? userID = HttpContext.Session.GetString("UserID");
        if (string.IsNullOrEmpty(userID))
        {
            return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
        }

        DBUser? dBUser = await _context.Users.FindAsync(userID);
        if (dBUser is null)
        {
            return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
        }

        DBFolder? dBFolder = await _context.Folders.FindAsync(dBUser.RootID);
        if (dBFolder is not null)
        {
            ViewBag.SpaceUsed = HomeController.GetDisplaySize(dBFolder.Size);
        }
        DBFolder? dBSharedFolder = await _context.Folders.FindAsync(dBUser.SharedWithMeID);
        if (dBSharedFolder is not null)
        {
            ViewBag.SharedFolders = dBSharedFolder.Subfolders.Count;
        }

        if (HttpContext.Session.GetString("Error") is not null)
        {
            ViewBag.Error = HttpContext.Session.GetString("Error");
            HttpContext.Session.Remove("Error");
        }

        ViewBag.Username = dBUser.Username;
        ViewBag.RootID = dBUser.RootID;
        ViewBag.SharedWithMeID = dBUser.SharedWithMeID;
        return View("Profile");
    }

    [HttpPost]
    public async Task<IActionResult> ChangeUsername(string Username)
    {
        string? userID = HttpContext.Session.GetString("UserID");
        if (string.IsNullOrEmpty(userID))
        {
            return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
        }

        DBUser? dBUser = await _context.Users.FindAsync(userID);
        if (dBUser is null)
        {
            return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
        }

        if (dBUser.Username == Username)
        {
            HttpContext.Session.SetString("Error", $"Username is already {dBUser.Username}");
            return RedirectToAction("Profile", "Account");
        }

        // Check if the username is already taken
        DBUser? user = await _context.Users
            .Where(u => u.Username == Username)
            .FirstOrDefaultAsync();
        if (user is not null)
        {
            HttpContext.Session.SetString("Error", $"Username: {user.Username} is already taken.");
            return RedirectToAction("Profile", "Account");
        }

        if (HomeController.InvalidChars.Any(Username.Contains))
        {
            HttpContext.Session.SetString("Error", $"Username cannot contain: {string.Join(", ", HomeController.InvalidChars.ToCharArray())}");
            return RedirectToAction("Profile", "Account");
        }

        dBUser.Username = Username;
        _context.Users.Update(dBUser);
        await _context.SaveChangesAsync();
        return RedirectToAction("Profile", "Account");
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword(string OldPassword, string NewPassword)
    {
        string? userID = HttpContext.Session.GetString("UserID");
        if (string.IsNullOrEmpty(userID))
        {
            return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
        }

        DBUser? dBUser = await _context.Users.FindAsync(userID);
        if (dBUser is null)
        {
            return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
        }

        if (OldPassword == NewPassword)
        {
            HttpContext.Session.SetString("Error", $"New password cannot be the same as old password");
            return RedirectToAction("Profile", "Account");
        }

        string oldPasswordHash = ComputeSha256Hash(OldPassword);
        if (dBUser.PasswordHash != oldPasswordHash)
        {
            HttpContext.Session.SetString("Error", $"Old password is incorrect: {OldPassword}");
            return RedirectToAction("Profile", "Account");
        }

        dBUser.PasswordHash = ComputeSha256Hash(NewPassword);
        _context.Users.Update(dBUser);
        await _context.SaveChangesAsync();
        return RedirectToAction("Profile", "Account");
    }

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
        DBUser? user = await _context.Users
            .Where(u => u.Username == Username)
            .FirstOrDefaultAsync();
        if (user is null)
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

        HttpContext.Session.SetString("UserID", user.ID);

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
        if (HomeController.InvalidChars.Any(Username.Contains))
        {
            ViewBag.Error = $"Username cannot contain: {string.Join(", ", HomeController.InvalidChars.ToCharArray())}";
            return View("Register");
        }

        // Check if the username is already taken
        DBUser? user = await _context.Users
            .Where(u => u.Username == Username)
            .FirstOrDefaultAsync();
        if (user is not null)
        {
            ViewBag.Error = "Username is already taken.";
            return View("Register");
        }

        string userID = Guid.NewGuid().ToString();
        // Create root folder
        DBFolder rootFolder = new()
        {
            ID = Guid.NewGuid().ToString(),
            Name = "Home",
            OwnerID = userID,
            Size = 0.0,
        };

        // Create shared folder
        DBFolder sharedFolder = new()
        {
            ID = Guid.NewGuid().ToString(),
            Name = "Shared With Me",
            OwnerID = userID,
            Size = 0.0,
        };
        _context.Folders.AddRange(rootFolder, sharedFolder);

        // Create new user
        user = new DBUser
        {
            ID = userID,
            Username = Username,
            PasswordHash = ComputeSha256Hash(Password),
            RootID = rootFolder.ID,
            SharedWithMeID = sharedFolder.ID,
        };
        _context.Users.Add(user);

        await _context.SaveChangesAsync();

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
