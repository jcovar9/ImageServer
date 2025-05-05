namespace JonahsImageServer.Models
{
    public class HomeViewModel
    {
        public required string Username { get; set; }
        public required List<DBFolder> FolderPath { get; set; }
        public required DBFolder CurrFolder { get; set; }
    }
}
