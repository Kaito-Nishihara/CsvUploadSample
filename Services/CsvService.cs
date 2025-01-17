using AutoMapper;
using CsvHelper.Configuration.Attributes;
using CsvHelper;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CsvUploadSample.Models;
using CsvUploadSample.Entities;
using System.Globalization;
using System.Text;

namespace CsvUploadSample.Services
{
    /// <summary>
    /// CSVアップロードインタフェース
    /// </summary>
    /// <typeparam name="TViewModel"></typeparam>
    /// <typeparam name="TEntity"></typeparam>
    public interface ICsvUploadService<TViewModel, TEntity>
    {
        Task<CsvResultViewModel> UploadAndProcessFileAsync(IFormFile file, string informationName, Dictionary<string, object> columnValues = null, bool enableParallelProcessing = false);
        Task<CsvResultViewModel> UploadAndProcessFileAsyncNotTransaction(IFormFile file, string informationName, Dictionary<string, object> columnValues = null, bool enableParallelProcessing = false);
        CsvResultViewModel CreateCsvResultFromModelState(ModelStateDictionary modelState);
        Task<List<TViewModel>> ParseCsvAsync(
            CsvReader csvReader,
            List<CsvErrorViewModel> csvErrorViewModels);
        Task ProcessCsvFileAsyncNoTransaction(
            CsvReader csvReader,
            List<TViewModel> viewModels,
            List<CsvErrorViewModel> csvErrorViewModels,
            Dictionary<string, object> columnValues = null
            );
    }

