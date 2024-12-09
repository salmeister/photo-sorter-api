
namespace PhotoSorterAPI.Models
{
    public class AppConfigs
    {
        public string[] ImportDirs { get; set; }
        public string[] ExcludeImportSubDirNames { get; set; }
        public string PicDestinationDir { get; set; }
        public string VideoDestinationDir { get; set; }
        public string FileNamePrefix { get; set; }
        public string FileNameSuffix { get; set; }
        public bool FileNameUseCameraModel { get; set; }
        public string KnownVideoExtensions { get; set; }
    }
}
