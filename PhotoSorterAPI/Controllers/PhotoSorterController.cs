using System;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PhotoSorterAPI.Services;

namespace PhotoSorterAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PhotoSorterController(IPhotoSorterService photoSorterServ, ILogger<PhotoSorterController> logger) : ControllerBase
    {
        private readonly ILogger<PhotoSorterController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Prevents overlapping runs from racing on the same files.
        private static readonly SemaphoreSlim RunLock = new(1, 1);

        /// <summary>
        /// Run Photo Sorter to import and sort photos
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public string Get()
        {
            if (!RunLock.Wait(0))
            {
                _logger.LogWarning("An import is already running; ignoring this request.");
                return "false";
            }

            string result = "false";
            try
            {
                _logger.LogInformation("######################### NEW EXECUTION STARTED  ######################### ");
                photoSorterServ.RunImport();
                result = "true";
                _logger.LogInformation("Returning result: {Result}", result);
                _logger.LogInformation("#########################   EXECUTION COMPLETE   ######################### ");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import failed.");
            }
            finally
            {
                RunLock.Release();
            }

            return result;
        }
    }
}