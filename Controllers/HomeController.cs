using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using JonahsImageServer.Models;
using Microsoft.EntityFrameworkCore;
using NetVips;

namespace JonahsImageServer.Controllers;

public class HomeController(ApplicationDbContext context) : Controller
{
    private readonly ApplicationDbContext _context = context;
    private const string ImagesDirectory = "wwwroot/uploads";
    public const int ItemsPerLoad = 5;
    public const string InvalidChars = "\'\"`()/\\";

    [HttpGet]
    public async Task<IActionResult> EnterDirectory(string folderIDPath = "")
    {
        // Get current username
        string? username = HttpContext.Session.GetString("Username");
        if(string.IsNullOrEmpty(username))
        {
            return RedirectToAction("Login", "Account"); // Redirect to login if not logged in
        }

        DBUser? dBUser = await _context.Users.FindAsync(username);
        if (dBUser is null)
        {
            return NotFound($"Username not in database: {username}");
        }

        List<DBFolder> folderPath = [];
        foreach (string folderID in folderIDPath.Split('/'))
        {
            if (!string.IsNullOrEmpty(folderID))
            {
                DBFolder? folder = await _context.Folders.FindAsync(folderID);
                if (folder is null)
                {
                    return NotFound($"The requested folder not in database: {folderID}");
                }
                folderPath.Add(folder);
            }
        }
        if (folderPath.Count == 0)
        {
            return NotFound($"Invalid folder path: {folderIDPath}");
        }

        DBFolder currFolder = folderPath.Last();
        folderPath.RemoveAt(folderPath.Count - 1);


        HomeViewModel model = new()
        {
            Username = username,
            FolderPath = folderPath,
            CurrFolder = currFolder,
        };
        ViewBag.RootID = dBUser.RootID;
        ViewBag.SharedWithMeID = dBUser.SharedWithMeID;
        return View("Index", model);
    }

