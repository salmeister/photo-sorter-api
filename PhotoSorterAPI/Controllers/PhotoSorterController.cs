using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PhotoSorterAPI.Services;

namespace PhotoSorterAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PhotoSorterController : ControllerBase
    {
        private readonly IPhotoSorterService _photoSorterServ;
        private readonly ILogger<PhotoSorterController> _logger;

        public PhotoSorterController(IPhotoSorterService photoSorterServ, ILogger<PhotoSorterController> logger)
        {
            _photoSorterServ = photoSorterServ;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

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
                _photoSorterServ.RunImport();
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