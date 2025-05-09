using System.ComponentModel.DataAnnotations;

namespace JonahsImageServer.Models
{
    public class DBUser
    {
        [Key]
        public required string ID { get; set; }
        public required string Username { get; set; }
        public required string PasswordHash { get; set; }
        public required string RootID { get; set; }
        public required string SharedWithMeID { get; set; }
    }

    public class DBFolder
    {
        [Key]
        public required string ID { get; set; }
        public required string Name { get; set; }
        public required string OwnerID { get; set; }
        public required double Size { get; set; } = 0.0;
        public string? ParentFolderID { get; set; }
        public List<string> Subfolders { get; set; } = [];
        public List<string> Images { get; set; } = [];
        public List<string> SharedWith { get; set; } = [];
    }

    public class DBImage
    {
        [Key]
        public required string ID { get; set; }
        public required string Name { get; set; }
        public required double Size { get; set; }
    }
}
