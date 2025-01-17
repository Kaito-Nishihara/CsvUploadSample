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
            /*using var transaction = await _context.Database.BeginTransactionAsync(); // トランザクションの開始
            try
            {               
                if (file == null || file.Length == 0)
                {
                    return BadRequest("ファイルが無効です。");
                }

                // ファイル処理のロジック
                await _csvUploadService.UploadAndProcessFile(file, _hubContext, HttpContext.RequestAborted, uploadId);
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", 30);

                await _csvUploadService.MigrateData(uploadId, x => x.UploadId == uploadId,_hubContext);
                await transaction.CommitAsync();
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", 100);

                return Ok("ファイルが正常にアップロードされました。");
            }
            catch (OperationCanceledException)
            {
                // クライアント側でキャンセルが発生した場合の処理
                await transaction.RollbackAsync();
                return StatusCode(StatusCodes.Status499ClientClosedRequest, "アップロードがキャンセルされました。");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // 他のエラー処理
               
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

                return await Task.FromResult(Ok("キャンセルが成功しました。"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message); // 無効なUploadIdの場合
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "キャンセル処理中にエラーが発生しました。");
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
