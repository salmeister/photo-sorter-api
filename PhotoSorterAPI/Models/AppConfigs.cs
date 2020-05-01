using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoSorterAPI.Models
{
    public class AppConfigs
    {
        public string ImportDir { get; set; }
        public string PicDestinationDir { get; set; }
        public string VideoDestinationDir { get; set; }
        public string FileNamePrefix { get; set; }
        public string FileNameSuffix { get; set; }
        public bool ShutterflyUpload { get; set; }
        public bool FileNameUseCameraModel { get; set; }
        public string KnownVideoExtensions { get; set; }
    }
}