    [HttpPost]
    public async Task<IActionResult> UploadChunk(string folderID, IFormFile chunk, string fileName, int chunkIndex, int totalChunks)
    {
        try
        {
            // Check for valid chunk
            if (chunk == null || chunk.Length == 0)
            {
                return Json(new { success = false, message = "No file received" });
            }

            // Check for valid user
            string? username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            DBFolder? currFolder = await _context.Folders.FindAsync(folderID);
            if (currFolder is null)
            {
                return Json(new { success = false, message = $"Folder not in database: {folderID}" });
            }

            if (chunkIndex == 0 && await _context.Images
                                        .Where(i => currFolder.Images.Contains(i.ID))
                                        .AnyAsync(i => i.Name == fileName))
            {
                return Json(new { success = false, message = $"Image with this name already exists: {fileName}"});
            }

            FileInfo tempFilePath = new(Path.Combine(ImagesDirectory, fileName + ".temp"));
            using (FileStream stream = new(tempFilePath.FullName, FileMode.Append, FileAccess.Write))
            {
                await chunk.CopyToAsync(stream);
            }

            // Check if this was the last chunk
            if (chunkIndex == totalChunks - 1)
            {
                DBImage dBImage = new ()
                {
                    ID = Guid.NewGuid().ToString() + Path.GetExtension(fileName),
                    Name = fileName,
                    Size = tempFilePath.Length,
                };
                currFolder.Size += dBImage.Size;
                currFolder.Images.Add(dBImage.ID);
                _context.Images.Add(dBImage);
                _context.Folders.Update(currFolder);
                currFolder = await _context.Folders.FindAsync(currFolder.ParentFolderID);
                while (currFolder is not null)
                {
                    currFolder.Size += dBImage.Size;
                    _context.Folders.Update(currFolder);
                    currFolder = await _context.Folders.FindAsync(currFolder.ParentFolderID);
                }
                await _context.SaveChangesAsync();

                FileInfo finalPath = new(Path.Combine(ImagesDirectory, dBImage.ID));
                System.IO.File.Move(tempFilePath.FullName, finalPath.FullName);
            }
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error uploading chunk: {ex.Message}");
            return Json(new { success = false, message = $"Error: {ex.Message}"});
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateFolder(string folderID, string folderName)
    {
        try
        {
            string? username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            DBFolder? parentFolder = await _context.Folders.FindAsync(folderID);
            if (parentFolder is null)
            {
                return Json(new { success = false, message = $"Parent folder does not exist: {folderID}" });
            }

            if (folderName.Any(InvalidChars.Contains))
            {
                return Json(new { success = false, message = "Folder name cannot have " + string.Join(", ", InvalidChars.ToCharArray()) });
            }

            if (await _context.Folders
                .Where(f => parentFolder.Subfolders.Contains(f.ID))
                .AnyAsync(f => f.Name == folderName))
            {
                return Json(new { success = false, message = $"Folder with this name already exists: {folderName}"});
            }

            DBFolder newFolder = new()
            {
                ID = Guid.NewGuid().ToString(),
                Name = folderName,
                Owner = parentFolder.Owner,
                Size = 0.0,
                ParentFolderID = parentFolder.ID,
            };
            _context.Folders.Add(newFolder);
            parentFolder.Subfolders.Add(newFolder.ID);
            _context.Folders.Update(parentFolder);
            await _context.SaveChangesAsync();

            return Json(new { success = true, id = newFolder.ID, name = newFolder.Name, owner = newFolder.Owner });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message});
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadImage(string imageID)
    {
        string? username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized();
        }

        FileInfo imagePath = new(Path.Combine(ImagesDirectory, imageID));
        if (!imagePath.Exists)
        {
            return NotFound($"Image not on server: {imagePath.FullName}");
        }

        DBImage? dBImage = await _context.Images.FindAsync(imageID);
        if (dBImage is null)
        {
            return NotFound("Image not in database");
        }

        byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(imagePath.FullName);
        return File(fileBytes, "application/octet-stream", dBImage.Name);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteImage(string folderID, string imageID)
    {
        try
        {
            string? username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            FileInfo imagePath = new(Path.Combine(ImagesDirectory, imageID));
            if (!imagePath.Exists)
            {
                return Json(new { success = false, message = $"Image to delete not on server: {imageID}" });
            }

            DBFolder? currFolder = await _context.Folders.FindAsync(folderID);
            if (currFolder is null)
            {
                return Json(new { success = false, message = $"Folder not in database: {folderID}"});
            }
            
            if (username != currFolder.Owner)
            {
                return Json(new { success = false, message = $"Cannot delete image because the current folder belongs to: {currFolder.Owner}"});
            }

            DBImage? dBImage = await _context.Images.FindAsync(imageID);
            if (dBImage is null)
            {
                return Json(new { success = false, message = $"Image not in database: {imageID}"});
            }

            currFolder.Images.Remove(dBImage.ID);
            _context.Images.Remove(dBImage);

            while(currFolder is not null)
            {
                currFolder.Size -= dBImage.Size;
                _context.Folders.Update(currFolder);
                currFolder = await _context.Folders.FindAsync(currFolder.ParentFolderID);
            }
            await _context.SaveChangesAsync();

            imagePath.Delete();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult GetImagePreview(string imageID)
    {
        try
        {
            FileInfo imagePath = new(Path.Combine(ImagesDirectory, imageID));
            if (!imagePath.Exists)
            {
                return NotFound();
            }

            byte[] imageData;
            using(Image image = Image.NewFromFile(imagePath.FullName))
            {
                if (256 < image.Width && 256 < image.Height)
                {
                    using Image resizedImage = image.Resize(0.5);
                    imageData = resizedImage.WebpsaveBuffer();
                }
                else
                {
                    imageData = image.WebpsaveBuffer();
                }
            }
            return File(imageData, "image/webp");
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteFolder(string folderID)
    {
        try
        {
            string? username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            DBFolder? targetFolder = await _context.Folders.FindAsync(folderID);
            if (targetFolder is null)
            {
                return Json(new { success = false, message = $"Folder to delete not in database: {folderID}" });
            }

            if (username != targetFolder.Owner)
            {
                return Json(new { success = false, message = $"Cannot delete because this folder belongs to: {targetFolder.Owner}"});
            }

            DBFolder? parentFolder = await _context.Folders.FindAsync(targetFolder.ParentFolderID);
            if (parentFolder is null)
            {
                return Json(new { success = false, message = $"Parent folder not in database: {targetFolder.ParentFolderID}"});
            }

            parentFolder.Subfolders.Remove(targetFolder.ID);

            while (parentFolder is not null)
            {
                parentFolder.Size -= targetFolder.Size;
                _context.Folders.Update(parentFolder);
                parentFolder = await _context.Folders.FindAsync(parentFolder.ParentFolderID);
            }
            RecursiveDeleteFolder(targetFolder.ID);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    private async void RecursiveDeleteFolder(string folderID)
    {
        // get folder
        DBFolder? dBFolder = await _context.Folders.FindAsync(folderID);
        if (dBFolder is null) { return; }

        // go thru each subfolder in current folder
        dBFolder.Subfolders.ForEach(RecursiveDeleteFolder);

        // unshare this folder with all shared users
        List<string> sharedFolderIDs = await _context.Users
            .Where(u => dBFolder.SharedWith.Contains(u.Username))
            .Select(u => u.SharedWithMeID)
            .ToListAsync();
        List<DBFolder> sharedFolders = await _context.Folders
            .Where(f => sharedFolderIDs.Contains(f.ID))
            .ToListAsync();
        sharedFolders.ForEach(sharedFolder =>
        {
            sharedFolder.Subfolders.Remove(folderID);
        });
        _context.Folders.UpdateRange(sharedFolders);

        // delete images in current folder
        await _context.Images.Where(i => dBFolder.Images.Contains(i.ID)).ExecuteDeleteAsync();
        foreach (string imageID in dBFolder.Images)
        {
            FileInfo fileInfo = new(Path.Combine(ImagesDirectory, imageID));
            if (fileInfo.Exists) fileInfo.Delete();
        }

        // delete current folder
        _context.Folders.Remove(dBFolder);
    }

    [HttpGet]
    public IActionResult GetThumbnail(string imageID)
    {
        try
        {
            FileInfo imagePath = new(Path.Combine(ImagesDirectory, imageID));
            if (!imagePath.Exists)
            {
                return NotFound();
            }

            using var thumbnail = Image.Thumbnail(Path.Combine(ImagesDirectory, imageID), 128);
            return File(thumbnail.WebpsaveBuffer(), "image/webp");
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetItems(string folderID, int startIndex)
    {
        try
        {
            string? username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return Json(new { success = false, message = "User not logged in"});
            }

            DBFolder? dBFolder = await _context.Folders.FindAsync(folderID);
            if (dBFolder is null)
            {
                return Json(new { success = false, message = $"Folder is not in database: {folderID}" });
            }

            Dictionary<string, Dictionary<string, string>> subfolders = [];
            Dictionary<string, Dictionary<string, string>> images = [];
            for (int i = startIndex; i < startIndex + ItemsPerLoad; i++)
            {
                if (i < dBFolder.Subfolders.Count)
                {
                    subfolders.Add(dBFolder.Subfolders[i], []);
                }
                else if (i < dBFolder.Subfolders.Count + dBFolder.Images.Count)
                {
                    images.Add(dBFolder.Images[i - dBFolder.Subfolders.Count], []);
                }
            }

            var subfolderInfo = await _context.Folders
                .Where(f => subfolders.Keys.Contains(f.ID))
                .Select(f => new { f.ID, f.Name, f.Size, f.Owner })
                .ToListAsync();
            subfolderInfo.ForEach(subfolder =>
            {
                double sizeMB = subfolder.Size / 1024.0 / 1024.0;
                double sizeGB = subfolder.Size / 1024.0 / 1024.0 / 1024.0;
                
                subfolders[subfolder.ID].Add("Name", subfolder.Name);
                subfolders[subfolder.ID].Add("Size", 1024.0 < sizeMB ? sizeGB.ToString("F2") + " GB" : sizeMB.ToString("F2") + " MB");
                subfolders[subfolder.ID].Add("Owner", subfolder.Owner);
            });

            var imageInfo = await _context.Images
                .Where(i => images.Keys.Contains(i.ID))
                .Select(i => new { i.ID, i.Name, i.Size })
                .ToListAsync();
            imageInfo.ForEach(image =>
            {
                images[image.ID].Add("Name", image.Name);
                images[image.ID].Add("Size", (image.Size / (1024 * 1024)).ToString("F2") + " MB");
            });

            var result = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>() {
                { "Subfolders", subfolders },
                { "Images", images },
            };
            return Json(new { success = true, result });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUsersForSharing(string folderID)
    {
        try
        {
            string? username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return Json(new { success = false, message = "User not logged in"});
            }

            var sharedWithFolder = await _context.Folders
                .Where(f => f.ID == folderID)
                .Select(f => new { f.Owner, f.SharedWith })
                .FirstAsync();
            if (sharedWithFolder is null)
            {
                return Json(new { success = false, message = $"Folder not in database: {folderID}" });
            }

            if (sharedWithFolder.Owner != username)
            {
                return Json(new { success = false, message = $"Only Owner: {sharedWithFolder.Owner} can share this folder"});
            }

            var usersSharedInfo = await _context.Users
                .Where(u => u.Username != username)
                .Select(u => new { u.Username, isShared = sharedWithFolder.SharedWith.Contains(u.Username)})
                .ToListAsync();

            Dictionary<string, bool> result = usersSharedInfo.ToDictionary(
                a => a.Username,
                a => a.isShared
            );

            return Json(new { success = true, result });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ShareFolderWithUser(string folderID, string otherUsername)
    {
        try {
            string? username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            DBUser? dBUser = await _context.Users.FindAsync(otherUsername);
            if (dBUser is null)
            {
                return Json(new { success = false, message = "User to share with not in database" });
            }

            DBFolder? dBFolder = await _context.Folders.FindAsync(folderID);
            if (dBFolder is null)
            {
                return Json(new { success = false, message = "Folder to share is not in database" });
            }

            DBFolder? usersSharedWithMeFolder = await _context.Folders.FindAsync(dBUser.SharedWithMeID);
            if (usersSharedWithMeFolder is null)
            {
                return Json(new { success = false, message = "User does not have a Shared With Me folder"});
            }
            
            if (dBFolder.SharedWith.Contains(dBUser.Username) ||
                usersSharedWithMeFolder.Subfolders.Contains(dBFolder.ID))
            {
                return Json(new { success = false, message = "User to share with is already shared with" });
            }

            usersSharedWithMeFolder.Subfolders.Add(dBFolder.ID);
            dBFolder.SharedWith.Add(dBUser.Username);
            _context.Folders.UpdateRange(usersSharedWithMeFolder, dBFolder);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UnshareFolderWithUser(string folderID, string otherUsername)
    {
        try {
            string? username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            DBUser? dBUser = await _context.Users.FindAsync(otherUsername);
            if (dBUser is null)
            {
                return Json(new { success = false, message = "User to unshare with not in database" });
            }

            DBFolder? dBFolder = await _context.Folders.FindAsync(folderID);
            if (dBFolder is null)
            {
                return Json(new { success = false, message = "Folder to unshare is not in database" });
            }

            DBFolder? usersSharedWithMeFolder = await _context.Folders.FindAsync(dBUser.SharedWithMeID);
            if (usersSharedWithMeFolder is null)
            {
                return Json(new { success = false, message = "User does not have a Shared With Me folder"});
            }

            if (!dBFolder.SharedWith.Contains(dBUser.Username) &&
                !usersSharedWithMeFolder.Subfolders.Contains(dBFolder.ID))
            {
                return Json(new { success = false, message = "User to unshare with hasn't been shared with" });
            }

            usersSharedWithMeFolder.Subfolders.Remove(dBFolder.ID);
            dBFolder.SharedWith.Remove(dBUser.Username);
            _context.Folders.UpdateRange(usersSharedWithMeFolder, dBFolder);
            await _context.SaveChangesAsync();
            
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
