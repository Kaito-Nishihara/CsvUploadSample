using CsvHelper;
using CsvUploadSample.Entities;
using CsvUploadSample.Models;
using CsvUploadSample.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Text;

namespace CsvUploadSample.Controllers
{
    public class CancelUploadRequest
    {
        public string UploadId { get; set; }
    }
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly CsvAppDbContext _context;
        private readonly ICsvUploadService<TempCsvMaster,CsvMaster> _csvUploadService;
        private readonly ISampleService _sampleService;
        private readonly IHubContext<ProgressHub> _hubContext;

        public HomeController(ILogger<HomeController> logger, CsvAppDbContext dbContext, ICsvUploadService<TempCsvMaster,CsvMaster> csvUploadService, IHubContext<ProgressHub> hubContext, ISampleService sampleService)
        {
            _logger = logger;
            _context = dbContext;
            _csvUploadService = csvUploadService;
            _hubContext = hubContext;
            _sampleService = sampleService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult GenerateUploadId()
        {
            var uploadId = Guid.NewGuid().ToString();
            return Ok(new { uploadId });
        }


        [HttpGet]
        public IActionResult FileUpload()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> FileUpload(IFormFile file, [FromForm] string uploadId)
        {
            /*using var transaction = await _context.Database.BeginTransactionAsync(); // �g�����U�N�V�����̊J�n
            try
            {               
                if (file == null || file.Length == 0)
                {
                    return BadRequest("�t�@�C���������ł��B");
                }

                // �t�@�C�������̃��W�b�N
                await _csvUploadService.UploadAndProcessFile(file, _hubContext, HttpContext.RequestAborted, uploadId);
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", 30);

                await _csvUploadService.MigrateData(uploadId, x => x.UploadId == uploadId,_hubContext);
                await transaction.CommitAsync();
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", 100);

                return Ok("�t�@�C��������ɃA�b�v���[�h����܂����B");
            }
            catch (OperationCanceledException)
            {
                // �N���C�A���g���ŃL�����Z�������������ꍇ�̏���
                await transaction.RollbackAsync();
                return StatusCode(StatusCodes.Status499ClientClosedRequest, "�A�b�v���[�h���L�����Z������܂����B");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // ���̃G���[����
               
            }*/
            try
            {
                var result = await _sampleService.UploadCsv(file);
            }
            catch (Exception ex) 
            {
                throw new Exception(ex.Message);
            }
            return StatusCode(StatusCodes.Status500InternalServerError, "");
        }

        [HttpPost]
        public async Task<IActionResult>  CancelUpload([FromBody] CancelUploadRequest request)
        {
            try
            {

                return await Task.FromResult(Ok("�L�����Z�����������܂����B"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message); // ������UploadId�̏ꍇ
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "�L�����Z���������ɃG���[���������܂����B");
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
