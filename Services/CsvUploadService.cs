using AutoMapper;
using CsvHelper;
using CsvUploadSample.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Net.Security;

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
        Task MigrateData(string uploadId, Expression<Func<TTemp, bool>> predicate, IHubContext<ProgressHub> hubContext);
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
                throw; // キャンセルを上位に伝播
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
            finally
            {                
                ThrowOperationCanceledException(uploadId);
                _uploadTokens.TryRemove(uploadId, out var _);// 処理が完了したらトークンを削除
            }
        }

        public async Task MigrateData(string uploadId, Expression<Func<TTemp, bool>> predicate, IHubContext<ProgressHub> hubContext)
        {
            var tempRecords = await _context.Set<TTemp>().Where(predicate).ToListAsync();
            var mainRecords = ConvertToMain(tempRecords);

            // データ移行処理（必要に応じてバリデーションを実施）
            _context.Set<TMain>().AddRange(mainRecords);
            await _context.SaveChangesAsync();
            await hubContext.Clients.All.SendAsync("ReceiveProgress", 70);
            ThrowOperationCanceledException(uploadId);
            // 一時テーブルのデータを削除
            _context.Set<TTemp>().RemoveRange(tempRecords);
            await _context.SaveChangesAsync();
            ThrowOperationCanceledException(uploadId);
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
            try
            {
                // CSVファイルをメモリに保持して再利用可能にする
                using var memoryStream = new MemoryStream();
                await csvStream.CopyToAsync(memoryStream);

                // ストリームの位置を先頭に戻す
                memoryStream.Position = 0;

                // ストリームリーダーとCsvReaderを利用
                using (var streamReader = new StreamReader(memoryStream))
                using (var csv = new CsvReader(streamReader, CultureInfo.InvariantCulture))
                {
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
                            record.RowNumber = rowNumber;
                            _context.Add(record);
                            ThrowOperationCanceledException(uploadId);
                            // 進捗を計算してクライアントに送信
                            //var progress = (int)((rowNumber / (double)totalCount) * 100);
                        }
                        catch (FormatException ex)
                        {
                            // フォーマットエラー（数値型などのフォーマットが間違っている場合）
                            throw new Exception($"行番号: {rowNumber}, 項目: {GetFieldName(csv)}, 無効な形式です");
                        }
                        catch (NullReferenceException ex)
                        {
                            // null 参照エラー（必須項目がnullの場合など）
                            throw new Exception($"行番号: {rowNumber}, 項目: {GetFieldName(csv)}, 必須項目です");
                        }
                        catch (Exception ex)
                        {
                            // どれにも該当しない場合
                            throw new Exception($"行番号: {rowNumber}, 項目: {GetFieldName(csv)}, エラーが発生しました");
                        }
                    }
                    await _context.SaveChangesAsync();
                    ThrowOperationCanceledException(uploadId);
                    if (errorMessages.Any())
                    {
                        throw new Exception(string.Join(Environment.NewLine, errorMessages));
                    }
                }
            }
            catch (DbUpdateException dbEx)
            {
                // 失敗したエンティティを調べる
                // エラーが起きたレコードを調査することは可能だが、具体的なカラムを算出することは難しい
                foreach (var entry in dbEx.Entries)
                {
                    var failedRecord = entry.Entity as TempCsvMaster;
                    if (failedRecord != null)
                    {
                        var rowNumber = failedRecord.RowNumber;
                        throw new Exception($"行番号 {rowNumber} でエラーが発生しました。");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
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
    }


}
