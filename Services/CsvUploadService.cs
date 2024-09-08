using AutoMapper;
using CsvHelper;
using CsvUploadSample.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;

namespace CsvUploadSample.Services
{
    class CsvMasterMapper : CsvHelper.Configuration.ClassMap<TempCsvMaster>
    {
        public CsvMasterMapper()
        {
            AutoMap(CultureInfo.InvariantCulture);
        }
    }
    public interface ICsvUploadService<TTemp, TMain>
    {
        Task<string> UploadAndProcessFile(IFormFile file, IHubContext<ProgressHub> hubContext, CancellationToken cancellationToken, string uploadId);
        Task MigrateData(string uploadId, Expression<Func<TTemp, bool>> predicate);
        void CancelUpload(string uploadId);
    }

    public class CsvUploadService<TTemp, TMain> : ICsvUploadService<TTemp, TMain>
    where TTemp : class, IHasUploadId
    where TMain : class
    {
        private readonly CsvAppDbContext _context;
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _uploadTokens = new();
        private readonly IMapper _mapper;

        public CsvUploadService(CsvAppDbContext context)
        {
            _context = context;
            // コンストラクタ内でマッピング設定を行う
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<TTemp, TMain>().ForMember("Id", opt => opt.Ignore());
            });

            // 新しいIMapperインスタンスを作成
            _mapper = config.CreateMapper();
        }

        public async Task<string> UploadAndProcessFile(IFormFile file, IHubContext<ProgressHub> hubContext, CancellationToken cancellationToken, string uploadId)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("ファイルが無効です。");
            }

            var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _uploadTokens[uploadId] = tokenSource;
            //using var transaction = await _context.Database.BeginTransactionAsync(); // トランザクションの開始
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

            try
            {
                if (fileExtension == ".zip")
                {
                    await ProcessZipFile(file, hubContext, tokenSource.Token, uploadId);
                }
                else if (fileExtension == ".csv")
                {
                    await ProcessCsvFile(file.OpenReadStream(), hubContext, tokenSource.Token, uploadId);
                }

                return uploadId;
            }
            catch (OperationCanceledException)
            {
                //await transaction.RollbackAsync(); // キャンセルされた場合のロールバック処理
                throw; // キャンセルを上位に伝播
            }
            catch (Exception ex)
            {
                //await transaction.RollbackAsync(); // その他のエラーが発生した場合のロールバック処理
                throw new Exception("ファイルアップロード中にエラーが発生しました。", ex);
            }
            finally
            {                
                ThrowOperationCanceledException(uploadId);
                _uploadTokens.TryRemove(uploadId, out var _);// 処理が完了したらトークンを削除
            }
        }

        public async Task MigrateData(string uploadId, Expression<Func<TTemp, bool>> predicate)
        {
            var tempRecords = await _context.Set<TTemp>().Where(predicate).ToListAsync();
            var mainRecords = ConvertToMain(tempRecords);

            // データ移行処理（必要に応じてバリデーションを実施）
            _context.Set<TMain>().AddRange(mainRecords);
            await _context.SaveChangesAsync();

            // 一時テーブルのデータを削除
            _context.Set<TTemp>().RemoveRange(tempRecords);
            await _context.SaveChangesAsync();
        }

        public void CancelUpload(string uploadId)
        {
            if (_uploadTokens.TryGetValue(uploadId, out var tokenSource))
            {
                tokenSource.Cancel(); // キャンセルリクエストを送信
            }
            else
            {
                throw new ArgumentException("無効なUploadIdです。");
            }
        }

        private async Task ProcessZipFile(IFormFile zipFile, IHubContext<ProgressHub> hubContext, CancellationToken cancellationToken, string uploadId)
        {
            using (var memoryStream = new MemoryStream())
            {
                await zipFile.CopyToAsync(memoryStream, cancellationToken);

                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var entryStream = entry.Open())
                            {
                                await ProcessCsvFile(entryStream, hubContext, cancellationToken, uploadId);
                            }
                        }
                    }
                }
            }
        }

        private static void ThrowOperationCanceledException(string uploadId)
        {
            if(_uploadTokens[uploadId].IsCancellationRequested)
                throw new OperationCanceledException();
        }

        private async Task ProcessCsvFile(Stream csvStream, IHubContext<ProgressHub> hubContext, CancellationToken cancellationToken, string uploadId)
        {
            using var streamReader = new StreamReader(csvStream);
            using var csv = new CsvReader(streamReader, CultureInfo.InvariantCulture);
            csv.Context.RegisterClassMap<CsvMasterMapper>();
            var records = new List<TTemp>();
            var errorMessages = new List<string>();
            var rowNumber = 0;

            while (await csv.ReadAsync())
            {
                ThrowOperationCanceledException(uploadId);
                rowNumber++;

                try
                {
                    var record = csv.GetRecord<TTemp>();
                    record.UploadId = uploadId;
                    _context.Add(record);
                    
                    
                    // 進行状況を計算してクライアントに送信
                    var progress = (int)((rowNumber / (double)210814) * 100);
                    await hubContext.Clients.All.SendAsync("ReceiveProgress", progress);
                }
                catch (Exception ex)
                {
                    // エラーが発生した場合、その行とエラー内容を記録
                    var errorMessage = $"行番号: {rowNumber}, 項目: {GetFieldName(csv)}, エラー: {ex.Message}";
                    errorMessages.Add(errorMessage);
                }
            }
            await _context.SaveChangesAsync();

            if (errorMessages.Any())
            {
                throw new Exception(string.Join(Environment.NewLine, errorMessages));
            }
        }

        

        private IEnumerable<TMain> ConvertToMain(IEnumerable<TTemp> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            // AutoMapperを使用して、TTempのリストをTMainのリストにマッピング
            var mainRecords = records.Select(record => _mapper.Map<TMain>(record)).ToList();

            return mainRecords;
        }


        private string GetFieldName(CsvReader csv)
        {
            try
            {
                // 現在のフィールド名を取得
                return csv.Context.Reader?.HeaderRecord![csv.Context.Reader.CurrentIndex]!;
            }
            catch
            {
                return "不明";
            }
        }

        // 他のメソッド...
    }


}
