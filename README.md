# Photo Sorter
Photo Sorter is a tool designed to help you organize your photo collection efficiently. It scans through your photos, reads their metadata, and sorts them into folders based on the date they were taken. This project aims to simplify the process of managing large photo libraries.

## Features
- Automatically sorts photos into folders by date
- Supports various image formats
- Reads metadata to determine the date taken
- Easy to use and configure

## Configuration

There are several properties that need to be set to configure the Photo Sorter application:

- `inputDirectory`: The directory where your unsorted photos are located.
- `outputDirectory`: The directory where the sorted photos will be saved.
- `supportedFormats`: A list of image formats that the application will process (e.g., `[".jpg", ".png", ".bmp"]`).
- `dateFormat`: The format in which the date folders will be named (e.g., `yyyy-MM-dd`).
- `logLevel`: The level of logging detail (e.g., `INFO`, `DEBUG`, `ERROR`).

Example configuration in `AppConfigs`:

```json
{
    "inputDirectory": "C:/photos/unsorted",
    "outputDirectory": "C:/photos/sorted",
    "supportedFormats": [".jpg", ".png", ".bmp"],
    "dateFormat": "yyyy-MM-dd",
    "logLevel": "INFO"
}
```

## Implementation
Calling the API can be configured through a cron job on a linux system
``` 0 6 * * * /home/usr/scripts/photosorter_job.sh >/dev/null 2>&1 ```
    - Content of script:
      ``` curl -X GET "http://ipaddress:port/PhotoSorter" -H "accept: text/plain" ```