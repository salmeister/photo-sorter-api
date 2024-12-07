using System;
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

        /// <summary>
        /// Run Photo Sorter
        /// </summary>
        /// <returns>A result value</returns>
        [HttpGet]
        public string Get()
        {
            string result = "false";
            try
            {
                _logger.LogInformation("######################### NEW EXECUTION STARTED  ######################### ");
                photoSorterServ.RunImport();
                result = "true";
                _logger.LogInformation($"Returning result: {result}");
                _logger.LogInformation($"#########################   EXECUTION COMPLETE   ######################### ");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                _logger.LogError(ex.StackTrace);
            }

            return result;
        }
    }
}