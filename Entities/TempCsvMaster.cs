using CsvHelper.Configuration.Attributes;

namespace CsvUploadSample.Entities
{
#nullable disable
    public class TempCsvMaster : IHasUploadId
    {
        [Ignore]
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public int InternetId { get; set; }
        public DateTime CreateAt { get; set; }
        public string SubName { get; set; }
        public string SubDescription { get; set; }
        public string SubType { get; set; }
        public int SubInternetId { get; set; }
        public DateTime SubCreateAt { get; set; }

        [Ignore]
        public string UploadId { get ; set ; }
        [Ignore]
        public int RowNumber { get; set; }
    }
    public interface IHasUploadId
    {        
        public string UploadId { get; set; }
        public int RowNumber { get; set; }
    }
}