    /// <summary>
    /// CSVアップロードサービス
    /// </summary>
    /// <typeparam name="TViewModel"></typeparam>
    /// <typeparam name="TEntity"></typeparam>
    public sealed class CsvUploadService<TViewModel, TEntity> : ICsvUploadService<TViewModel, TEntity>
        where TViewModel : class
        where TEntity : new()
    {
        private readonly CsvAppDbContext _context;
        private readonly IMapper _mapper;
        private readonly DbContextOptions<CsvAppDbContext> _options;

        public CsvUploadService(CsvAppDbContext context,  DbContextOptions<CsvAppDbContext> options)
        {
            _context = context;
            _options = options;
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<TViewModel, TEntity>();
            });
            _mapper = config.CreateMapper();
        }

        /// <summary>
        /// 指定した CSV ファイルをアップロード
        /// </summary>
        /// <param name="file">アップロードされた CSV ファイル。</param>
        /// <param name="informationName">処理対象の情報名。エラーメッセージに使用されます。</param>
        /// <param name="columnValues">レコード検証に使用するカラムの値の辞書（任意）。</param>
        /// <param name="enableParallelProcessing">並列処理を実行するか逐次処理を実行するかのフラグ。</param>
        /// <returns>処理結果およびエラー情報を含む CsvResultViewModel。</returns>
        public async Task<CsvResultViewModel> UploadAndProcessFileAsync(IFormFile file, string informationName, Dictionary<string, object> columnValues = null, bool enableParallelProcessing = false)
        {
            var result = new CsvResultViewModel { CsvErrors = new List<CsvErrorViewModel>() };
            try
            {
                // CSVファイル処理を実行
                // フラグに基づき並列処理を実行するか逐次処理を実行するかを切り替える
                //NOTE : 原則逐次処理、数百万とかのデータを扱い逐次では処理が遅くなる場合のみ並列処理を使用してください。
                result.CsvErrors = await ProcessCsvFileAsync(file.OpenReadStream(), columnValues); // 逐次処理

                // エラーがなければ成功とする
                result.IsSuccess = !result.CsvErrors.Any();
            }
            catch (Exception ex)
            {
                // 想定外のException場合はLoggerでError出力する、アプリとしてエラー画面には飛ばさない
                result.IsSuccess = false;
                result.CsvErrors.Add(new CsvErrorViewModel(0, string.Format("Error0001{0}", informationName)));
                //n1Logger.WriteError(ex.Message, _httpContextAccessor.HttpContext, ex);
            }
            return result;
        }

        public async Task<CsvResultViewModel> UploadAndProcessFileAsyncNotTransaction(IFormFile file, string informationName, Dictionary<string, object> columnValues = null, bool enableParallelProcessing = false)
        {
            var result = new CsvResultViewModel { CsvErrors = new List<CsvErrorViewModel>() };
            try
            {
                // CSVファイル処理を実行
                // フラグに基づき並列処理を実行するか逐次処理を実行するかを切り替える
                //NOTE : 原則逐次処理、数百万とかのデータを扱い逐次では処理が遅くなる場合のみ並列処理を使用してください。
                result.CsvErrors = await ProcessCsvFileAsync(file.OpenReadStream(), columnValues); // 逐次処理

                // エラーがなければ成功とする
                result.IsSuccess = !result.CsvErrors.Any();
            }
            catch (Exception ex)
            {
                // 想定外のException場合はLoggerでError出力する、アプリとしてエラー画面には飛ばさない
                result.IsSuccess = false;
                result.CsvErrors.Add(new CsvErrorViewModel(0, "Messages.N10001{0},"));
            }
            return result;
        }

        /// <summary>
        /// ModelStateDictionary からエラーメッセージ取得
        /// </summary>
        /// <param name="modelState">エラーを含む ModelStateDictionary オブジェクト。</param>
        /// <returns>エラー情報を含む CsvResultViewModel インスタンス。</returns>
        public CsvResultViewModel CreateCsvResultFromModelState(ModelStateDictionary modelState)
        {
            var csvErrors = modelState.SelectMany(state => state.Value!.Errors.Select(error =>
                new CsvErrorViewModel
                {
                    RowNumber = 0,
                    Error = error.ErrorMessage,
                    PropertyName = "添付ファイル"
                }
            )).ToList();

            return new CsvResultViewModel
            {
                IsSuccess = false,
                CsvErrors = csvErrors
            };
        }



        /// <summary>
        /// 取り込んだCSVファイルをDBに保存
        /// </summary>
        /// <param name="csvStream">CSVファイルのストリーム</param>
        /// <param name="columnValues">カラム名と設定する値のペア（例: "CreateUser" と "csv upload"）</param>
        /// <returns>バリデーションエラーや処理エラーを含む CsvErrorViewModel のリスト</returns>
        private async Task<List<CsvErrorViewModel>> ProcessCsvFileAsync(Stream csvStream, Dictionary<string, object> columnValues)
        {
            var csvErrorViewModels = new List<CsvErrorViewModel>();
            var strategy = _context.Database.CreateExecutionStrategy();

            // ExecutionStrategy でトランザクション全体を実行
            await strategy.ExecuteAsync(async () =>
            {
                // トランザクションのスコープ
                await using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    using var csvReader = Util.CreateCsvReader(csvStream);

                    await foreach (var (viewModel, rowNumber) in ReadCsvRecordsAsync(csvReader, csvErrorViewModels))
                    {
                        // バリデーションエラーチェック                        
                        if (!ValidateRecord(viewModel, rowNumber, csvErrorViewModels)) continue;

                        // エラーが100件以上の場合は処理を終了
                        if (csvErrorViewModels.Count >= 100)
                        {
                            await transaction.RollbackAsync(); // トランザクションをロールバック
                            return;
                        }

                        // エラーがなければ保存処理に入る
                        if (!csvErrorViewModels.Any())
                        {
                            try
                            {
                                var entityRecord = _mapper.Map<TEntity>(viewModel);

                                if (columnValues is not null)
                                {
                                    Util.SetEntityPropertyValues(entityRecord, columnValues);
                                }

                                await _context.AddAsync(entityRecord);
                                await _context.SaveChangesAsync();
                            }
                            catch (Exception ex)
                            {
                                HandleCsvProcessingException(ex, rowNumber, csvReader, csvErrorViewModels);
                            }
                        }
                    }
                    // エラーがない場合のみコミット
                    if (!csvErrorViewModels.Any())
                    {
                        await transaction.CommitAsync();
                    }
                    else
                    {
                        await transaction.RollbackAsync(); // エラーがある場合はロールバック
                    }
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw; // 例外を再スローしてリトライ可能にする
                }
            });

            return csvErrorViewModels;
        }



        /// <summary>
        /// 非同期にCSVファイルを、各レコードをTViewModel型に変換
        /// </summary>
        /// <param name="csvReader">CSVファイルを読み取るCsvReaderインスタンス</param>
        /// <param name="csvErrorViewModels">エラーメッセージを収集するリスト</param>
        /// <returns>CSVファイルから取得した各レコードとその行番号のタプル</returns>
        private async IAsyncEnumerable<(TViewModel Record, int RowNumber)> ReadCsvRecordsAsync(CsvReader csvReader, List<CsvErrorViewModel> csvErrorViewModels)
        {
            int rowNumber = 1;
            while (await csvReader.ReadAsync())
            {
                TViewModel record;
                try
                {
                    // CSVレコードをTViewModelに変換
                    record = csvReader.GetRecord<TViewModel>();
                }
                catch (Exception ex)
                {
                    // 読み取りエラーをログとして追加し、次のレコードに進む
                    HandleCsvProcessingException(ex, rowNumber, csvReader, csvErrorViewModels);
                    rowNumber++; // 次の行番号にインクリメント
                    continue; // 次のレコードへ
                }

                yield return (record, rowNumber);
                rowNumber++; // 次の行番号にインクリメント
            }
        }

        /// <summary>
        /// TViewModel レコードのAttribute設定に基づくバリデーションチェック
        /// </summary>
        /// <param name="viewModelRecord">バリデーションを実行する TViewModel レコード</param>
        /// <param name="rowNumber">現在のレコードの行番号</param>
        /// <param name="errorMessages">エラーメッセージを収集するリスト</param>
        private bool ValidateRecord(TViewModel viewModelRecord, int rowNumber, List<CsvErrorViewModel> errorMessages)
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(viewModelRecord);

            // データ注釈に基づいてバリデーションを実行
            bool isValid = Validator.TryValidateObject(
                viewModelRecord,
                validationContext,
                validationResults,
                validateAllProperties: true
            );

            // エラーメッセージの収集
            if (!isValid)
            {
                foreach (var validationResult in validationResults)
                {
                    var propertyNames = validationResult.MemberNames.Select(memberName =>
                    {
                        var property = viewModelRecord.GetType().GetProperty(memberName);
                        var nameAttribute = property?.GetCustomAttribute<NameAttribute>();
                        return nameAttribute != null ? string.Join(", ", nameAttribute.Names) : memberName;
                    });

                    var displayName = string.Join(", ", propertyNames); // 複数のプロパティが関与する場合も対応
                    var errorMessage = ReplaceCsvValidateMessage(validationResult.ErrorMessage, validationResult.MemberNames.FirstOrDefault());
                    errorMessages.Add(new CsvErrorViewModel(rowNumber, errorMessage, displayName));
                }
            }
            return isValid;
        }

        

        /// <summary>
        /// CSVレコードの処理中に発生した例外をエラーメッセージリストに追加
        /// </summary>
        /// <param name="ex">発生した例外</param>
        /// <param name="rowNumber">エラーが発生した行番号</param>
        /// <param name="csv">現在のCSVリーダーオブジェクト</param>
        /// <param name="errorMessages">エラーメッセージを追加するためのリスト</param>
        private void HandleCsvProcessingException(Exception ex, int rowNumber, CsvReader csv, List<CsvErrorViewModel> errorMessages)
        {
            // デフォルトのフィールド名を取得
            var fieldName = Util.GetFieldName(csv);

            // 例外に応じたエラーメッセージとフィールド名を設定
            var (errorMessage, adjustedFieldName) = ex switch
            {
                CsvHelper.TypeConversion.TypeConverterException => (GetSpecificErrorMessageForType(fieldName), fieldName),
                FormatException => (ReplaceCsvValidateMessage("Messages.E10003{0}"), fieldName),
                DbUpdateException  => (ReplaceCsvValidateMessage("Messages.E10012{0}"), "-"),
                _ => (ReplaceCsvValidateMessage("Messages.N10001{0}"), "-")
            };

            // エラーメッセージを追加
            errorMessages.Add(new CsvErrorViewModel(rowNumber, errorMessage, adjustedFieldName));
        }

        /// <summary>
        /// プロパティの型情報に応じたエラーメッセージを生成
        /// </summary>
        /// <param name="fieldName">エラーの対象となるフィールド名</param>
        /// <returns>型に基づいた具体的なエラーメッセージ</returns>
        private string GetSpecificErrorMessageForType(string fieldName)
        {
            // TViewModelのプロパティを取得し、Name属性をチェックしてフィールド名に一致するプロパティの型を特定
            var property = typeof(TViewModel).GetProperties()
                .FirstOrDefault(prop =>
                    prop.GetCustomAttribute<NameAttribute>()?.Names.Contains(fieldName) ?? false);

            if (property is null)
            {
                return ReplaceCsvValidateMessage("Messages.E10003{0}");
            }

            // プロパティの型に応じたエラーメッセージを返す
            return property.PropertyType switch
            {
                Type t when t == typeof(int) => ReplaceCsvValidateMessage("Messages.E10006{0}"),
                Type t when t == typeof(decimal) => ReplaceCsvValidateMessage("Messages.E10006{0}"),
                Type t when t == typeof(DateTime) => ReplaceCsvValidateMessage("Messages.E10006{0}"),
                Type t when t == typeof(DateOnly) => ReplaceCsvValidateMessage("Messages.E10006{0}"),
                _ => ReplaceCsvValidateMessage("Messages.E10006{0}")
            };
        }

        /// <summary>
        /// CSV のバリデーションメッセージ用に修正
        /// </summary>
        /// <param name="input">バリデーションメッセージ。</param>
        /// <param name="memberName">削除対象となるメンバ名（既定値: "{0}"）。</param>
        /// <returns>メンバ名に関連する部分が削除されたメッセージ。</returns>
        private string ReplaceCsvValidateMessage(string input, string memberName = "{0}")
        {
            // 置換対象のパターンリスト
            var patternsToRemove = new[] { $"{memberName}の", $"{memberName}を", $"{memberName}は", $"{memberName}が" };

            // 各パターンを削除
            foreach (var pattern in patternsToRemove)
            {
                input = input.Replace(pattern, string.Empty);
            }

            return input;
        }

     
        public async Task<List<TViewModel>> ParseCsvAsync(
            CsvReader csvReader,
            List<CsvErrorViewModel> csvErrorViewModels)
        {
            var records = new List<TViewModel>(); // 結果を格納するリスト
            int rowNumber = 1; // 行番号を追跡

            while (await csvReader.ReadAsync())
            {
                TViewModel record;

                try
                {
                    // CSVレコードを TViewModel に変換
                    record = csvReader.GetRecord<TViewModel>();
                    rowNumber++; // 次の行番号にインクリメント
                    // 有効なレコードをリストに追加
                    records.Add(record);
                }
                catch (Exception ex)
                {
                    // 読み取りエラーをログとして追加し、次のレコードに進む
                    HandleCsvProcessingException(ex, rowNumber, csvReader, csvErrorViewModels);
                }

                rowNumber++; // 次の行番号にインクリメント
            }

            return records;
        }


        public async Task ProcessCsvFileAsyncNoTransaction(
            CsvReader csvReader,
            List<TViewModel> viewModels,
            List<CsvErrorViewModel> csvErrorViewModels,
            Dictionary<string, object> columnValues = null)
        {
            try
            {
                int rowNumber = 1; // 行番号を追跡
                foreach (var viewModel in viewModels)
                {
                    // バリデーションエラーチェック                        
                    if (!ValidateRecord(viewModel, rowNumber, csvErrorViewModels)) continue;

                    // エラーが100件以上の場合は処理を終了
                    if (csvErrorViewModels.Count >= 100)
                    {
                        return;
                    }

                    // エラーがなければ保存処理に入る
                    if (!csvErrorViewModels.Any())
                    {
                        try
                        {
                            var entityRecord = _mapper.Map<TEntity>(viewModel);

                            if (columnValues is not null)
                            {
                                Util.SetEntityPropertyValues(entityRecord, columnValues);
                            }

                            await _context.AddAsync(entityRecord);
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            HandleCsvProcessingException(ex, rowNumber, csvReader, csvErrorViewModels);
                        }
                    }
                    rowNumber++;
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }

        }


    }
}
