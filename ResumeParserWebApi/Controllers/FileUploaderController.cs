using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Azure.Storage.Blobs;
namespace ResumeParserWebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploaderController : ControllerBase
    {
        private readonly ILogger<FileUploaderController> _logger;
        private readonly IConfiguration _configuration;
        private readonly BlobServiceClient _blobServiceClient;
        public FileUploaderController(ILogger<FileUploaderController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Get the connection string from configuration
            var azureConnectionString = _configuration.GetSection("AzureBlobStorage")["ConnectionString"];
            _blobServiceClient = new BlobServiceClient(azureConnectionString);
        }
        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile([FromForm] List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
            {
                return BadRequest("No files uploaded.");
            }
           
            var containerClient = _blobServiceClient.GetBlobContainerClient("allresumestorage");
            await containerClient.CreateIfNotExistsAsync();
            var uploadedFiles = new List<object>();

            foreach (var file in files)
            {
                var blobClient = containerClient.GetBlobClient(file.FileName);
                await blobClient.UploadAsync(file.OpenReadStream(), true);
                uploadedFiles.Add(new { FileName = file.FileName, BlobUrl = blobClient.Uri });
            }

            return Ok(uploadedFiles);
        }
    }
}
