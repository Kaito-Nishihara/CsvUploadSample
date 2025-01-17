namespace CsvUploadSample.Models
{
    public class CsvResultViewModel
    {
        public bool IsSuccess { get; set; }
        public List<CsvErrorViewModel> CsvErrors { get; set; }
    }
}
