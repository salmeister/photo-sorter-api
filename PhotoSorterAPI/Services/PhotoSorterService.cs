using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoSorterAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const string ManualMoveDirName = "ManualMove";
        private const string DefaultVideoExtensions = ".mp4,.m4v,.avi,.mov,.mpg,.wmv,.webm";
        private const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";
        private const int MaxCameraModelLength = 64;
        private const int MaxDuplicateSuffix = 10;

        private static readonly HashSet<string> SkippedFileNames = new(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", "Readme.md", ".DS_Store" };
        private static readonly HashSet<string> JunkFileNames = new(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", "desktop.ini", ".DS_Store" };

        // Only formats that can be losslessly-enough re-encoded in place. HEIC/AVIF orientation is applied by the container (irot) so they are never rewritten.
        private static readonly HashSet<MagickFormat> RotatableFormats = [MagickFormat.Jpeg, MagickFormat.Jpg, MagickFormat.Png, MagickFormat.Tiff, MagickFormat.Tif, MagickFormat.WebP];

        private static readonly char[] UnsafeFileNameChars = [.. Path.GetInvalidFileNameChars().Union(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])];

        private readonly ILogger<PhotoSorterService> _logger;
        private readonly AppConfigs _configs;
        private readonly HashSet<string> _videoExts;
        private readonly string[] _excludedSubDirNames;

        public PhotoSorterService(ILogger<PhotoSorterService> logger, IOptions<AppConfigs> configs)
        {
            _logger = logger;
            _configs = configs.Value;

            string videoExtensions = string.IsNullOrWhiteSpace(_configs.KnownVideoExtensions) ? DefaultVideoExtensions : _configs.KnownVideoExtensions;
            _videoExts = videoExtensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith('.') ? e : $".{e}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // ManualMove is always excluded so failed files are not re-processed on every run.
            _excludedSubDirNames = (_configs.ExcludeImportSubDirNames ?? [])
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Append(ManualMoveDirName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void RunImport()
        {
            foreach (string importDir in _configs.ImportDirs ?? [])
            {
                if (!Directory.Exists(importDir))
                {
                    _logger.LogWarning("Import directory {ImportDir} does not exist for user {User}. Check your settings.", importDir, Environment.UserName);
                    continue;
                }

                EnsureDirectory(_configs.PicDestinationDir);
                EnsureDirectory(_configs.VideoDestinationDir);

                _logger.LogInformation("Excluding import subdirectories matching: {Excluded}", string.Join(", ", _excludedSubDirNames));
                _logger.LogInformation("Getting pictures and videos in {ImportDir}", importDir);

                FileInfo[] files = new DirectoryInfo(importDir)
                    .GetFiles("*", SearchOption.AllDirectories)
                    .Where(fi => !IsExcluded(importDir, fi.DirectoryName))
                    .ToArray();

                FileInfo[] videos = files.Where(fi => _videoExts.Contains(fi.Extension)).ToArray();
                FileInfo[] pictures = files.Except(videos).ToArray();
                _logger.LogInformation("{PictureCount} pictures found.", pictures.Length);
                _logger.LogInformation("{VideoCount} videos found.", videos.Length);

                foreach (FileInfo pictureFile in pictures)
                {
                    ProcessPicture(pictureFile);
                }

                foreach (FileInfo videoFile in videos)
                {
                    ProcessVideo(videoFile);
                }

                CleanUp(importDir);
            }
        }

        private bool IsExcluded(string importDir, string directory)
        {
            // Compare relative to the import root so the root path itself never triggers an exclusion.
            string relative = Path.GetRelativePath(importDir, directory);
            if (relative == ".")
                return false;

            return _excludedSubDirNames.Any(name => relative.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        private void CleanUp(string importDir)
        {
            _logger.LogInformation("Starting cleanup of {ImportDir}...", importDir);

            // Deepest directories first so parents become empty once their children are removed.
            DirectoryInfo[] subDirs = new DirectoryInfo(importDir)
                .GetDirectories("*", SearchOption.AllDirectories)
                .Where(di => !IsExcluded(importDir, di.FullName))
                .OrderByDescending(di => di.FullName.Length)
                .ToArray();

            int removed = 0;
            foreach (DirectoryInfo dir in subDirs)
            {
                try
                {
                    string[] entries = Directory.GetFileSystemEntries(dir.FullName);
                    if (!entries.All(e => File.Exists(e) && JunkFileNames.Contains(Path.GetFileName(e))))
                        continue;

                    foreach (string junk in entries)
                    {
                        _logger.LogDebug("Deleting {File}", junk);
                        File.Delete(junk);
                    }

                    _logger.LogDebug("Deleting {Directory}", dir.FullName);
                    Directory.Delete(dir.FullName);
                    removed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not clean up {Directory}", dir.FullName);
                }
            }

            _logger.LogInformation(removed > 0 ? "Removed {Count} empty directories." : "Nothing to cleanup.", removed);
        }

        private void ProcessPicture(FileInfo pictureFile)
        {
            if (SkippedFileNames.Contains(pictureFile.Name) || pictureFile.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))
                return;

            _logger.LogDebug("Processing picture {File}...", pictureFile.FullName);
            string ext = pictureFile.Extension.ToLowerInvariant();
            string newFileName = Path.GetFileNameWithoutExtension(pictureFile.Name);

            try
            {
                DateTime pictureDate;
                using (var image = new MagickImage(pictureFile.FullName))
                {
                    pictureDate = GetPictureDate(image, pictureFile);

                    var nameBuilder = new StringBuilder();
                    nameBuilder.Append(_configs.FileNamePrefix);
                    nameBuilder.Append(pictureDate.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                    if (_configs.FileNameUseCameraModel)
                    {
                        string cameraModel = SanitizeFileNamePart(GetCameraModel(image));
                        if (cameraModel != "")
                        {
                            nameBuilder.Append('_');
                            nameBuilder.Append(cameraModel);
                        }
                    }
                    nameBuilder.Append(_configs.FileNameSuffix);
                    newFileName = nameBuilder.ToString();
                    _logger.LogDebug("New picture name is {FileName}", newFileName);

                    AutoRotate(image, pictureFile.FullName);
                }

                string moveToPath = CreatePicDirStructure(pictureDate);
                if (moveToPath is not null && MoveToUniquePath(pictureFile.FullName, moveToPath, newFileName, ext))
                {
                    _logger.LogInformation("Successfully moved file {File} to {Destination} as {FileName}{Ext}", pictureFile.Name, moveToPath, newFileName, ext);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process picture {File}", pictureFile.FullName);
            }

            MoveToManualFolder(pictureFile, newFileName, ext);
        }

        private DateTime GetPictureDate(MagickImage image, FileInfo file)
        {
            IExifProfile profile = image.GetExifProfile();
            if (profile is not null)
            {
                foreach (ExifTag<string> tag in new[] { ExifTag.DateTimeOriginal, ExifTag.DateTimeDigitized, ExifTag.DateTime })
                {
                    string value = profile.GetValue(tag)?.Value?.TrimEnd('\0').Trim();
                    if (DateTime.TryParseExact(value, ExifDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                    {
                        _logger.LogDebug("The picture taken date is {Date} (from {Tag})", date, tag);
                        return date;
                    }
                }
                _logger.LogDebug("No usable EXIF date found.");
            }
            else
            {
                _logger.LogDebug("Could not get image exif profile.");
            }

            return GetFileDate(file);
        }

        private static DateTime GetFileDate(FileInfo file)
        {
            // Copies often get a new creation time but keep the original modified time, so use whichever is older.
            return file.CreationTime < file.LastWriteTime ? file.CreationTime : file.LastWriteTime;
        }

        private void AutoRotate(MagickImage image, string filePath)
        {
            ushort? orientation = image.GetExifProfile()?.GetValue(ExifTag.Orientation)?.Value;
            if (orientation is null or <= 1 or > 8)
            {
                _logger.LogDebug("No rotating needed");
                return;
            }

            if (!RotatableFormats.Contains(image.Format))
            {
                _logger.LogDebug("Skipping rotation for {Format}; the file is left unmodified.", image.Format);
                return;
            }

            using (var frames = new MagickImageCollection())
            {
                frames.Ping(filePath);
                if (frames.Count > 1)
                {
                    _logger.LogDebug("Skipping rotation for multi-frame image {File}", filePath);
                    return;
                }
            }

            _logger.LogDebug("Orientation is {Orientation}; auto-orienting image.", orientation);
            image.Orientation = (Orientation)orientation.Value;
            image.AutoOrient();

            IExifProfile profile = image.GetExifProfile();
            if (profile is not null)
            {
                profile.SetValue(ExifTag.Orientation, (ushort)1);
                image.SetProfile(profile);
            }

            // Write to a temp file first so a failed encode never corrupts the original.
            string tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                image.Write(tempPath, image.Format);
                File.Move(tempPath, filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not rotate {File}; leaving it unmodified.", filePath);
                File.Delete(tempPath);
            }
        }

        private void ProcessVideo(FileInfo videoFile)
        {
            _logger.LogDebug("Processing video {File}...", videoFile.FullName);
            string ext = videoFile.Extension.ToLowerInvariant();
            string basename = Path.GetFileNameWithoutExtension(videoFile.Name);

            try
            {
                string moveToPath = CreateVidDirStructure(GetFileDate(videoFile));
                if (moveToPath is not null && MoveToUniquePath(videoFile.FullName, moveToPath, basename, ext))
                {
                    _logger.LogInformation("Successfully moved file {File} to {Destination}", videoFile.Name, moveToPath);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process video {File}", videoFile.FullName);
            }

            MoveToManualFolder(videoFile, basename, ext);
        }

        private string CreatePicDirStructure(DateTime dt)
        {
            string monthDir = $"{dt.Month:00}_{dt.ToString("MMM", CultureInfo.CurrentCulture)}";
            string monthPath = Path.Combine(_configs.PicDestinationDir, dt.Year.ToString(CultureInfo.InvariantCulture), monthDir);
            return EnsureDirectory(monthPath) ? monthPath : null;
        }

        private string CreateVidDirStructure(DateTime dt)
        {
            string vidPath = Path.Combine(_configs.VideoDestinationDir, dt.Year.ToString(CultureInfo.InvariantCulture));
            return EnsureDirectory(vidPath) ? vidPath : null;
        }

        private bool EnsureDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogInformation("Creating directory {Directory}", path);
                    Directory.CreateDirectory(path);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not create directory {Directory}", path);
                return false;
            }
        }

        private bool MoveToUniquePath(string fromFilePath, string toPath, string fileName, string ext)
        {
            for (int i = 0; i < MaxDuplicateSuffix; i++)
            {
                string candidate = Path.Combine(toPath, i == 0 ? $"{fileName}{ext}" : $"{fileName}_{i}{ext}");
                if (File.Exists(candidate))
                    continue;

                _logger.LogDebug("Attempting to move {From} to {To}", fromFilePath, candidate);
                File.Move(fromFilePath, candidate, overwrite: false);
                return true;
            }

            _logger.LogError("Could not find a unique name for {FileName}{Ext} in {Directory}", fileName, ext, toPath);
            return false;
        }

        private string GetCameraModel(MagickImage image)
        {
            IExifProfile profile = image.GetExifProfile();
            if (profile is null)
            {
                _logger.LogDebug("Could not get the exif profile for the image.");
                return "";
            }

            string model = profile.GetValue(ExifTag.Model)?.Value?.TrimEnd('\0').Trim();
            string make = profile.GetValue(ExifTag.Make)?.Value?.TrimEnd('\0').Trim();
            _logger.LogDebug("Camera make is {Make}, model is {Model}", make, model);

            string cameraModel = !string.IsNullOrWhiteSpace(model) ? model : make ?? "";
            if (cameraModel == "")
                _logger.LogInformation("Could not determine the camera make or model.");

            return cameraModel;
        }

        // EXIF values are untrusted input; strip anything that could alter the destination path.
        private static string SanitizeFileNamePart(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(c == ' ' || char.IsControl(c) || UnsafeFileNameChars.Contains(c) ? '_' : c);
            }

            string result = sb.ToString().Trim('.', '_');
            return result.Length > MaxCameraModelLength ? result[..MaxCameraModelLength] : result;
        }

        private void MoveToManualFolder(FileInfo file, string fileName, string ext)
        {
            try
            {
                string moveToPath = Path.Combine(file.DirectoryName, ManualMoveDirName);
                _logger.LogInformation("Moving {File} to {Destination}.", file.FullName, moveToPath);
                if (!EnsureDirectory(moveToPath))
                    return;

                if (!MoveToUniquePath(file.FullName, moveToPath, fileName, ext))
                {
                    string timestamped = Path.Combine(moveToPath, $"{fileName}_{DateTime.Now.ToString("MMddyyyyThhmmssffftt", CultureInfo.InvariantCulture)}{ext}");
                    _logger.LogDebug("Attempting to move {From} to {To}", file.FullName, timestamped);
                    File.Move(file.FullName, timestamped, overwrite: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not move {File} to the manual folder.", file.FullName);
            }
        }
    }
}
