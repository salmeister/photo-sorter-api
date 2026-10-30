# Photo Sorter
Photo Sorter is a tool designed to help you organize your photo collection efficiently. It scans through your photos, reads their metadata, and sorts them into folders based on the date they were taken. This project aims to simplify the process of managing large photo libraries.

## Features
- Automatically sorts photos into `<PicDestinationDir>/<yyyy>/<MM_Mon>` folders and videos into `<VideoDestinationDir>/<yyyy>`
- Renames photos to `<prefix>yyyyMMdd_HHmmss[_CameraModel]<suffix>` using the EXIF date taken (falls back to the file date)
- Supports JPEG, PNG, TIFF, GIF, WebP, HEIC/HEIF, AVIF, and other formats readable by ImageMagick
- Auto-rotates JPEG, PNG, TIFF, and still WebP images based on EXIF orientation (HEIC/AVIF are left unmodified since viewers apply their rotation)
- Files that cannot be processed are moved to a `ManualMove` folder next to the original

## Configuration

Settings live in the `AppConfigs` section (e.g. `appsettings.Production.json`, or environment variables such as `AppConfigs__ImportDirs__0`):

- `ImportDirs`: Directories to scan (recursively) for new pictures and videos.
- `ExcludeImportSubDirNames`: Subdirectory names to skip within the import directories. `ManualMove` is always skipped.
- `PicDestinationDir`: Root directory for sorted pictures.
- `VideoDestinationDir`: Root directory for sorted videos.
- `FileNamePrefix` / `FileNameSuffix`: Text added before/after the generated picture name.
- `FileNameUseCameraModel`: Append the camera model (or make) to picture names.
- `KnownVideoExtensions`: Comma-separated list of extensions treated as video; everything else is treated as a picture.

```json
{
  "AppConfigs": {
    "ImportDirs": [ "/mnt/home_content/Media-Import" ],
    "ExcludeImportSubDirNames": [ "ManualMove", "Temp" ],
    "PicDestinationDir": "/mnt/home_content/Pictures",
    "VideoDestinationDir": "/mnt/home_content/Videos",
    "FileNamePrefix": "",
    "FileNameSuffix": "",
    "FileNameUseCameraModel": true,
    "KnownVideoExtensions": ".mp4, .m4v, .avi, .mov, .mpg, .wmv, .webm, .mkv, .3gp, .mts, .m2ts"
  }
}
```

## Implementation
Calling the API can be configured through a cron job on a linux system
``` 0 6 * * * /home/usr/scripts/photosorter_job.sh >/dev/null 2>&1 ```
    - Content of script:
      ``` curl -X GET "http://ipaddress:port/PhotoSorter" -H "accept: text/plain" ```