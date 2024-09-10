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
        private readonly IHubContext<ProgressHub> _hubContext;

        public HomeController(ILogger<HomeController> logger, CsvAppDbContext dbContext, ICsvUploadService<TempCsvMaster,CsvMaster> csvUploadService, IHubContext<ProgressHub> hubContext)
        {
            _logger = logger;
            _context = dbContext;
            _csvUploadService = csvUploadService;
            _hubContext = hubContext;
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
            using var transaction = await _context.Database.BeginTransactionAsync(); // トランザクションの開始
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
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }

        }

        [HttpPost]
        public async Task<IActionResult>  CancelUpload([FromBody] CancelUploadRequest request)
        {
            try
            {
                _csvUploadService.CancelUpload(request.UploadId);
                return Ok("キャンセルが成功しました。");
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



        private async Task ImportCsvToTemporaryTable(StreamReader streamReader)
        {
            using var csv = new CsvReader(streamReader, CultureInfo.InvariantCulture);

            var records = csv.GetRecords<TempCsvMaster>().ToList();

            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    foreach (var record in records)
                    {
                        // 一時テーブルにデータを追加
                        _context.TempCsvMasters.Add(record);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception("データインポート中にエラーが発生しました。", ex);
                }
            }
        }


        private async Task ValidateAndProcessData()
        {
            var invalidRecords = new List<TempCsvMaster>();
            var validRecords = new List<TempCsvMaster>();

            var allRecords = await _context.TempCsvMasters.ToListAsync();

            foreach (var record in allRecords)
            {
                if (IsValid(record))
                {
                    validRecords.Add(record);
                }
                else
                {
                    invalidRecords.Add(record);
                }
            }

            if (invalidRecords.Any())
            {
                // エラーメッセージの生成
                var errorMessage = GenerateErrorMessage(invalidRecords);
                throw new Exception(errorMessage);
            }

            // 有効なデータを本番テーブルに移行
            var productionRecords = validRecords.Select(record => new CsvMaster
            {
                Name = record.Name,
                Description = record.Description,
                Type = record.Type,
                InternetId = record.InternetId,
                CreateAt = record.CreateAt
            }).ToList();

            _context.CsvMasters.AddRange(productionRecords);
            await _context.SaveChangesAsync();

            // 一時テーブルのデータを削除
            _context.TempCsvMasters.RemoveRange(allRecords);
            await _context.SaveChangesAsync();
        }

        private bool IsValid(TempCsvMaster record)
        {
            // バリデーションロジックをここに実装
            return !string.IsNullOrEmpty(record.Name) && record.InternetId > 0;
        }

        private string GenerateErrorMessage(List<TempCsvMaster> invalidRecords)
        {
            var sb = new StringBuilder();
            sb.AppendLine("バリデーションエラーが発生しました:");

            foreach (var record in invalidRecords)
            {
                sb.AppendLine($"Id: {record.Id}, Name: {record.Name}, エラーメッセージ: [ここにエラー内容]");
            }

            return sb.ToString();
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
