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
            using var transaction = await _context.Database.BeginTransactionAsync(); // �g�����U�N�V�����̊J�n
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
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }

        }

        [HttpPost]
        public async Task<IActionResult>  CancelUpload([FromBody] CancelUploadRequest request)
        {
            try
            {
                _csvUploadService.CancelUpload(request.UploadId);
                return Ok("�L�����Z�����������܂����B");
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
                        // �ꎞ�e�[�u���Ƀf�[�^��ǉ�
                        _context.TempCsvMasters.Add(record);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception("�f�[�^�C���|�[�g���ɃG���[���������܂����B", ex);
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
                // �G���[���b�Z�[�W�̐���
                var errorMessage = GenerateErrorMessage(invalidRecords);
                throw new Exception(errorMessage);
            }

            // �L���ȃf�[�^��{�ԃe�[�u���Ɉڍs
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

            // �ꎞ�e�[�u���̃f�[�^���폜
            _context.TempCsvMasters.RemoveRange(allRecords);
            await _context.SaveChangesAsync();
        }

        private bool IsValid(TempCsvMaster record)
        {
            // �o���f�[�V�������W�b�N�������Ɏ���
            return !string.IsNullOrEmpty(record.Name) && record.InternetId > 0;
        }

        private string GenerateErrorMessage(List<TempCsvMaster> invalidRecords)
        {
            var sb = new StringBuilder();
            sb.AppendLine("�o���f�[�V�����G���[���������܂���:");

            foreach (var record in invalidRecords)
            {
                sb.AppendLine($"Id: {record.Id}, Name: {record.Name}, �G���[���b�Z�[�W: [�����ɃG���[���e]");
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
