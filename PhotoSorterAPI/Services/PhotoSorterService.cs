using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoSorterAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PhotoSorterAPI.Services
{
    public interface IPhotoSorterService
    {
        void RunImport();
    }

    public class PhotoSorterService : IPhotoSorterService
    {
        private readonly ILogger<PhotoSorterService> _logger;

        private readonly AppConfigs _configs;
        private static List<string> videoExts { get; set; }
        private readonly char dirDel = Path.DirectorySeparatorChar;


        public PhotoSorterService(ILogger<PhotoSorterService> logger, IOptions<AppConfigs> configs)
        {
            _logger = logger;
            _configs = configs.Value;
            videoExts = _configs.KnownVideoExtensions.Split(',').ToList();
        }

        public void RunImport()
        {
            foreach (string importDir in _configs.ImportDirs) {
                if (Directory.Exists(importDir))
                {
                    if (!Directory.Exists(_configs.PicDestinationDir))
                    {
                        _logger.LogInformation($"Creating directory: {_configs.PicDestinationDir}");
                        Directory.CreateDirectory(_configs.PicDestinationDir);
                    }

                    if (!Directory.Exists(_configs.VideoDestinationDir))
                    {
                        _logger.LogInformation($"Creating directory: {_configs.VideoDestinationDir}");
                        Directory.CreateDirectory(_configs.VideoDestinationDir);
                    }

                    _logger.LogInformation($"Getting pictures in {importDir}");
                    FileInfo[] Pictures = (from fi in new DirectoryInfo(importDir).GetFiles("*.*", SearchOption.AllDirectories)
                                           where !videoExts.Contains(fi.Extension.ToLower())
                                           select fi)
                                                .ToArray();
                    _logger.LogInformation($"{Pictures.Length.ToString()} pictures found.");

                    _logger.LogInformation($"Getting videos in {importDir}");
                    FileInfo[] Videos = (from fi in new DirectoryInfo(importDir).GetFiles("*.*", SearchOption.AllDirectories)
                                         where videoExts.Contains(fi.Extension.ToLower())
                                         select fi)
                                                .ToArray();
                    _logger.LogInformation($"{Videos.Length.ToString()} videos found.");

                    List<string> moveErrors = new List<string>();
                    List<string> uploadErrors = new List<string>();

                    foreach (FileInfo pictureFile in Pictures)
                    {
                        ProcessPicture(pictureFile, ref moveErrors, ref uploadErrors);
                    }

                    foreach (FileInfo videoFile in Videos)
                    {
                        ProcessVideo(videoFile, ref moveErrors);
                    }

                    CleanUp();
                }
                else
                {
                    _logger.LogWarning($"Import directory {importDir} does not exist for user {Environment.UserName}. Check your settings.");
                }
            }
        }

        void CleanUp()
        {
            _logger.LogInformation("Starting Cleanup...");
            foreach (string importDir in _configs.ImportDirs)
            {
                DirectoryInfo[] subDirs = (from di in new DirectoryInfo(importDir).GetDirectories("*.*", SearchOption.AllDirectories)
                                           where (Directory.EnumerateFileSystemEntries(di.FullName).Any())
                                           select di)
                                                 .ToArray();

                if (subDirs.Length > 0)
                {
                    _logger.LogInformation($"Cleanup up {subDirs.Length.ToString()} directories...");

                    foreach (DirectoryInfo dir in subDirs)
                    {
                        try
                        {
                            if (File.Exists($"{dir.FullName}{dirDel}Thumbs.db"))
                            {
                                _logger.LogDebug("Deleting Thumbs.db");
                                File.Delete($"{dir.FullName}{dirDel}Thumbs.db");
                            }
                            if (File.Exists($"{dir.FullName}{dirDel}desktop.ini"))
                            {
                                _logger.LogDebug("Deleting desktop.ini");
                                File.Delete($"{dir.FullName}{dirDel}desktop.ini");
                            }
                            _logger.LogDebug($"Deleting {dir.FullName}");
                            Directory.Delete(dir.FullName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInformation(ex.Message);
                        }
                    }
                    _logger.LogInformation("Finished cleaning up.");
                }
                else
                {
                    _logger.LogInformation("Nothing to cleanup.");
                }
            }
        }

        private void ProcessPicture(FileInfo pictureFile, ref List<string> moveErrors, ref List<string> uploadErrors)
        {
            if (pictureFile.Name == "Thumbs.db" || pictureFile.Name == "Readme.md" || pictureFile.Extension == ".ini")
            {
                return;   //skip
            }
            _logger.LogDebug($"Processing picture {pictureFile.Name}...");

            var image = new MagickImage(pictureFile.FullName);
            
            DateTime pictureDate = GetPictureDate(image);

            StringBuilder newFileName = new StringBuilder();
            newFileName.Append(_configs.FileNamePrefix);
            newFileName.Append(pictureDate.ToString("yyyyMMdd_HHmmss"));
            string ext = pictureFile.Extension.ToLower();
            string moveToPath = CreatePicDirStructure(pictureDate);

            AutoRotate(image);

            if (_configs.FileNameUseCameraModel)
            {
                string cameraModel = GetCameraModel(image).Replace(" ","_");
                if (cameraModel != "")
                {
                    newFileName.Append("_");
                    newFileName.Append(cameraModel);
                }
            }

            newFileName.Append(_configs.FileNameSuffix);
            _logger.LogDebug($"New picture name is {newFileName}");

            string result;
            //Attempt to move
            if (moveToPath != "Error")
            {
                try
                {
                    result = MovePicture(pictureFile.FullName, moveToPath, newFileName.ToString(), ext);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    result = "Error";
                }

                if (result == "Error")
                {
                    moveErrors.Add(pictureFile.Name);
                    MoveToManualFolder(pictureFile.FullName, pictureFile.Directory.FullName, newFileName.ToString(), ext);
                }
            }
            else
            {
                moveErrors.Add(pictureFile.Name);
                MoveToManualFolder(pictureFile.FullName, pictureFile.Directory.FullName, newFileName.ToString(), ext);
            }

            result = $"Successfully moved file {pictureFile.Name} to {moveToPath} as {newFileName.ToString()}{ext}";
            _logger.LogInformation(result);
        }

        private DateTime GetPictureDate(MagickImage image)
        {
            _logger.LogDebug("Attempting to get picture taken date.");
            //Set Date
            DateTime? pictureDate = null;

            //Get Date Taken
            IExifProfile profile = image.GetExifProfile();
            if (!(profile is null))
            {
                IExifValue dateTakeExif = profile.GetValue(ExifTag.DateTimeDigitized);
                if (dateTakeExif is not null)
                {
                    pictureDate = DateTime.ParseExact(dateTakeExif.GetValue().ToString().TrimEnd('\0'), "yyyy:MM:dd HH:mm:ss", null);
                    _logger.LogDebug($"The picture taken date is {pictureDate}");
                }
                else
                {
                    _logger.LogDebug("The picture taken value is null");
                }
            }
            else
            {
                _logger.LogDebug("Could not get image exif profile.");
            }
            //Get File Creation Date
            if (pictureDate is null)
            {
                pictureDate = File.GetCreationTime(image.FileName);
            }
            //Default to Today
            if (pictureDate is null)
            {
                pictureDate = DateTime.Now;
            }

            return pictureDate.Value;
        }

        private void AutoRotate(MagickImage image)
        {
            IExifProfile profile = image.GetExifProfile();
            if (!(profile is null))
            {
                string orientation = profile.GetValue(ExifTag.Orientation)?.ToString() ?? "";
                if (!String.IsNullOrWhiteSpace(orientation))
                {
                    _logger.LogDebug($"Orientation is {orientation}");
                    switch (orientation)
                    {
                        case "2":
                            _logger.LogDebug($"Flopping image");
                            image.Flop(); //x-axis
                            break;
                        case "3":
                            _logger.LogDebug($"Rotating image 180 degrees");
                            image.Rotate(180);
                            break;
                        case "4":
                            _logger.LogDebug($"Rotating image 180 degrees and flopping");
                            image.Rotate(180);
                            image.Flop(); //x-axis
                            break;
                        case "5":
                            _logger.LogDebug($"Rotating image 90 degrees and flopping");
                            image.Rotate(90);
                            image.Flop(); //x-axis
                            break;
                        case "6":
                            _logger.LogDebug($"Rotating image 90 degrees");
                            image.Rotate(90);
                            break;
                        case "7":
                            _logger.LogDebug($"Rotating image 270 degrees and flopping");
                            image.Rotate(270);
                            image.Flop(); //x-axis
                            break;
                        case "8":
                            _logger.LogDebug($"Rotating image 270 degrees");
                            image.Rotate(270);
                            break;
                        case "1":
                        case "1H":
                        default:
                            _logger.LogDebug($"No rotating needed");
                            break;
                    }
                    ushort defaultOrient = 1;
                    _logger.LogDebug($"Resetting the default orientation of the image.");
                    profile.SetValue(ExifTag.Orientation, defaultOrient);
                    _logger.LogDebug($"Saving the image modifications.");
                    image.Write(image.FileName);
                }
            }
            else
            {
                _logger.LogInformation("Could not get exif profile.");
            }
        }

        private void ProcessVideo(FileInfo videoFile, ref List<string> moveErrors)
        {
            _logger.LogDebug($"Processing video {videoFile.Name}...");
            DateTime myDateTaken = videoFile.CreationTime;
            string ext = videoFile.Extension.ToLower();
            string basename = Path.GetFileNameWithoutExtension(videoFile.FullName);

            string moveToPath = CreateVidDirStructure(ref myDateTaken);


            //Attempt to move
            if (moveToPath != "Error")
            {
                try
                {
                    string result = MoveVideo(videoFile.FullName, moveToPath, basename, ext);
                    if (result == "Error")
                    {
                        moveErrors.Add(videoFile.Name);
                        MoveToManualFolder(videoFile.FullName, videoFile.Directory.FullName, basename, ext);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    moveErrors.Add(videoFile.Name);
                    MoveToManualFolder(videoFile.FullName, videoFile.Directory.FullName, basename, ext);
                }
            }
            else
            {
                moveErrors.Add(videoFile.Name);
                MoveToManualFolder(videoFile.FullName, videoFile.Directory.FullName, basename, ext);
            }
        }

        private string CreatePicDirStructure(DateTime dt)
        {
            string year = dt.Year.ToString();
            string monthNum = dt.Month.ToString().PadLeft(2, '0');
            string monAbbrv = dt.ToString("MMM");

            try
            {
                string yearPath = $"{_configs.PicDestinationDir}{dirDel}{year}";
                string monthPath = $"{yearPath}{dirDel}{monthNum}_{monAbbrv}";
                if (!Directory.Exists(yearPath))
                {
                    _logger.LogInformation($"Creating directory {yearPath}");
                    Directory.CreateDirectory(yearPath);
                }
                if (!Directory.Exists(monthPath))
                {
                    _logger.LogInformation($"Creating directory {monthPath}");
                    Directory.CreateDirectory(monthPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return "Error";
            }

            return $"{_configs.PicDestinationDir}{dirDel}{year}{dirDel}{monthNum}_{monAbbrv}";
        }

        private string CreateVidDirStructure(ref DateTime dt)
        {
            string year = dt.Year.ToString();
            string vidPath = $"{_configs.VideoDestinationDir}{dirDel}{year}";
            try
            {
                if (!Directory.Exists(vidPath))
                {
                    _logger.LogInformation($"Creating directory {vidPath}");
                    Directory.CreateDirectory(vidPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return "Error";
            }

            return vidPath;
        }

        private string MoveVideo(string fromFilePath, string toPath, string fileName, string ext)
        {
            string path = Path.Combine(toPath, $"{fileName}{ext}");
            if (!File.Exists(path))
            {
                _logger.LogDebug($"Attempting to move {fromFilePath} to {path}");
                File.Move(fromFilePath, path);
            }
            else
            {
                int i;
                for (i = 1; i < 10; i++)
                {
                    if (!File.Exists(Path.Combine(toPath, $"{fileName}_{i}{ext}")))
                    {
                        _logger.LogDebug($"Attempting to move from {fromFilePath} to {Path.Combine(toPath, $"{fileName}_{i}{ext}")}");
                        File.Move(fromFilePath, Path.Combine(toPath, $"{fileName}_{i}{ext}"));
                        break;
                    }
                }
                if (i >= 10)
                {
                    _logger.LogError($"Could not find a unique name for {fileName}{ext}");
                    return "Error";
                }
            }
            return "Success";
        }

        private string GetCameraModel(MagickImage image)
        {
            string cameraModel = "", model, make;
            IExifProfile profile = image.GetExifProfile();
            if (profile is not null)
            {
                model = profile.GetValue(ExifTag.Model)?.ToString() ?? "";
                _logger.LogDebug($"Camera model is {model}");
                make = profile.GetValue(ExifTag.Make)?.ToString() ?? "";
                _logger.LogDebug($"Camera make is {make}");

                if (!String.IsNullOrWhiteSpace(model))
                {
                    cameraModel = model.TrimEnd('\0').Trim();
                    _logger.LogInformation($"Setting the camera model to {cameraModel}");
                }
                else
                {
                    if (!String.IsNullOrWhiteSpace(make))
                    {
                        cameraModel = make.TrimEnd('\0').Trim();
                        _logger.LogInformation($"Setting the camera model to {cameraModel}");
                    }
                    else
                    {
                        _logger.LogInformation("Could not determine the camera make or model.");
                    }
                } 
            }
            else
            {
                _logger.LogDebug("Could not get the exif profile for the image.");
            }
            return cameraModel;
        }

        private void MoveToManualFolder(string fromFilePath, string moveToPath, string fileName, string ext)
        {
            
            moveToPath = Path.Combine(moveToPath, "ManualMove");
            _logger.LogInformation($"Moving {fromFilePath} to the {moveToPath}.");

            if (!Directory.Exists(moveToPath))
            {
                _logger.LogInformation($"Creating {moveToPath}");
                Directory.CreateDirectory(moveToPath);
            }

            string baseFileName = Path.Combine(moveToPath, fileName);

            if (!File.Exists($"{baseFileName}{ext}"))
            {
                _logger.LogDebug($"Attempting to move from {fromFilePath} to {baseFileName}{ext}");
                File.Move(fromFilePath, $"{baseFileName}{ext}");
            }
            else
            {
                int i;
                for (i = 1; i < 10; i++)
                {
                    if (!File.Exists($"{baseFileName}_{i}{ext}"))
                    {
                        _logger.LogDebug($"Attempting to move from {fromFilePath} to {baseFileName}_{i}{ext}");
                        File.Move(fromFilePath, $"{baseFileName}_{i}{ext}");
                        break;
                    }
                }
                if (i >= 10)
                {
                    _logger.LogDebug($"Attempting to move from {fromFilePath} to {baseFileName}_{DateTime.Now.ToString("MMddyyyyThhmmssffftt")}{ext}");
                    File.Move(fromFilePath, $"{baseFileName}_{DateTime.Now.ToString("MMddyyyyThhmmssffftt")}{ext}");
                }
            }
        }

        private string MovePicture(string fromFilePath, string toPath, string fileName, string ext)
        {
            string defaultPath = Path.Combine(toPath, fileName + ext);
            if (!File.Exists(defaultPath))
            {
                _logger.LogDebug($"Attempting to move from {fromFilePath} to {defaultPath}");
                File.Move(fromFilePath, defaultPath);
            }
            else
            {
                _logger.LogDebug($"{defaultPath} already exists so adding a numeric suffix.");
                int i;
                for (i = 1; i < 10; i++)
                {
                    if (!File.Exists(Path.Combine(toPath,$"{fileName}_{i}{ext}")))
                    {
                        _logger.LogDebug($"Attempting to move from {fromFilePath} to {Path.Combine(toPath, $"{fileName}_{i}{ext}")}");
                        File.Move(fromFilePath, Path.Combine(toPath, $"{fileName}_{i}{ext}"));
                        break;
                    }
                }
                if (i >= 10)
                {
                    _logger.LogError($"Could not find a unique name for {fileName}{ext}");
                    return "Error";
                }
            }

            return "Success";
        }
    }
}
