namespace JonahsImageServer.Models
{
    public class HomeViewModel
    {
        public required string UserID { get; set; }
        public required List<DBFolder> FolderPath { get; set; }
        public required DBFolder CurrFolder { get; set; }
        public required string CurrFolderSizeDisplay { get; set; }
        public required string CurrFolderOwnerName { get; set; }
    }
}
