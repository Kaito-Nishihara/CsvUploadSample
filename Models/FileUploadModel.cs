using System.ComponentModel.DataAnnotations;

namespace CsvUploadSample.Models
{
    public class FileUploadModel
    {
        public string Description { get; set; }

        [Required(ErrorMessage = "ファイルを選択してください。")]
        [DataType(DataType.Upload)]
        public IFormFile File { get; set; }
    }

}
