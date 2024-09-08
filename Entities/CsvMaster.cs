using CsvHelper.Configuration.Attributes;

namespace CsvUploadSample.Entities
{
#nullable disable
    public class CsvMaster
    {
        [Ignore]
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public int InternetId { get; set; }
        public DateTime CreateAt {  get; set; }
    }
}
