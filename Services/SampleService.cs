using CsvUploadSample.Entities;
using CsvUploadSample.Models;

namespace CsvUploadSample.Services
{
    public interface ISampleService
    {
        Task<CsvResultViewModel> UploadCsv(IFormFile formFile);
    }
    public class SampleService(CsvAppDbContext dbContext,
        ICsvUploadService<TempCsvMaster, CsvMaster> csvMasterUploadService,
         ICsvUploadService<TempCsvMaster, SubMaster> csvSubMasterUploadService) : ISampleService
    {
        public async Task<CsvResultViewModel> UploadCsv(IFormFile formFile)
        {
            var result = new CsvResultViewModel { CsvErrors = new List<CsvErrorViewModel>() };
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            try
            {
                var csvReader = Util.CreateCsvReader(formFile.OpenReadStream());

                var viewModel = await csvMasterUploadService.ParseCsvAsync(csvReader, result.CsvErrors);
                await csvMasterUploadService.ProcessCsvFileAsyncNoTransaction(csvReader, viewModel, result.CsvErrors);
                await csvSubMasterUploadService.ProcessCsvFileAsyncNoTransaction(csvReader, viewModel, result.CsvErrors);

                if (!result.CsvErrors.Any())
                {
                    await transaction.CommitAsync();
                }
                else
                {
                    await transaction.RollbackAsync();
                }
            }
            catch (Exception ex) 
            {
                await transaction.RollbackAsync();
            }
            return result;
        }
    }
}
