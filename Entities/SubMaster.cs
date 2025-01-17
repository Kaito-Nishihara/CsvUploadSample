using CsvHelper.Configuration.Attributes;

namespace CsvUploadSample.Entities
{
#nullable disable
    public class SubMaster
    {
        [Ignore]
        public int Id { get; set; }
        public string SubName { get; set; }
        public string SubDescription { get; set; }
        public string SubType { get; set; }
        public int SubInternetId { get; set; }
        public DateTime SubCreateAt { get; set; }
    }
}
